# V2.6 第一轮本地候选

正式基线为 COS 当前、COS 固定及 GitHub main 的签名 V2.5.8，SHA-256 为 `b326511e8e19aa6db7fbf85dcdd6fa2f94b6537ef765628b7a410248bd7b83b9`。这是正式版结算后更新过展示页的清单，运行文件与原 Release 相同。候选共有 1,462 个文件，替换 15 个，沿用五条 SnatchinBracken 清理规则。

## 复活修复

包内实际为 Erk 的 ReviveCompany Patched 1.6.0，运行时自报 1.4.0。旧救援使用仅含玩家槽位的聊天消息。缺少确认时，原模块约两秒后重发。隔离复现里，第一具尸体是 408，客机在复活后 0.102 秒正常再次死亡，第二具尸体是 409。旧消息随后在客机上对尸体 409 执行了复活，房主仍保留其死亡状态。复现使用原版死亡入口和原模块结算路径，测试中丢弃一份确认以稳定触发重发。[原模块](https://thunderstore.io/c/lethal-company/p/Erk/ReviveCompany_Patched/) · [同类问题研究记录](https://thunderstore.io/c/lethal-company/p/TheWilczur/EnemyCompatibilityFixes/changelog/)

CrewCompatibility 1.0.4 复用 ReviveCompany 的瞄准、长按、资格判断和原有恢复代码。房主当场核对被救玩家、施救者、时间与当前尸体 NetworkObject，并把确认结果按 V81 原版死亡 RPC 的可靠有序通道发给其他端。旧聊天消息无法重新执行复活。清理捕获的旧尸体引用，本地 HUD 在有效复活时恢复。有效尸体的救援提示由现有目标判断驱动，避免其他交互提示隐藏时长按被意外阻断。

| 设置 | V2.5.8 | V2.6 |
| --- | --- | --- |
| `LimitAmountOfRevives` | `true` | `false` |
| `RevivesPerLevelMultiplier` | `1` | `1` |
| `SetAmountOfRevives` | `0` | `0` |
| `InfiniteReviveTime` | `false` | `false` |
| `TimeUntilCantBeRevived` | `120` | `120` |
| `ReviveButton` / `ReviveTime` | `R` / `5` 秒 | `R` / `5` 秒 |
| `HealthYouReviveWith` / `ExtraHealthLostPerRevive` | `25` / `5` | `25` / `5` |
| `CanPickUpBodies` / `CanReviveTeleportedBodies` | `true` / `true` | `true` / `true` |

血量继续沿用原模块的扣减次序。按正式默认配置，实际连续复活血量从 20、15、10、5 到 1 HP，随后的复活也至少为 1 HP。

## 组件与升级

- Hatchet 0.2.79 使用通用重击候选，实际构建提交为 `56302b0103ea64d4925678871016661dd1958a39`，27 个运行文件均与完整交付 ZIP 的校验记录一致。
- Universal Wrench 0.2.16-dev 构建提交为 `b3ad7945b5331e9014bfd61d2ff71c5031ea8f78`。只调整便携地雷第一人称持握偏移，沿用已交付诊断事件与全部既有资源。
- CrewCompatibility 1.0.4 构建提交为 `a68f37142b71a5f4abcc92d1233877e74e2c5cc7`。隔离房主和客机分别核对了 42 个自研组件与复活相关文件。
- 候选接收端使用定向配置迁移，从事务备份读取既有 ReviveCompany CFG，关闭次数限制并保留其他个人设置。隔离 V2.5.8 实装替换 15 个文件，清理五条旧路径，原始存档、智能柜配置和自定义复活时间逐字节核对。再次查询无文件改动。完整包 1,462 个路径和全部增量对象均逐文件核验。

## 实机证据

| 被救端 | 实测再次死亡延迟 |
| --- | --- |
| 房主 | 0.027、0.104、0.501、1.005、3.006 秒 |
| 客机 | 0.025、0.101、0.504、1.002、3.004 秒 |

每档使用原版死亡流程与实际长按 R。双端在再次死亡后匹配新的尸体、存活人数以及第三人称模型，下一次救援恢复本地摄像机、HUD 与交互。重复送入旧版复活消息后，新的死亡状态保持。录像为同步采集的 V81 原生画面。

定时检查保留同一具尸体至约 122.74 秒，再长按 R。游戏显示剩余时间为零并拒绝复活，双端保留相同尸体和死亡状态。

便携地雷在两个方向实测同一物品的交接、平视及上下 30°、正常步行和奔跑、切到空槽、取回、丢弃和对端第三人称持握。拆卸、原位和朝向、25 Credits 价值及重新激活实测通过。

交付与回退资料保存在 `D:/codex/LethalGoofballsPack/V2.6-candidate`。异地联机、第三名观察者和实际语音通话尚未覆盖。

## Wesley 汉化与阅读返修

V2.6 的更新候选包含 WesleyImageReader 0.1.3，带入导弹保险箱两态、秘密档案的原版检视与汉化，以及按字形宽度调整的中文排版。[Wesley 专项记录](wesley-reader.md)
