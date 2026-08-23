"""
rapidocr_bridge.py  （优化版，降低 OCR 系统负荷）

协议保持与 V12.8 原版完全一致：
  - 服务模式：stdin 按行读 JSON 请求头 {"image_size": N}，随后跟 N 字节 PNG 数据；
    处理结果以一行 JSON 写到 stdout。全程内存管道，不写临时文件。
  - 命令行模式：python rapidocr_bridge.py <image_path>，结果打印到 stdout。

主要优化点（针对 OCR 执行时系统负载过大问题）：

1. 限制 ONNX Runtime 线程数
   - 构造 RapidOCR 时传入 intra_op_num_threads / inter_op_num_threads
   - 通过 OMP_NUM_THREADS / MKL_NUM_THREADS 等环境变量限制 OpenMP 线程
   - 默认 = CPU 逻辑核数 // 2，避免 ONNX Runtime 占满全部 CPU 核心

2. 降低检测模型输入分辨率
   - det_limit_side_len 默认 736（原版 960），检测模型是 OCR 中最耗资源的部分

3. 关闭角度分类器 (use_cls=False)
   - 游戏内 UI 文字基本为水平正向，关闭 cls 模型可省去一次模型推理

4. 图像预下采样
   - 输入图像最长边 > max_image_side（默认 1280）时先按比例缩小再送 OCR
   - 避免超大截图（例如 4K 全屏）直接灌入检测模型

5. 性能日志（默认关闭，通过环境变量 RAPIDOCR_PRINT_PERF=true 开启）
   - 每次 OCR 的 load/pre/ocr 耗时、图像尺寸、缩放比例
   - 日志走 stderr，不影响 stdout 的 JSON 响应协议

6. 进程级单例 RapidOCR
   - 服务模式下 RapidOCR 实例只创建一次，避免每次请求重新加载 ONNX 模型

可调环境变量（默认值见下）：
  RAPIDOCR_INTRA_OP_THREADS   默认 CPU核数//2   ONNX 单图推理线程数
  RAPIDOCR_INTER_OP_THREADS   默认 1             ONNX 跨算子并行度
  RAPIDOCR_OMP_THREADS        默认 CPU核数//2   OpenMP/OpenCV 线程数
  RAPIDOCR_DET_LIMIT_SIDE_LEN 默认 736           检测模型输入边长
  RAPIDOCR_DET_LIMIT_TYPE     默认 max           检测缩放模式
  RAPIDOCR_USE_CLS            默认 false         角度分类开关
  RAPIDOCR_MAX_IMAGE_SIDE     默认 1280          图像最长边下采样阈值
  RAPIDOCR_PRINT_PERF         默认 false         性能日志开关
"""

import json
import os
import sys
import time
from io import BytesIO
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


# ---------------------------------------------------------------------------
# 可调参数（优先读取环境变量，便于不重新打包 EXE 的情况下调优）
# ---------------------------------------------------------------------------
def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def _env_bool(name: str, default: bool) -> bool:
    raw = os.environ.get(name)
    if not raw:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


