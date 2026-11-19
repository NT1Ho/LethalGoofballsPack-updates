# LethalGoofballsPack V2.5 本地热更新候选

2026-11-18，保留 V2.5 版本号。以既有 22 项更新和线上展示页修订为基础。正式 COS 和 GitHub 清单的 payload SHA-256 均为 `09be7cc0e157092945a6ef8dc403e014534e3b56e95d5b293d55333f1b7facfc`。退役清理继续使用接收端 1.17.3。

## 候选内容

| MOD | 版本 | 套数 |
| --- | --- | ---: |
| BigBadPigeon-Pigeons_HighVis_Suits | 1.2.2 | 11 |
| ProjectSCP-SCP_Suits | 1.1.2 | 8 |
| Dwarggo-Fashion_Company | 1.3.1 | 8 |
| BLUR-Military_Suits | 1.5.0 | 7 |
| TeamClark-ODST_Suit | 1.0.0 | 1 |
| TeamClark-CombineEliteSuit | 1.0.2 | 1 |
| ViViKo-BreakingBadHazmatSuit | 1.0.3 | 1 |
| Kats-Some_Suits | 1.0.6 | 3 |
| BatTeam-LethalFashion | 1.0.8 | 原版六套解锁 |

共 40 套新服装。More Suits 1.5.4 原生递归读取每个包的 `moresuits` 目录，保留作者材质字节并避开同名高级贴图覆盖。现有 MoreMaterials 1.3.2、TooManySuits 2.0.4 和 MoreCompany 1.14.0 继续共用。两个上游 `!less-suits.txt` 与包清单一起保存为来源资料，以保留原有服装。Fashion Company 的 Hightech 与 Used 保留作者商店轮换设置，价格分别为 75 和 15 Credits。LethalFashion 解锁 Green Suit、Hazard Suit、Pajama Suit、Purple Suit、Bee Suit、Bunny Suit。原有 Glow 保留轮换价 60。

SelfSortingStorage 1.5.3 默认 `[Upgrades] Wide Cupboard = true`、`Quantity Display = true`，柜体沿用深蓝色 `#000E57`，售价 20 Credits。已配置玩家的文件在接收端升级时保留。作者的宽柜设置应在购买前启用。对已解锁柜子的旧档，按作者说明使用重置功能或从新档切换。

Hatchet 0.2.62 采用构建提交 `cb410d3835739b26e7dcdc6f78b068585efd9499` 的原交付 ZIP，27 个玩家文件逐一校验，0.2.61 回退 ZIP 单独存放。Universal Wrench 仍为 0.2.10-dev。相关独立 MOD 的源代码继续保存在各自仓库。 [Hatchet 专项记录](https://github.com/NT1Ho/Hatchet/blob/b7d7b4b8c0b71fc0cf66bdb25fc6e1c1f4b3db51/docs/evidence/hatchet-v1-tme-grip/README.md)

新增或更新组件的版本、下载地址、依赖和 SHA-256 见 [component-sources.json](component-sources.json)。本地交付目录还有全部 129 个组件的登记。

## 构建与升级

候选清单 SHA-256 为 `2c2e072afd2e2f04dbb4c374aa7f2fc48c1d005f338983c14304e7b2cae16fa3`。安装 ZIP 的 1,460 个文件逐个校验。相较上一份 V2.5 候选新增 107 个、替换 10 个、删除 0 个。相较 V2.2 新增 142 个、替换 18 个、删除 5 个。

| 交付件 | SHA-256 |
| --- | --- |
| 完整 MOD 安装候选 ZIP | `b2f2c29c7a74f0c89b7230ea26a3b3febc0066ff8baee2ba75c9e1f400db8338` |
| V2.2 → V2.5 完整增量及发布中心候选 ZIP | `a518ed3446b9947dd0e0ae70fe41320effdb391b28c6dd0c010f092a3efb5069` |
| V2.2 → V2.5 标准累计登记 ZIP | `1d25a50132016e9726dc1c5fc8a6a76f905a14dccc7ee59d943ec89e9f74b9c7` |
| Hatchet 0.2.61 完整回退 ZIP | `01afac4884ea90ea2fe4ce72c72cf56fe55215b90c94773772ebe60508a7c6fe` |

接收端 1.17.3 从隔离签名源实装。V2.2 一次写入 160 个文件并清理五条 SnatchinBracken 路径。线上旧 V2.5 迁往候选同版本写入 116 个文件并保留现有智能柜配置。两边原存档和侧车数据逐字节保留，复查均零变更。

本地发布中心 2.2.3 使用隔离草稿和 V2.2 累计基线。线上同版本发布入口仅支持展示页改动，这条限制需要在未来发布内容变更前处理。

## V81 隔离实机

| 路径 | 结果 |
| --- | --- |
| 服装 | 同机 V81 两进程通过实际 LAN 会话。房主逐套换穿 38 套免费服装，客机核对服装 ID 和贴图。客机从八个包各换穿一套由房主核对。两套轮换付费服用隔离夹具加入当日商店轮换，实际终端买入 75 和 15 Credits 并穿戴。|
| 衣架 | 两边都能识别六套解锁的原版服装和 20 套原有免费整套服装。购买后共 67 套，六页可见数为 13、13、13、13、13、2。ODST 与两件原创工具组合抽查。|
| 智能柜 | 游戏终端购买 1000 → 980 Credits。插件的原生宽型预制体和深蓝配置生效。两件同类废料收纳到同一格，退出 V81 后重新进档，房主与客机均显示实体牌 2。实际取回一件后双端显示 1，原版红柜仍登记。|
| Hatchet | 两端加载 0.2.62。房主与客机各测试 Gangnam Style 持斧及对端观察，持握的身体右掌间距峰值 0.000002 游戏单位。房主造成轻击 20、重击 60 点伤害。舞蹈结束后用实际槽位切换扳手和手斧，客机看到双方对应的动画与持物。|
| 日志 | DawnLib 可选依赖提示、原有网络预制体警告和 Hatchet 扫描节点提示已在旧 V2.5 日志中出现。新增组件在两端正常加载。测试用插件只在隔离环境；首次测量夹具的反射字段和槽位匹配问题复核通过。|

服装颜色和模型的视觉验证为抽查，所有 40 套都有游戏内注册、穿戴和对端材质证据。联机范围为同一台电脑上的双进程 LAN。逐文件清单见 [上一份 V2.5](files-from-previous-v2.5.csv) 与 [正式 V2.2](files-from-v2.2.csv)。
