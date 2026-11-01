# V2.0.0 正常宽度双层船 · 本地候选

来源为 Thunderstore 的 MelanieMelicious_2_sToRy_ShIp__works_w_Wider_Ship_Mod 2.2.14。官方 ZIP 的 manifest 只要求 BepInExPack 5.4.2100。保留既有 BepInEx；以官方 DLL、bundle、新生成配置替换原来的双层船安装。未启用三楼、额外楼层或扩展前平台。ShipWindows 2.11.1 保留 VanillaMode=false、Move Posters=false、原遮板及其他设置，只在五个窗户分区设置 Always unlocked=true。售价与遮板自身的解锁配置维持原值。

Wesley Pack Compatibility 1.0.3 移除仅适用于加宽外侧窄平台的 ExteriorPlatform 补丁。保留 Asten Laboratory 寻路构建源修复与 Galetry 汉化。

## 同机 V81 检查

使用既有隔离房主与客户端，原 UWPhase1Migration 槽及原先不存在的 UWFixes013 槽。新建和既有会话均实际生成门、地板、左右侧窗。屋顶由作者把原 RoofWindow 对象停用，以 MelanieShip/RoofWindow0 的 GlassNoRefraction 双材质代替，主客机复核了新档实物。无预购窗记录。顶部采用双层船自带的玻璃表现；未验证其具有单独的活动遮板。
正常宽度外梯底部 (-0.57,0.29,-8.47)、顶部 (-0.57,10.80,-9.59)，客户端在落地后以 Interact 和 W 实际攀升 2.12 单位。先前加宽船使用的中间平台和第二段独立外梯已不存在。角色站在正常宽度屋顶时的连续主客机读数见 v81-measurements.json。
种子 73637897、Asteroid-13、LabFlow，两端从种子日志邻近采样至 landed=true 约 26 秒。按约两秒窗口计帧，最低均值主机 26.9、客户端 41.8 FPS。该测试没有复现原报告 60 秒持续 4–9 FPS，不代表异地所有机器改善相同。

## 正式发布时的删除操作

逐行采用 deletes.txt，包含 11 个 V2.0.0 受管文件。管理端 Workspace.Remove 从草稿的 files 中移除路径并写入 deletes；接收端 Engine 只处理本机状态登记且仍匹配原始 SHA-256 的文件，未登记会跳过，已改动会报错并保留。对通过旧更新器安装的朋友，需按该机制下发；手放的同名或其他目录副本不能保证被清理。兼容 DLL、双层船配置和 ShipWindows 配置应作为替换文件，不应放进 deletes。线上管理端草稿、签名入口与 Release 不在本次操作范围。此前宽船布局下摆放的家具在所有个人存档中的位置未逐一验证。

## Asteroid-13 拟态体

原版 MaskedPlayerEnemy，室内 Asten Laboratory 以当前星球敌人表的真实预制体在室内 AI 节点生成。Mirage 复制玩家外观并在当前设定中提供 metarig 控制器；TooManyEmotes 驱动拟态体舞蹈。Decrepit Store 的 MannequinA/B/C 不属于此次测试。在场景内通过真实 HitEnemy 及其原生死亡网络路径进行致命命中（夹具提供伤害，未模拟手动挥铲）。
第一组主客共同观察网络对象 382，舞蹈结束后击杀，躯干从距根约 1.2 米降至 0.12 米。第二组在原生表演 springy 播放中确认 IsPerformingCustomEmote 为真再击杀，主客对象 353 的 Dead、死亡片段及低位躯干一致，见 masked-death.txt。此对象通过 MaskedPlayerDie 动画倒地。两次均未复现站尸，未对怪物或玩家尸体应用任何生产补丁。异地网络、朋友使用的装扮与原事故录像未复现。
