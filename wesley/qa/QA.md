# 隔离验收工具

只部署编译后的 Wesley.IsolatedQA.dll 到隔离游戏副本。不要把源码目录、探针、反编译参考和测试参考素材放入玩家包。此探针要求隔离标记，并在每项操作开始前确认实际 ES3 存档绝对路径位于本游戏副本的 uw-test 目录下。它复用现有 GameHarness 的本地 LAN、虚拟键盘与存档重定向。

在 qa 下运行 dotnet build Wesley.IsolatedQA.csproj -c Release。项目引用本地 V81 游戏与相邻 candidate 目录的上游 DLL。修改了插件的进程必须冷启。证据默认保存在相应游戏副本 wesley-qa 下，有请求 GUID 和 UTC 时间戳。

示例（PowerShell）：

    ./Invoke-WesleyQA.ps1 -PeerRoot $peer -Op state
    ./Invoke-WesleyQA.ps1 -PeerRoot $peer -Op catalog
    ./Invoke-WesleyQA.ps1 -PeerRoot $hostRoot -Op wrench -Arg native
    ./Invoke-WesleyQA.ps1 -PeerRoot $hostRoot -Op wrench -Arg BloodyLungApparatusDisable

扳手对照要求房主已降落。探针创建测试用扳手与对应设备，通过现有原版拾取/输入驱动对地面设备瞄准并长按 E。记录目标物理命中、中央提示、次数、原物件存续和转化产物数量。任何前置失败都记失败。房主与客户端部署、真实玩家实际录入输入分别记录。

story 可列出实际已出现的磁带/阅读物与播放器、原始路由状态、物品栏。 tapegrab 接受现有已生成物品的 NetworkObjectId；tapesubmit 使用现有播放器的 InteractTrigger 并检查路由原先锁定、交付后可路由。使用实际终端命令验证 route；返轨后 save、重新进入、读回同一场景锁和插槽。中途用测试入口变更航线不是自然解锁证据。

Test-WesleyScenes.ps1 为可选的机制抽样/失败复跑工具。传入明确的 Scene 数组，支持 SceneName|DungeonFlowName 作为测试限定的室内。定种子和测试用 quota 只作用于隔离运行；该夹具调整配额期限的记录不用于被开除/自然进度判断。通过 LLL 原有主机广播选择的室内与自然分配分别统计。enter/exit 使用场景配对的 EntranceTeleport 并对照真实位置和角色内外标志；它不等于完整手工走路或从头通关。

## 定向静态证据

* QuickSell 1.1.10 对普通批量操作按 prefab 名称比对本地黑名单，-a 可以绕过。catalog 中的 itemName 和扫描文字不是保护键。最终合并配置由整合包候选目录维护，依据包含交付组件、真实物品 prefab 及录像带目标。生成输入为 128 个上游 .lethalbundle 的真实序列化内容。
* 组件 ItemConsumer 对部分设备按 itemName 比对并在各端 DestroyItemInSlot(currentItemSlot)，因此显示翻译必须与身份隔离。DestroyItemInSlotFix 的反编译验证为 HUD 插槽修正，不能据此推断快速切槽、客户端晚加入与储物重载均正常。
* JLL 的 BackDoor 三个设施电源插口通过 TriggerOnHost 中转，与 AppyInsert 的宿主绑定相符。Brooding seed 的 AlterActivator 在服务器写当前存档；Berunah 的 Crystal cube 经 IndianaCubes 打开门。真实传送、状态复原与源数据连线各自记录。
* SnatchinBracken 保留实装 fork 配置 Snatchin Floating Head = false，普通 Bracken 不对应浮头分支。Dora Pocket 保持原 Item 网络对象身份，手电升级在改动前广泛接受地上的 LungProp，Bloody/Cosmic/Royal 均存在该脚本；该风险由 UW 仓库单独修复。搬雷检查原版 NetworkPrefab hash，因此新机关的场景子节点不会仅因同名受影响。
* AssimilatedGrowth 使用 allPlayerScripts.Length 分配感染状态及变异对象数组。SpecialGhostGirl 的 ChoosePlayerToHaunt 则在两个玩家循环和一个玩家权重数组中固定为4。包兼容补丁只针对这一方法，按实际玩家槽（统计循环受可用统计数组上限约束）；原有选人权重保留。桌游的四张牌不是四人上限。Steam 实装 MoreCompany 配置显示目前房间人数10，与可选最大50分别记录。
* 动态加入、退出、重连及被开除不等价；双进程数据不代表满员或异地实测。最终检查状态以同版本结果文件为准。

本目录下 private/ 和 bin/obj 均不进入源码仓库。源码只提交脚本、自编 C#、衍生清单、兼容说明。仅单独保留需要的测试结果，避免混入原版或第三方反编译代码及资产。