# CPU 线程数：默认取逻辑核数的一半，避免 ONNX Runtime 把所有核心拉满
_DEFAULT_THREADS = max(1, (os.cpu_count() or 4) // 2)
INTRA_OP_THREADS = _env_int("RAPIDOCR_INTRA_OP_THREADS", _DEFAULT_THREADS)
INTER_OP_THREADS = _env_int("RAPIDOCR_INTER_OP_THREADS", 1)
OMP_THREADS = _env_int("RAPIDOCR_OMP_THREADS", _DEFAULT_THREADS)

# 检测模型输入边长限制（越小越快，但小文字可能漏检）
DET_LIMIT_SIDE_LEN = _env_int("RAPIDOCR_DET_LIMIT_SIDE_LEN", 736)
DET_LIMIT_TYPE = os.environ.get("RAPIDOCR_DET_LIMIT_TYPE", "max")

# 是否启用角度分类（游戏 UI 文字基本水平，默认关闭以节省一次推理）
USE_CLS = _env_bool("RAPIDOCR_USE_CLS", False)

# 输入图像预下采样阈值（最长边超过此值则按比例缩小）
MAX_IMAGE_SIDE = _env_int("RAPIDOCR_MAX_IMAGE_SIDE", 1280)

# 是否打印性能日志（调试用；走 stderr，不影响 stdout 响应协议）
PRINT_PERF = _env_bool("RAPIDOCR_PRINT_PERF", False)


def _apply_thread_env() -> None:
    """在导入 onnxruntime / opencv 之前设置 OpenMP 线程数。"""
    os.environ.setdefault("OMP_NUM_THREADS", str(OMP_THREADS))
    os.environ.setdefault("MKL_NUM_THREADS", str(OMP_THREADS))
    os.environ.setdefault("OPENCV_OPEN_THREADS_NUM", str(OMP_THREADS))
    os.environ.setdefault("OPENCV_FOR_THREADS_NUM", str(OMP_THREADS))


_apply_thread_env()


# ---------------------------------------------------------------------------
# RapidOCR 实例（懒加载 + 单例）
# ---------------------------------------------------------------------------
_OCR_INSTANCE = None


def _build_ocr():
    """构造一个优化参数的 RapidOCR 实例（对版本差异做兼容处理）。"""
    try:
        from rapidocr_onnxruntime import RapidOCR
    except ModuleNotFoundError:
        _debug_print(
            "[rapidocr_bridge] 未找到 rapidocr_onnxruntime 模块。"
            "请使用打包的 OCRRuntime\\rapidocr_bridge\\rapidocr_bridge.exe 运行，"
            "或先安装依赖：pip install -r Tools\\OCRBuild\\requirements.txt"
        )
        raise

    kwargs = {
        "det_limit_side_len": DET_LIMIT_SIDE_LEN,
        "det_limit_type": DET_LIMIT_TYPE,
        "intra_op_num_threads": INTRA_OP_THREADS,
        "inter_op_num_threads": INTER_OP_THREADS,
        "use_cls": USE_CLS,
    }

    # 版本兼容：
    # - rapidocr 1.3.x 构造签名是 (config_path=None, **kwargs)，参数通过 UpdateParameters
    #   映射到配置（det_* → Det、use_cls → Global、线程参数 → Global→Det/Cls/Rec），必须全部透传；
    # - 更早版本可能有显式参数名，此时只保留显式支持的参数，避免 TypeError。
    try:
        import inspect

        parameters = inspect.signature(RapidOCR.__init__).parameters
        if "kwargs" not in parameters:
            kwargs = {key: value for key, value in kwargs.items() if key in parameters}
    except Exception:
        pass

    try:
        return RapidOCR(**kwargs)
    except TypeError:
        # 极少数旧版本即使过滤后仍不接受某些参数，退回默认构造，保证进程能启动
        _debug_print("[rapidocr_bridge] 当前版本不支持优化参数，已退回默认构造。")
        return RapidOCR()


def get_ocr():
    global _OCR_INSTANCE
    if _OCR_INSTANCE is None:
        _OCR_INSTANCE = _build_ocr()
        _debug_print(
            "[rapidocr_bridge] OCR 初始化完成："
            f"intra={INTRA_OP_THREADS} inter={INTER_OP_THREADS} omp={OMP_THREADS} "
            f"det_limit_side_len={DET_LIMIT_SIDE_LEN} det_limit_type={DET_LIMIT_TYPE} "
            f"use_cls={USE_CLS} max_image_side={MAX_IMAGE_SIDE} print_perf={PRINT_PERF}"
        )
    return _OCR_INSTANCE


def _debug_print(message: str) -> None:
    """调试信息统一走 stderr，避免污染 stdout 的 JSON 响应通道。"""
    try:
        print(message, file=sys.stderr, flush=True)
    except Exception:
        pass


# ---------------------------------------------------------------------------
# 图像预处理：解码 + 预下采样
# ---------------------------------------------------------------------------
def decode_image(image_bytes: bytes):
    """把 PNG 字节解码为 BGR ndarray（RapidOCR 所需通道顺序）。"""
    import numpy as np

    # 优先 OpenCV（更快），不可用时退回 PIL
    try:
        import cv2

        mat = cv2.imdecode(np.frombuffer(image_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
        if mat is not None:
            return mat
    except Exception:
        pass

    from PIL import Image

    with Image.open(BytesIO(image_bytes)) as image:
        rgb = image.convert("RGB")
        return np.asarray(rgb)[:, :, ::-1].copy()


def _downscale_if_needed(mat):
    """最长边超过 MAX_IMAGE_SIDE 时按比例下采样。返回 (mat, scale)。"""
    height, width = mat.shape[:2]
    longest = max(height, width)
    if longest <= MAX_IMAGE_SIDE:
        return mat, 1.0
    scale = MAX_IMAGE_SIDE / float(longest)
    new_width = max(1, int(round(width * scale)))
    new_height = max(1, int(round(height * scale)))
    try:
        import cv2

        return cv2.resize(mat, (new_width, new_height), interpolation=cv2.INTER_AREA), scale
    except Exception:
        import numpy as np
        from PIL import Image

        pil = Image.fromarray(mat[:, :, ::-1])
        resized = pil.resize((new_width, new_height), Image.LANCZOS)
        return np.asarray(resized)[:, :, ::-1].copy(), scale


def _load_image_from_path(image_path: str):
    """从文件读图（支持非 ASCII 路径），返回 BGR ndarray。"""
    import numpy as np

    try:
        import cv2

        mat = cv2.imdecode(np.fromfile(image_path, dtype=np.uint8), cv2.IMREAD_COLOR)
        if mat is not None:
            return mat
    except Exception:
        pass

    from PIL import Image

    with Image.open(image_path) as image:
        rgb = image.convert("RGB")
        return np.asarray(rgb)[:, :, ::-1].copy()


# ---------------------------------------------------------------------------
# OCR 核心
# ---------------------------------------------------------------------------
def to_float(value):
    try:
        return float(value)
    except Exception:
        return 0.0


def bounds_from_points(points, scale=1.0):
    xs = [to_float(point[0]) for point in points]
    ys = [to_float(point[1]) for point in points]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    # 坐标是下采样后的，需要映射回原图坐标系
    return {
        "x": left / scale,
        "y": top / scale,
        "width": max(0.0, (right - left) / scale),
        "height": max(0.0, (bottom - top) / scale),
    }


def recognize(ocr, image):
    """OCR 核心入口。image 为已解码的 BGR ndarray。"""
    t0 = time.perf_counter()
    downscaled, scale = _downscale_if_needed(image)
    pre_ms = (time.perf_counter() - t0) * 1000.0

    t1 = time.perf_counter()
    result, _elapsed = ocr(downscaled)
    ocr_ms = (time.perf_counter() - t1) * 1000.0

    items = []
    raw_lines = []
    for row in result or []:
        if len(row) < 3:
            continue
        points, text, confidence = row[0], str(row[1]), to_float(row[2])
        if not text.strip():
            continue
        raw_lines.append(text)
        items.append(
            {
                "text": text,
                "confidence": confidence,
                "bounds": bounds_from_points(points, scale),
            }
        )

    response = {"raw_text": "\n".join(raw_lines), "items": items}
    if PRINT_PERF:
        height, width = image.shape[:2]
        perf = {
            "pre_ms": round(pre_ms, 1),
            "ocr_ms": round(ocr_ms, 1),
            "image_w": int(width),
            "image_h": int(height),
            "scale": round(scale, 4),
            "items": len(items),
        }
        response["perf"] = perf
        _debug_print(
            "[rapidocr_bridge][perf] "
            f"pre={perf['pre_ms']}ms ocr={perf['ocr_ms']}ms "
            f"image={perf['image_w']}x{perf['image_h']} scale={perf['scale']} items={perf['items']}"
        )
    return response


def read_exact(stream, length):
    chunks = []
    remaining = length
    while remaining > 0:
        chunk = stream.read(remaining)
        if not chunk:
            raise EOFError(f"image stream ended early: expected {length} bytes")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


# ---------------------------------------------------------------------------
# 服务模式：常驻进程，按行读取 JSON 请求头，随后读取定长图像字节
# ---------------------------------------------------------------------------
def run_server():
    ocr = get_ocr()
    _debug_print(
        "[rapidocr_bridge] 服务启动：常驻 OCR 进程就绪，等待请求。"
    )
    input_stream = sys.stdin.buffer
    while True:
        header_line = input_stream.readline()
        if not header_line:
            break
        header_line = header_line.strip().lstrip(b"\xef\xbb\xbf")
        if not header_line:
            continue
        try:
            request = json.loads(header_line.decode("utf-8"))
            if request.get("command") == "shutdown":
                print(json.dumps({"ok": True}, ensure_ascii=False), flush=True)
                break

            image_size = int(request.get("image_size", 0))
            if image_size <= 0:
                response = {"raw_text": "", "items": [], "error": "missing image bytes"}
            else:
                image_bytes = read_exact(input_stream, image_size)
                response = recognize(ocr, decode_image(image_bytes))
            print(json.dumps(response, ensure_ascii=False), flush=True)
        except Exception as exc:
            _debug_print(f"[rapidocr_bridge] 请求处理异常：{exc!r}")
            print(json.dumps({"raw_text": "", "items": [], "error": str(exc)}, ensure_ascii=False), flush=True)


def main():
    if len(sys.argv) >= 2 and sys.argv[1] == "--server":
        run_server()
        return 0

    if len(sys.argv) < 2:
        print(json.dumps({"raw_text": "", "items": [], "error": "missing image path"}, ensure_ascii=False))
        return 2

    image_path = Path(sys.argv[1])
    if not image_path.exists():
        print(json.dumps({"raw_text": "", "items": [], "error": "image not found"}, ensure_ascii=False))
        return 3

    try:
        mat = _load_image_from_path(str(image_path))
        print(json.dumps(recognize(get_ocr(), mat), ensure_ascii=False))
        return 0
    except Exception as exc:
        print(json.dumps({"raw_text": "", "items": [], "error": str(exc)}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
