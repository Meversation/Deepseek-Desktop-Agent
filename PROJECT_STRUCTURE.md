# 项目结构

```
Deepseek-Desktop-Agent/
├── .github/
│   └── workflows/
│       └── static.yml
├── docs/
│   ├── DESIGN.md
│   ├── design-启动恢复协议.md
│   ├── design-双档停止状态机.md
│   └── design-ds页面脚本.md
├── publish/                  # 最终发布的单文件 exe（提交到仓库供下载）
│   ├── Deepseek-Desktop-Agent-framework-dependent.exe
│   ├── Deepseek-Desktop-Agent-self-contained.exe
│   ├── framework-dependent/
│   │   ├── Deepseek-Desktop-Agent.exe
│   │   └── Deepseek-Desktop-Agent.pdb
│   └── self-contained/
│       ├── Deepseek-Desktop-Agent.exe
│       └── Deepseek-Desktop-Agent.pdb
├── src/
│   └── DeepseekDesktopAgent/ # .NET 10 WinForms 主程序源码
│       ├── Program.cs        # 入口
│       ├── MainForm*.cs      # 主窗体（按功能拆分的 partial class）
│       ├── Core.cs           # 核心逻辑
│       ├── BrowserManager.cs # 内嵌浏览器管理
│       ├── CdpClient.cs      # CDP 客户端
│       ├── PowerShellRunner.cs
│       ├── ToolExecutor.cs
│       ├── SessionStore.cs / SessionTree.cs
│       ├── SkillInjector.cs
│       ├── ThinkingBlockParser.cs
│       ├── MarkdownRenderer.cs
│       ├── Theme.cs / UiKit.cs
│       ├── UpdateChecker.cs
│       ├── SelfTest.cs
│       ├── Diagnostics.cs
│       ├── Dialogs.cs
│       ├── Native.cs
│       ├── RuntimeStateStore.cs
│       ├── Assets/
│       │   ├── icon.ico
│       │   ├── skills.zip
│       │   └── system_prompt.txt
│       ├── bin/              # 编译输出（不提交）
│       ├── obj/              # 构建中间产物（不提交）
│       └── publish/          # 本地发布输出（不提交）
├── Works/
│   ├── liang-skin/           # 皮肤/换肤示例（HTML + webp 素材）
│   │   ├── index.html
│   │   └── assets/
│   └── remotion-intro/       # Remotion 视频工程（Node.js）
│       ├── package.json
│       ├── remotion.config.ts
│       ├── tsconfig.json
│       ├── src/              # 场景组件（.tsx / .ts）
│       ├── public/           # 音乐、封面、头像等素材
│       ├── out/              # 渲染输出
│       └── node_modules/     # 依赖（不提交）
├── weekly/                   # 周报页面（静态 HTML）
│   ├── index.html
│   └── W44/
│       ├── index.html
│       └── assets/
├── .gitignore
├── index.html                # 项目主页？
├── build.cmd / build.ps1     # 构建脚本
├── run.cmd / run.ps1         # 运行脚本
├── CHANGELOG.md
├── TODO.md
├── auto_update.txt
├── LICENSE
└── README.md
```
