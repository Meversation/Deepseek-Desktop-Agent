# 启动恢复协议（①-I / ①-I-A / ①-II）

> 配套代码：`RuntimeStateStore.cs`。会话数据仍由 `SessionStore` 管理，SchemaVersion 保持 1，不新增字段。

## 1. 进程生命周期

- 启动：`RuntimeStateStore.Save` 写 `runtime\<pid>.json`，`CleanShutdown=false`，`Phase=Idle`，同时刷新 `runtime\last.json`。
- 运行：每次阶段变化（Thinking / ToolRunning / WaitingUser / Stopping）立即覆盖写入；最后一次工具调用记录 `LastToolKind`、`LastToolPayloadHash`、`LastToolStartedAt`。
- 正常退出：置 `CleanShutdown=true`、`Phase=Idle`、清空 `PendingPrompts` 后保存，然后删除 `runtime\<pid>.json`，保留 `last.json` 供下次对照。
- 异常退出：文件残留且 `CleanShutdown=false`，由下次启动扫描发现。

保留 `last.json` 的理由：即使当前进程记录被清理，仍能回答“上一次运行是怎么结束的”，且体积只有几百字节。

## 2. 启动检测流程

1. `ScanUnclean()` 枚举 `runtime\*.json`，跳过 `last.json`。
2. 条件：`CleanShutdown=false` 且该 `Pid` 已不存在（`Process.GetProcessById` 失败）。
3. 结果按 `StartedAt` 倒序；多于一条时只对最新一条发起恢复询问，其余归入“历史残留”，仅在诊断页展示。
4. 读取损坏文件：跳过并记入 `LastErrors`，不阻塞启动。
5. 判定结果只有三种：
   - 无残留 → 正常启动，不弹窗；
   - 有残留但 `Phase=Idle` 且无排队消息 → 只提示“上次异常结束，无未完成工作”；
   - 有残留且 `Phase` 为 Thinking / ToolRunning / WaitingUser / Stopping → 弹恢复询问。

## 3. 恢复询问（三类文案）

标题：上次运行未正常结束

- 继续：接回上一轮会话，把最后一条 `Running` 轮次标记为 `Stopped/Immediate`，并重新执行排队消息。
- 只看记录：打开对应会话，不自动发送任何内容，不改轮次状态。
- 新建会话：忽略残留记录，删除该 `pid` 文件，按正常流程新建。

默认按钮为“只看记录”，避免崩溃后自动重放写操作。

## 4. 崩溃影响分析（①-II）

按 `LastToolKind` 给出结论：

| 工具 | 影响 | 建议 |
| --- | --- | --- |
| PowerShell | 进程随程序消失，变量、函数、当前目录全部丢失；命令可能执行到一半 | 复核目标文件、服务、注册表；必要时重跑整段 |
| 文件写入（PowerShell 内） | 可能留下半成品 | 校验大小或哈希后再决定覆盖 |
| 截图 | 无本机副作用 | 直接重试 |
| 上传文件 | 只读上传 | 直接重试 |
| HTTP 下载 | 可能未下完 | 检查大小/哈希后重下 |
| 鼠标 / 键盘 | 可能留下半完成的界面操作 | 先确认当前窗口状态 |
| 无工具 | 仅回复中断 | 重新发送或继续 |

文案由 `RuntimeStateStore.BuildMessage(state)` 统一生成，UI 只负责展示。

## 5. 恢复边界

可恢复：

- `PendingPrompts` 队列（按原顺序）；
- 当前轮 `SessionId` / `TurnId`；
- 最后一次工具调用的类型与参数哈希。

不可恢复：

- PowerShell 进程内的变量、函数、别名、当前目录；
- 已内嵌浏览器页面的滚动位置与临时输入；
- 已执行但未落盘的命令副作用。

## 6. 幂等与安全

- 同一 `TurnId` 只允许恢复一次：恢复前检查 `SessionRecord.Turns` 中该轮是否仍为 `Running`，否则只提示不重放。
- 恢复动作必须先落盘“已恢复”标记，再执行队列，避免二次崩溃导致重复执行。
- 任何恢复操作都不自动执行写文件、删文件、改注册表类命令，只重放**用户排队的消息**；工具是否执行仍走正常确认流程。

## 7. 与 SessionStore 的兼容

- 不修改 `SessionRecord` / `TurnRecord` 字段，不提升 `SchemaVersion`。
- 运行态只存 `%LOCALAPPDATA%\DeepseekDesktopAgent\runtime\`，与会话目录 `sessions\` 分离。
- 清除运行态不影响会话数据；删除会话时顺带删除对应 `runtime\<pid>.json` 仅作可选优化。
