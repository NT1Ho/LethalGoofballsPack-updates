# V2.0.0 本地维护 · V81 降落、平台和交互

原 Steam 会话种子 73637897，Asteroid-13、无天气、Asten Laboratory。主机生成完成后开始退出房间，所有玩家完成生成的回调出现在断线期间。原记录没有逐帧采样或队友日志，无法将当时持续 4 FPS 唯一归因于地牢、平台或某个客户端。

1. 加宽双层船的 MelMel.OutsideShip0 与 ShipDoorClosed 的盒体重叠。上梯底部世界坐标 (-0.57,5.46,-8.10) 两个区域每个物理步竞争同一角色，即使观察端人在舱内也会处理。仅在玩家与该外侧盒体实际重叠且已处于其混响时，跳过同组船内开门或关门触发器的重复处理。离开平台恢复原生处理，碰撞、楼梯和怪物处理沿用原版。修复版同机双进程交换站位并在指定种子降落。
2. Wesley Interiors 的 dreadlab.lethalbundle 中对应四种名称均有不可读网格，另有同名可读 Cube。V81 RoundManager.BakeDunGenNavMesh 在生成结束时收集该室内整个子树。按当前 LabFlow 和 CurrentDungeon 的 Children 模式，从实际构建源剔除已知会被 Unity 跳过的不可读网格。保留其他几何与其他地牢。主客机各烘焙一次，基线 98 条错误，修复版 0 条。未专项测量怪物 AI 行走路线。
3. Galetry 的 JLL ItemShop 收到货位同步后动态拼接英文和价格。读取 LC Chinese 的现有名称词典，只写货位提示，保留原网络物品、价格和购买逻辑。实机主机瞄准显示“购买大捆炸药（12 积分）: [E]”；两端货位均出现中文，客户端本次单独瞄准未捕获到文字。另补六条脚本生成的精确提示，未逐条游玩机关。
4. 修复 DLL 为 Wesley Pack Compatibility 1.0.2；同时部署候选目录的 Wesley-interactions.cfg。本地 Steam DLL、配置与隔离实测文件逐项 SHA-256 相同。旧 DLL 为 CF35573B9A32F17CC1C7CCB7A020404259B9E5999CB9CE585DDB32878B0EF74F，新 DLL 为 E4998BAF660864044090CEF6FBCECD46BA34DA96E83A82A81406A292EF334D19。此提交仅维护分支，无线上发布。

原始读数及采样范围见同目录 platform-landing-v200.json。二层平台按坐标定位玩家后运行原生物理与联机回调，FPS 以实时时钟统计。正式三人 Steam 会话与隔离双进程的表现应分别看待。
