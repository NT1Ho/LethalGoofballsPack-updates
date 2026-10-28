# 版本更新记录

只记录现有本地历史资料，不编造缺失版本。整合包版本与更新器内部版本分开。

## V1.9 本次维护补记

- 修复两个 HookGen DLL 的历史下载地址，并补齐 V1.6 原地址附件。DLL 内容不变。
- 修复发布备份同内容去重漏传；加入历史引用与同版本并发修改检查。
- 接收端首次接入保留已有 .cfg；不变配置不重复覆盖。
- 增加 AI 交接、MOD 清单、独立分支和离线回退导出工具。

## V1.9

- 新增 · MoreMoneyStart（初始资金）：新存档、被解雇后重开的初始资金设为 1000 Credits。
- 新增 · SuitSaver（服装记忆）：自动保存每位玩家上次穿的服装，重进游戏或房间时恢复。
- 配置 · Natural Selection（生态链）：巨人生态功能：关闭 → 开启
- 配置 · ReviveCompany_Patched（手动复活）：允许手动复活被普通传送器传回飞船的尸体。
- 新增 · H增强版 MoreCounterplay（怪物反制）：加入弹簧头、八音盒、炮塔和黑豹反制，以及老鸟导电引雷、电击枪停机 10 秒；修复八音盒放置物品报错、重复计重和客户端放置提示消失。
- 新增 · CSync（配置同步）：同步房主的 MoreCounterplay 配置。
- 修复 · IndoorHazardGuard（室内机关保护）：修复室内无人时，老鸟的室外导弹无法伤害、点燃森林巨人的问题。

## V1.8.9

- 新增 · EverythingCanDieAlternative（怪物受伤与击杀扩展）：老鸟血量设为 15 HP；孢子蜥蜴可受伤、被击杀。
- 新增 · LethalNetworkAPI（多人同步支持）：支持 MOD 在多人游戏中同步数据
- 新增 · SuitsPlus（趣味主题服装）：添加 Bmo、Finn、Gorilla、Jake、Peter Griffin、Prismo 六套服装。
- 新增 · MemeSuits（表情包服装）：添加全部六套表情包服装。
- 新增 · TooManyHats（趣味帽饰）：添加多种帽子和趣味饰品
- 新增 · RandomCosmetics（随机主题饰品）：添加帽子、面具和身体配饰
- 新增 · TooManySuits（服装架翻页）：开启飞船服装架翻页。
- 新增 · MoreMaterials（服装材质支持）：为服装提供材质和外观效果支持
- 新增 · FixCameraResolution（画面清晰度调整）：内部渲染分辨率固定为 1920×1080。
- 新增 · Cute_Cosmetics（可爱饰品）：添加可爱饰品，禁用真人照片饰品 Newjeans。
- 新增 · PigeonsCosmeticsAddon（鸽子饰品包）：添加帽子、肩饰、尾巴等配饰
- 新增 · Natural Selection（生态链）：开启蜘蛛捕猎、蜘蛛网减速、蜜蜂攻击、沙虫捕食和史莱姆生态；关闭史莱姆吞尸、寻找尸体及巨人生态。
- 新增 · NaturalSelectionLib（生态链支持库）：支持怪物生态互动
- 新增 · SpiderPositionFix（蜘蛛位置同步修复）：修复蜘蛛位置和动画同步
- 配置 · SellBodiesFixed（怪物尸体出售）：老鸟尸体基础售价：250–300 → 750–900。
- 新增 · KiwiBodies（啄木鸟尸体）：啄木鸟血量设为 8 HP；死亡后生成可搬运、保存、出售的尸体，重 80 磅，基础售价 300–400。
- 配置 · MoreCompany（多人联机与饰品）：开启玩家和假人的饰品显示。
- 新增 · IndoorHazardGuard（机关伤害限制）：建筑内有玩家时，机关和爆炸才能伤害怪物。
- 修复 · Mirage / MoreCompany（假人饰品联动）：假人继承玩家头部等饰品外观。

## V1.8.0

