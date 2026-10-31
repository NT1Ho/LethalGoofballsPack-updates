# 针对性 V81 验证 — 2026-10-30（本地时间）

运行环境：V81 两个真实 Unity 游戏进程，隔离 LAN 房主/客户端；两侧相同候选插件与配置。探针调用游戏实际方法并读取运行时状态，不是单元测试模拟，也不等同于异地真人联机验收。ES3 与 DawnLib 存档路径重定向到测试副本 `uw-test`，存档名 `PackSixMaintenance`，未使用用户真实存档。

| 项目 | 结果与证据 |
|---|---|
| 老鸟命中 | 电击枪原版 ShockWithGun 路径触发 H 增强停机后，分别使用房主/客户端的原版 Shovel.HitShovel。左/右上下腿及机身五个区域均每挥击仅扣 1 HP，房主 15→10、客户端 10→5；原有 ECDA 网络伤害流程。新增碰撞体查询结束后全部关闭；原爪部盒尺寸未变，9 米负面测试不扣血。 |
| 霰弹枪提示 | 房主/客户端分别检查真实 safetyOn × shellsLoaded 的六种组合，原生四行提示没有增加 HUD；丢弃后弹药行清除。只改变 UI，未重新测试无关射击伤害机制。 |
| 加宽船地板 | 左/中/右三个新增/原有区域各放扳手，实际落地高度约地板上 2–3 cm，水平位置保持；重启游戏后对应三处局部坐标均保留。 |
| 家具与窗户 | 原版家具摆放及服务器 RPC 成功记录；进程重启后 hasBeenMoved 保留。左右窗户使用 ShipWindows 原生创建/保存路径，重启仍存在。实机画面观察单层船舱、监控及终端；未穷举所有月球/家具组合。 |
| 便携地雷 | 加宽侧舱中通过现有严格校验的 LandmineDisassembly.Apply 拆除，贴合新地板；实际拾取、丢弃、部署、重新解除通过；船体父对象保留；重启后仍在地板上。没有更改 Universal Wrench。 |
| 玩家尸体 | 客户端真实死亡生成 RagdollGrabbableObject，房主通过原生 BeginGrabObject 射线拿起：carryWeight 1→2.125，增加118.125磅；原配置计算236.25磅，减半。客户端配置同为2.125。 |
| Touchscreen | 双端均从原版 shipInnerRoomBounds 内正常站位，通过模组真实屏幕射线与 OnPlayerInteraction 入口触发。Ping/Flash 在两端均执行原版方法；观察端音源playing=true、Flash冷却2.25，约20–30ms内收到。绑定核对 E/LMB。 |
| 子弹 | 真实终端输入 shells/confirm，从余额扣80，进入原版订单队列；快递船开门生成网络子弹物品，客户端可见；两端商店价格80。 |

最终新增补丁 DLL SHA-256：`42A5D98126C681DD44DD5A856549E237F19FF93E96EE6DFBD8F6044359DE94F5`。

完整运行日志：`evidence/final/`；之前测试场景日志保留在 `evidence/session-before-reload/`、`evidence/second-session/`。测试准备时修正过：老鸟需要原版巢穴前提、反射可选参数、家具名字匹配、拿取重量需经过 BeginGrabObject、屏幕操作应站船内且等待摄像机稳定。上述是测试夹具修正，未据此盲改第三方 MOD。

现存日志限制：Coroner 对原版 Radar-booster Flash 的 StunExplosion 前缀记录捕获异常，但原版闪光/网络播放继续成功；隔离 LAN 退出时 TooManyEmotes 个人保存回调可报空引用；DawnLib 的可选兼容程序集反射会打印缺失警告。未为消除这些已有/旁支日志升级或改动稳定组件，不能将测试称为“零警告”。

未做：异地联网、长时间游玩、所有月球和家具穷举、无关扳手玩法回归、正式更新器发布。所有新增插件和配置需在实际联机参与方保持一致。
