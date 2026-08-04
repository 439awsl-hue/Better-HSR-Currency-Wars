import json
import sys
from io import BytesIO
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def to_float(value):
    try:
        return float(value)
    except Exception:
        return 0.0


def bounds_from_points(points):
    xs = [to_float(point[0]) for point in points]
    ys = [to_float(point[1]) for point in points]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    return {
        "x": left,
        "y": top,
        "width": max(0.0, right - left),
        "height": max(0.0, bottom - top),
    }


def recognize(ocr, image):
    result, _elapsed = ocr(image)
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
                "bounds": bounds_from_points(points),
            }
        )

    return {"raw_text": "\n".join(raw_lines), "items": items}


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


def decode_image(image_bytes):
    import numpy as np
    from PIL import Image

    with Image.open(BytesIO(image_bytes)) as image:
        rgb = image.convert("RGB")
        # rapidocr_onnxruntime 接收 ndarray 时沿用 OpenCV 的 BGR 通道顺序。
        return np.asarray(rgb)[:, :, ::-1].copy()


def run_server():
    from rapidocr_onnxruntime import RapidOCR

    ocr = RapidOCR()
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
        from rapidocr_onnxruntime import RapidOCR

        print(json.dumps(recognize(RapidOCR(), str(image_path)), ensure_ascii=False))
        return 0
    except Exception as exc:
        print(json.dumps({"raw_text": "", "items": [], "error": str(exc)}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
