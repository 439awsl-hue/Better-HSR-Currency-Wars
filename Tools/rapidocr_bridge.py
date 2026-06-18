import json
import sys
from pathlib import Path

if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
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


def recognize(ocr, image_path):
    result, _elapsed = ocr(str(image_path))
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


def run_server():
    from rapidocr_onnxruntime import RapidOCR

    ocr = RapidOCR()
    for line in sys.stdin:
        line = line.strip().lstrip("\ufeff")
        if not line:
            continue
        try:
            request = json.loads(line)
            image_path = Path(request.get("image_path", ""))
            if not image_path.exists():
                response = {"raw_text": "", "items": [], "error": "image not found"}
            else:
                response = recognize(ocr, image_path)
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

        print(json.dumps(recognize(RapidOCR(), image_path), ensure_ascii=False))
        return 0
    except Exception as exc:
        print(json.dumps({"raw_text": "", "items": [], "error": str(exc)}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
