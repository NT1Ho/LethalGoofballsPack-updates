# LethalGoofballsPack V2.5.8 · V81 本地候选

本候选从线上签名 V2.5 累计版构建。COS 的当前和固定版本、GitHub 主分支签名清单及 V2.5 Release 的清单内容一致。基线清单 SHA-256 为 `4ded9937522da0f0e886d68e919e954e4e7647d578b4662c1c997120579647f3`，共 1460 个路径。新候选有 1462 个路径，新增 2、替换 11，本次移除 0。沿用五条 SnatchinBracken 历史退役规则。逐文件旧值、新值和字节数见 [files.json](files.json)。

## 运行来源

| 组件 | 来源 |
| --- | --- |
| Universal Wrench `0.2.12-dev` | [构建源码](https://github.com/NT1Ho/UniversalWrench/commit/5eb28f15b7760d8d3edcab6b751226d181d2e6b8)和独立包。包含已有 `0.2.11-dev` 材质修复，沿用 V2.5 老鸟 B5 音频。新增显形资源是本项目 Shader 在 Unity `2022.3.62f2`、HDRP `14.0.12` 编译的独立 AssetBundle。 |
| SlimeBlast `1.0.0` | [构建源码](https://github.com/NT1Ho/LethalGoofballsPack-updates/commit/f7e3d9cdba0c00446d383d77c75a5bce7d19c982)。只接管已进入原版爆炸伤害入口的史莱姆，以原版持有端死亡流程清理。 |
| Natural Selection `0.4.19` | [Fandovec03 原始包](https://thunderstore.io/c/lethal-company/p/Fandovec03/Natural_selection/)。整包保留已发行的 DLL，`BepInEx/config/fandovec03.NaturalSelection.cfg` 的 `[Entity settings]` 中将 `Enable slime` 改为 `false`。 |
| 线上 V2.5 其余内容 | Hatchet `0.2.63`、服装、SelfSortingStorage `1.5.3`、汉化、接收端及别的 MOD 运行文件保持原哈希。完整来源、配置改动、逐文件哈希在候选的 `BepInEx/pack-info`。 |

## 史莱姆调查与实现

运行中的 V81 史莱姆初始化 `enemyHP=3`、`canDie=false`、`canBeDestroyed=true`，原版开门速度倍率为 `0.4`。正式版现装 ECDA 在 `nwnt.EverythingCanDieAlternative_Enemy_Control.cfg` 设置 `BLOB.Enabled = false`。常规配置里的 `BLOB.Health=3` 和 `BLOB.Unimmortal=true` 在关闭接管后不参与运行。实际血量来自原版预制体。V81 原版普通伤害规则照旧。

实际安装的 Natural Selection 在启动时用 `Enable slime` 决定是否装载 `BlobAIPatch`，共用碰撞入口也以此开关约束史莱姆与怪物及尸体交互。故只改配置。爆炸修复沿用 `Landmine.SpawnExplosion`，待整合包的室内限制和有效命中入口确认后执行。对史莱姆再做可达性和遮挡验证，沿用原版摧毁分支去掉对应网络对象。FairAI、ECDA、Natural Selection 与 IndoorHazardGuard 保留原运行文件。

## 验收证据

- 完整候选 ZIP 逐一核验 1462 个安装文件的字节数和 SHA-256。增量 ZIP 核验候选清单和 13 个内容对象。发布中心草稿使用同一版本和候选清单。
- 接收端 `1.17.3.0` 用独立 RSA 测试签名及本地对象源，从 1460 个与线上清单匹配的 V2.5 文件实际升级，一次写入 13 个文件。五个特意放回的 SnatchinBracken 退役路径都被清理，旧存档和玩家自定义储物柜配置哈希不变。再次检查零写入、零清理。
- V81 两端均收到同一只实际生成的 3 HP 史莱姆，普通伤害、距离外爆炸和被墙完全遮挡的爆炸均存活，一次无遮挡爆炸由原版流程清理，两端对应的网络对象均消失。
- 实机读回 Natural Selection 共用碰撞的史莱姆开关为关闭，蜘蛛的生态 AI 仍有 Natural Selection 补丁。配置逐行对照只有 `Enable slime` 变化。接收端更新该配置时使用原有备份流程。
- V81 双侧 UnityTransport 实测 99 ms 往返。客机连续收纳三件原始物品，再把刚取出的物品一次按 Q 收回。房主旧视角四次均未命中，修订校验四次均接受。两端物品 ID 与袋内重量结果一致。
- 房主侧确认距离、墙体遮挡与已持有状态的拒绝路径。遮挡和持有拒绝均调用提示，物品未消失或进入口袋。
- 两件物品都通过原版拾取测得 2 lb。电源之心原版终端扣除 800 Credits，原版运输船正常交付，客机可拾取、照明和放下。房主原先长按 E 升级设施电源也通过。
- 原版理发师实机对照包含关灯、全光、左右扫光、墙挡光和近距离原版显示。房主光源在客机照出对应区域，遮挡墙挡住光源时，从侧面有直接视线的客机仍看不到目标。测试截图和日志位于本地 `验证证据`。

## 下载与回退

本地目录 `D:/codex/LethalGoofballsPack/V2.5.8-candidate` 内含完整安装 ZIP、V2.5 增量与发布中心候选 ZIP、完整候选清单、中文更新说明及 `SHA256SUMS.txt`。`发布中心` 是新本地工作目录。`V2.5-候选回退资料.zip` 保存原 V2.5 清单、所有被替换路径的原始字节和本候选新建路径列表。线上更新渠道仍指向 V2.5。
