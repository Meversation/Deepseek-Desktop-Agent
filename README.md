# Deepseek Desktop Agent

用 C# 重写的 Windows 桌面 Agent。基于 DeepSeek 网页版，通过 CDP 自动化操控浏览器，让 DeepSeek 能够读写文件、执行 PowerShell、截图、控制鼠标键盘和访问网络。

本项目是 [EAgent](https://github.com/YaoGaoChaoLe/EAgent) 的 C# 重构版本，保留原有工具协议与提示词，移除易语言实现。

## 特性

- 零 API Key、零 Token 费用，直接使用 DeepSeek 网页版
- 单文件 exe，自包含发布，无需安装 .NET 运行时
- DeepSeek 窗口内嵌到主界面，随主程序关闭，也可还原为独立窗口
- PowerShell 后台执行，不弹出黑框，指令与输出实时显示在内置选项卡
- 内置 6 个工具：执行 PowerShell、截图、上传文件、模拟鼠标、模拟键盘、HTTP 读文件
- 支持新建会话或从历史会话列表中指定继续哪一个会话
- 白天 / 夜间主题切换，无控件重建、无尺寸抖动
- 支持 Ctrl+A / C / X / V / Z / Y 等常用编辑快捷键

## 环境要求

- Windows 10 / 11
- Microsoft Edge（系统自带）或 Google Chrome

## 构建

需要 .NET 10 SDK。

```powershell
dotnet build src\EAgent.CSharp\EAgent.CSharp.csproj -c Release
```

发布为单文件 exe：

```powershell
dotnet publish src\EAgent.CSharp\EAgent.CSharp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

产物位于 `src\EAgent.CSharp\bin\Release\net10.0-windows\win-x64\publish\EAgent.exe`。

## 使用

1. 右键 `EAgent.exe`，选择以管理员身份运行。
2. 点击“选择工作目录”，选择一个目录。
3. 程序会启动 Edge/Chrome 并打开 DeepSeek，请先在浏览器内登录。
4. 点击“开始 / 继续”，等待 DeepSeek 读取系统提示词。
5. 之后的对话请在 EAgent 的输入框内发送，网页窗口仅用于查看。

## 数据与配置

- 配置文件：`%APPDATA%\EAgent\config.json`
- 浏览器调试配置：`%TEMP%\deepseek_debug_profile`（沿用原版路径，可复用已有登录）
- 每次运行的临时目录：`%TEMP%\EAgent_<pid>_<随机数>`

## 自测

```powershell
EAgent.exe --selftest
```

结果写入 `%TEMP%\eagent_selftest.txt`，覆盖提示词加载、六个工具解析、编码、Skill 释放和 PowerShell 后台执行。

## 声明

- 本项目仅用于学习交流，不适用于生产环境。
- 通过 CDP 自动化操作网页，与使用 API 的方式不同，可能违反 DeepSeek 用户协议，账号风险由使用者自行承担。
- DeepSeek 生成的命令可能具有破坏性，建议在每次执行前人工审查。
- 高频使用可能触发风控或账号限制，作者不承担责任。

## 协议

本项目遵循 GPL-3.0 协议，继承自 [YaoGaoChaoLe/EAgent](https://github.com/YaoGaoChaoLe/EAgent)。
