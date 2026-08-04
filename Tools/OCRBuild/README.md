# OCR 运行时构建

OCR 桥接程序使用 PyInstaller `onedir` 方式发布，避免 `onefile` 在系统临时目录生成并遗留 `_MEI...` 解包目录。

构建环境建议使用 Python 3.12 x64。在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\OCRBuild\Build-OcrRuntime.ps1
```

构建完成后，目录版运行时会写入：

```text
OCRRuntime\rapidocr_bridge\rapidocr_bridge.exe
```

应用通过标准输入把 PNG 字节直接传给常驻 OCR 进程，不会为每次识别创建临时截图文件。
