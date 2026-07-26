# Better HSR-Currency Wars V11 恢复工程

本工程由本机 V11 发布物的 DLL、PDB 和 WPF BAML 恢复，并使用 GitHub V4 源码校验技术栈与核心流程结构。

## 已部署环境

- .NET SDK 10.0.302
- ILSpy CLI 10.1.1.8388
- V11 原始 OCRRuntime 与识别素材

便携工具位于上级目录的 `.devtools`，不依赖系统全局安装。

## 构建

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

## 运行

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run.ps1
```

也可以直接双击 `启动 V11.cmd`。启动脚本使用工作区内的便携
`dotnet.exe` 托管 DLL，不依赖系统全局安装的 Desktop Runtime。

正式发布目录为：

`bin/Release/net10.0-windows/win-x64/publish`

该目录是与原 V11 相同形式的 win-x64 自包含发布，EXE 会优先加载同目录的
`hostfxr.dll`、`coreclr.dll` 和 WPF Runtime，不依赖系统全局 .NET。

反编译时生成的 `HsrCurrencyWarsCleanWpf.MainWindow.xaml` 已更名为
`MainWindow.xaml`，使 `App.StartupUri` 与程序集内的 `mainwindow.baml`
资源名称保持一致。

## 主要流程文件

- `HsrCurrencyWarsCleanWpf/Core/CurrencyWarsFlow.cs`：局外流程
- `HsrCurrencyWarsCleanWpf/Core/InGameOpeningFlow.cs`：局内流程
- `HsrCurrencyWarsCleanWpf/Core/ScanEvaluator.cs`：扫描结果判断
- `HsrCurrencyWarsCleanWpf/Core/OcrClickResolver.cs`：OCR 点击定位
- `HsrCurrencyWarsCleanWpf/MainWindow.cs`：主界面与自动化编排

现有 F 盘 V11 发布目录未被修改。
