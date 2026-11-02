# LethalGoofballsPack V2.1.0 · V81

船型采用正常宽度双层船 2.2.14。双层船本体的文件与既有基线一致，复用原文件。ShipWindows 2.11.1 默认解锁五处窗户；Wesley Pack Compatibility 使用正常宽度候选 1.0.3。清理 WiderShipMod 1.4.3、旧外梯补丁 0.4.5 及对应受管文件。宽船音区辅助插件 Ship Edge Reverb 1.0.0 从未进入正式基线，其独立登记保存在本机未采用目录。客户端的 MOD 展示名称和用途已同步正常宽度布局。

Wesley 简体中文精翻 1.1.0 与 Wesley 图片中文阅读 0.1.2 一起纳入累计包。阅读器 0.1.0 和 0.1.1 已被最终登记取代。环境招牌使用原图，290 项内容支持按 Z 阅读中文，手持卡牌手册保留中文显示与放大检视。

| 新增组件 | 发行版 | 原作者包 |
|---|---|---|
| fumiko-CullFactory | 2.0.11 | https://thunderstore.io/c/lethal-company/p/fumiko/CullFactory/ |
| Zaggy1024-PathfindingLagFix | 2.4.2 | https://thunderstore.io/c/lethal-company/p/Zaggy1024/PathfindingLagFix/ |
| DiFFoZ-BepInEx_Faster_Load_AssetBundles_Patcher | 1.2.0 | https://thunderstore.io/c/lethal-company/p/DiFFoZ/BepInEx_Faster_Load_AssetBundles_Patcher/ |

使用作者发行的 DLL 和本机编译数据文件，保留许可。PathfindingLib 使用正式包已有 2.4.1，满足最低 2.4.0。包内未发现加载补丁作者列出的 XUnity AutoTranslator 或 IntroTweaks。插件使用默认配置；玩家本机生成的缓存不随更新下发。

## 实测

- 正常宽度船型下两个 V81 进程通过 LAN 回环联机。Asten Laboratory / Asteroid-13 / LabFlow 使用种子 73637897。房主及客户端均实际进入并退出；入口所在房间保留 72/72 个在用渲染器和 8/8 盏在用灯，房主视角下另外 3152 个远处渲染器被剔除。走廊所在房间为 20/20 和 2/2；新增金属板、Wesley 实验日志近处渲染器均保留。另行降落 Galetry，在商店实际瞄准货位，出现中文购买提示，近处商品仍有可用渲染器。
- 在原生敌人表下生成拟态体，房主记录到移动和 PathComplete；客户端观察到同一网络对象编号和位置更新。已有寻路库、汉化和三项新增组件同时加载。首个过早的 LAN 连接被 LobbyControl 拒绝，断开阶段触发既有 LethalLevelLoader 错误；大厅准备完毕后重新连接，成功完成两处场景验证。未逐个测试其他敌人或每个房间。
- 冷启动日志成功重压缩 267 次，暖启动观测到 265 次读取解压缓存。隔离副本当时约占 2.72 GiB，实际大小依内容而异。没有以两次启动作整包性能提升百分比的推算。
- 发布材料来自 9 个组件的最终登记。相对 V2.0.0，11 个新增、10 个替换、11 个删除。候选为 1310 个目标文件。管理端累计登记新增针对本次移除的规则：只允许删除被移除组件自己的 pack-info 子目录文件；其他包和共享元数据的直接删除仍被拦截。隔离管理测试含跨组件和共享文件拒绝用例。
- 正式接收端独立升级为 1.17.1，内置诊断 0.1.1。旧正式接收端 1.17 实际通过签名更新源发现、下载并缓存 1.17.1，COS 和 GitHub 两个下载源 SHA-256 一致。

## 正式发布结果

管理端实际发布窗口在 V2.0.0 基线上运行完整安装预检，再签名上传。COS 复用 1252 个已有对象，20 个资源实传 3,298,250 B；GitHub 草稿上传 21 个资源及 manifest.json，共 22 个正式附件。

第一次 GitHub 检查在已上传后发现，带授权的 releases/tags/V2.1.0 对未公开草稿仍返回 404，而已授权的 releases 列表包含该草稿。管理端改为在目标标签未找到时从已授权列表定位草稿，仍逐项检查附件哈希和大小。受控测试重现草稿标签 404、部分附件失败和恢复。真实恢复事务取回原草稿 ID 431145698，保留 COS 提交，复用全部 21 项资源，再上传资源 0 B。发布、线上回读和本地归档完成后，实际管理端显示成功。

COS 当前指针、COS 版本指针、GitHub 备用指针经接收端公钥验签后都与候选逐字一致，公开 Release 的 manifest.json 也一致。目标清单 SHA-256 为 723c1afc00ffc8ff3760306edf9c0f1e211a67e2d34954b0413028f808747e10。一个独立安装目录从完整的 1310 文件正式 V2.0.0 开始，经正式接收端在线发现 V2.1.0，实际写入 21 项、删除 11 项，最终 1310 个目标文件逐一匹配 SHA-256，私人配置和模拟存档保留。

本机 Steam 也通过常规 Pack Updater 窗口更新至 V2.1.0。11 条删除全部落实；仅本机历史候选安装的 Ship Edge Reverb 单独移入升级备份，未为朋友强制删除未受管文件。9 份本机配置按接收端规则保留个人内容，其余目标文件全部匹配正式哈希。原有 10 份存档/设置文件哈希不变。管理端基线已更新，9 个组件的 11 份登记归档至 V2.1.0，待发布区为空。

正式整合包 https://github.com/NT1Ho/LethalGoofballsPack-updates/releases/tag/V2.1.0
接收端程序 https://github.com/NT1Ho/LethalGoofballsPack-updates/releases/tag/updater-v1.17.1

源码依据：正常宽度船型 dfc8284；Wesley 精翻 43bc04d；图片阅读器 c8a8603；接收端与诊断实机验收 567cf0e。第三方优化包直接采用原作者发行文件。