- 新增 · QuickSell（快捷出售）：添加 /sell all 和 /sell quota 手动出售指令。
- 新增 · FasterItemDropship（快递加速）：快递到达时间 10 秒，开门后 3 秒离开，最长停留 40 秒。
- 新增 · ReviveCompany_Patched（手动复活）：按 R 5 秒复活队友，恢复 25 HP，每次额外复活减少 5 HP；救援期限 120 秒。每轮全队共享复活次数等于玩家人数。
- 新增 · GeneralImprovements（综合体验优化）：显示隐藏星球目录，添加操作和界面修复。
- 新增 · ChatCommandAPI（聊天指令支持）：支持 MOD 使用聊天指令
- 新增 · TooManyItems（物品保存与同步修复）：修复物品保存、同步和飞船内掉落问题
- 新增 · LethalModUtils（模组工具支持）：提供聊天指令等 MOD 所需的工具
- 新增 · NiceChat（聊天功能支持）：添加聊天支持，关闭生死互通聊天、额外时间戳和聊天框放大。
- 配置 · BetterStamina（体力优化）：跑步体力消耗、负重惩罚倍率：0.75 → 0.65；站立、走路恢复倍率：1.25 → 1.35。
- 配置 · Mirage（怪物拟声与假人伪装）：假人死亡掉落手持商店物品的概率：0% → 100%。
- 配置 · TalkingHeads（说话头变大）：说话头部放大倍率：4 → 8

## V1.7.8

- 新增 · BetterStamina（体力优化）：跑步体力消耗、负重惩罚倍率设为 0.75；站立、走路恢复倍率设为 1.25。
- 新增 · TalkingHeads（说话头变大）：说话时头部随音量放大；放大倍率 4，缩放速度 20。
- 新增 · HotbarPlus（扩展物品栏）：物品栏从 4 格增加到 6 格，直接开放。
- 新增 · LethalPresents（危险礼物盒）：礼物盒加入怪物、地雷和炮塔事件，触发参数 SpawnChance=6；关闭尖刺陷阱、飞船内事件和室内外怪物跨区生成。
- 新增 · NewYearJester（恭喜发财八音盒）：八音盒播放《恭喜发财》，带春节外观
- 新增 · LethalRegeneration（自然回血）：每 2 次游戏时钟更新，船内恢复 5 HP、船外恢复 2 HP；直接开放，累计回血不限量。
- 新增 · CoilHeadStare（弹簧头凝视）：弹簧头缓慢转头凝视，受击时弹簧晃动。
- 新增 · LCMaxSoundsFix（音效数量优化）：真实声道上限设为 128，虚拟声道上限设为 1024。
- 新增 · BonkHitSFX（搞笑敲击音效）：铲子等武器命中时播放 BONK 音效
- 配置 · NeedyCats（黏人猫咪）：猫咪生成权重：5 → 8
- 配置 · Mirage（怪物拟声与假人伪装）：非假人拟声间隔：25–45 秒 → 15–30 秒；假人生成概率：0.5% → 2%；出生携物概率：0% → 80%。

## V1.6

- 新增 · SellBodiesFixed（怪物尸体出售）：怪物死亡后生成可搬运、带回飞船和出售的尸体。
- 新增 · LethalLib（内容扩展支持库）：支持 MOD 添加物品和怪物
- 新增 · HookGenPatcher（插件挂钩支持）：添加插件挂钩支持组件。
- 新增 · MonoDetour（底层补丁支持）：提供 MOD 运行所需的底层支持
- 新增 · MonoDetour_BepInEx_5（底层补丁适配）：连接 BepInEx 与 MonoDetour
- 配置 · MoreCompany（多人联机与饰品）：大厅人数设置：5 → 50

## V1.5

- 新增 · SnatchinBrackenFORK（黑影拖拽修复版）：替换原黑影拖拽插件，修复接触玩家后只掉物品、无法正常抓人的问题。
- 移除 · SnatchinBracken（黑影拖拽）：移除原插件，由 SnatchinBrackenFORK 接替。
- 配置 · NeedyCats（黏人猫咪）：猫咪生成权重：2 → 5
