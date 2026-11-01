# 游戏故障诊断 0.1.0（接收端 1.13.0.0 本地候选）

此模块属于 LethalGoofballsPack 更新器，独立于 Universal Wrench。开发分支不发布整合包、Release、COS 或签名清单。整合包声明版本保持 V2.0.0；本地正常宽度双层船仍是未发布候选。

## 使用

接收端的“故障诊断”页面可以安装/修复插件、启停监控和调整 F1–F12 标记键。退出游戏后生效。新接收端在正常更新前安装它内置的诊断插件；不删除其他 MOD。管理端仍通过原来的文件清单和发布事务分发整合包，发布诊断 DLL 时也应登记其 SHA。开发期间只用 `接收端.exe --diagnostics "游戏目录"` 进入离线诊断页面，避免触发当前线上旧飞船文件更新。

游戏启动即记录。按 F8 保存状态和当帧结束时的一张游戏 PNG，短提示出现后继续游玩。2秒防重复；每会话最多128个自动/手动标记，达到上限明确拒绝；截图累计超过256MB停止新增截图，仍保存现场。截图可能导致单次读回/编码停顿，这与持续监控开销分开测量。

退出后选择会话和标记，查看截图，写备注，点击“保存备注”。每次标记独立备注。主客机可以填相同“共同联机编号”。导出 ZIP 含中文 `报告.md`、identity、JSONL 前后窗口、截图、备注和文件 SHA 清单，可直接交给 Astra。无自动上传，导出前自行检查 MOD 日志及截图中的名字等隐私。

数据位于游戏根目录 `.lcpack-diagnostics`。异常退出后没有 closed.json，接收端标注为异常/待恢复；恢复只识别完整 JSONL 行，不改原证据。锁文件判断活跃写入，游戏运行时禁止编辑/导出/删除此会话。正常退出刷新，平时每2秒持久刷新；强杀可能损失最后约2秒，系统/磁盘故障不承诺零损失。

每会话循环日志约32MB，前置窗口最多60秒/1024条，后置30秒，单异常窗口最多4MB；16条普通日志/秒和8条错误/秒独立配额，溢出计数进入证据。磁盘写入失败停止记录并保留错误状态。接收端启动/刷新时清理14天前或总量超过1GB的普通正常会话；标记、备注/固定、异常及活跃会话不自动删除。超过1GB提示手动整理，可整次会话移至回收站。重要记录保护优先于硬性总量上限。

## 数据与边界

- 每帧仅累加帧时间、检测按键；每2秒输出帧均值、最大值、100ms以上帧、GC次数和记录器主线程计时；每秒采样现有游戏单例、最多32名玩家和手持物。不持续扫描全场对象。
- 手动标记才做一次12m瞄准射线及15m附近非分配碰撞查询（128碰撞体，最多48相关对象），记录网络编号、所有者、玩家/敌人/物品/交互状态。半径和上限会造成不可见数据，不当作世界完整快照。
- 场景加载/卸载、联机/人数/死亡/区域/物品状态变化、BepInEx和Unity错误进入事件流。程序错误、500ms以上帧、降落120秒未结束触发窗口，自动触发冷却60秒。它们是条件，不是MOD故障归因。
- 同局关联使用哈希Steam lobby、seed、planet、serverTime/networkTick及网络对象ID。LAN没有全球唯一房间身份，需共同编号和事件核对。UTC受系统时钟偏差影响，不声称异地时钟精确同步，不额外发RPC。
- 启动后后台一次性读取实际加载插件 GUID/名称/版本、程序集MVID和相关DLL、配置的SHA256。复用接收端 state.json 的期望哈希比较 match/mismatch/unmanaged/missing。配置只收集哈希，不收集值；资源包不重复哈希。没有identity不能用退出后当前文件假冒当时加载版本。
- 磁盘哈希和MVID不代表运行时Harmony改写后的IL；不追踪所有RPC，不持续截屏/录像，不判定复杂视觉或动画Bug的原因。启动早于诊断插件订阅的日志可能缺失。崩溃/挂起保留已写记录，但不会生成原生崩溃转储或自动判断死锁。
- F8现有配置没有发现实际绑定；枚举帮助文字中的F8不是冲突。无法静态排除所有第三方硬编码/用户新改键，按需在页面调整。

## 设计依据

采用标准 BepInEx ILogListener 与 Unity 日志订阅，保持已有日志输出机制；主线程只读取Unity对象并构造普通值，后台有界队列写盘，不修改玩法、物理和网络结果。

- [BepInEx logging/listeners](https://docs.bepinex.dev/v5.4.11/articles/dev_guide/plugin_tutorial/4_logging.html)：支持独立监听器，无需替换磁盘日志。
- [Unity threaded log callback](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-logMessageReceivedThreaded.html)：回调可能来自多线程，回调只限流和排队，不访问Unity对象。
- [Unity ScreenCapture](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ScreenCapture.CaptureScreenshot.html)：只手动请求游戏画面，在帧末执行。
- [AsyncLoggers](https://github.com/mattymatty97/LTC_AsyncLoggers)：日志后台化思路适用，但全局日志机制替换超出本次需求，不安装。
- [Log Muncher](https://lethalcompanymodding.github.io/Thunderstore/www/Tools/Log-Muncher.html)：事后聚合有价值，但没有完整现场、截图、联机版本身份。没有照搬其历史“安全忽略”规则。
- [UnityDebuggerAssistant](https://github.com/LethalCompanyModding/UnityDebuggerAssistant)：补丁/异常溯源可用于未来定向排查；第一版不全局遍历并格式化全部Harmony堆栈。

## 构建与验证

`updater/Build.ps1 -GameDir <合法V81目录> -Dotnet <.NET8 SDK路径> -SkipTests -OutputDirectory <候选目录>` 同时构建插件、接收端和管理端。插件仅依赖游戏已有Unity/NGO/Newtonsoft与BepInEx。不要分发游戏程序集。针对性测试见 tests；全量旧更新器测试不会替代运行时验收。

运行时驱动 `diagnostics/tests/RuntimeProbe.cs` 只能放到已有隔离副本，要求 `UNIVERSAL_WRENCH_ISOLATED_TEST` 标志；它通过测试文件收取45秒测量、注入F8、故意错误/停顿和退出命令，不进入交付。复用原隔离驱动重定向ES3及Dawn持久化，同时备份并核对真实存档。

本轮源代码基线：历史 `制作致命公司整合包` 工作目录的接收端1.12.32.0源码及现有构建、测试、许可；先前GitHub主分支只保存更新入口与维护文档，故现在将两端源码纳入同仓开发分支。近期两条聊天已读取，未声称通读所有历史回合；旧交接文档V1.9不能代表当前安装。最新正常船候选以 maintenance/v200-standard-ship 的 dfc8284 和实际本地文件为准。
