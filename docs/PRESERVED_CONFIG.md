# 必须保留的配置和行为

以 `inventory/CONFIG_BASELINE.json` 与交接包 `pack-reference/BepInEx/config` 为完整基线。以下只是重点，不是重新生成配置的模板。

- MoreCompany 多人及饰品、TooManyEmotes 全动作、TalkingHeads 8 / 20。
- Mirage 当前拟声、假人生成/携带物品与饰品联动；不得恢复默认。
- BetterStamina 体力消耗倍率 0.65，站立/步行恢复倍率 1.35，负重倍率 0.65；其余现有值保留。
- HotbarPlus 六格、NeedyCats 权重 8、SnatchinBracken 15 秒规则。
- MoreMoneyStart 1000 Credits，普通传送尸体允许现有手动救援。
- FairAI 和 IndoorHazardGuard 的室内机关人数保护；室外老鸟导弹例外。
- Natural Selection 巨人生态开启；史莱姆尸体吞噬和寻尸关闭，相关兼容开关保持基线。
- KiwiBodies 当前 8 HP、80 磅、基础 300–400；现有售价倍率不变。
- FixCameraResolution 固定 1920×1080、原版雾/阴影/后期/头盔。
- H增强版 MoreCounterplay 现有所有反制和修复，老鸟停机 10 秒。

本轮不修改任何 MOD 配置。包内规则与玩家个人偏好不能凭文件名自动区分：更新器保留未变的 .cfg；发布方明确更改某 .cfg 时仍按整文件更新并先备份。若以后需要逐项合并，应另行设计并验证，当前不能宣称已经实现键级合并。
