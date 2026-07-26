# Better HSR-Currency Wars V11.2

用于《崩坏：星穹铁道》“货币战争”玩法的 OCR 自动化辅助工具。

## 普通用户

请前往 GitHub Releases 下载完整的 V11.2 自包含发行包。发行包包含 OCR 运行环境，不要求系统预装 .NET Desktop Runtime。

## 源码

- 主项目：`Better HSR-Currency Wars V11.csproj`
- 局外流程：`HsrCurrencyWarsCleanWpf/Core/CurrencyWarsFlow.cs`
- 局内流程：`HsrCurrencyWarsCleanWpf/Core/InGameOpeningFlow.cs`
- 主界面与自动化编排：`HsrCurrencyWarsCleanWpf/MainWindow.cs`
- OCR 桥接源码：`Tools/rapidocr_bridge.py`

本机配置文件 `config.clean.json`、编译输出和 OCR 可执行文件不会提交到源码仓库。

## 说明

V11 工程由本机现有发布物恢复，并参考早期 V4 源码校验核心流程。恢复及构建信息见 `RECOVERY.md`。

本项目仅供学习和交流使用。
