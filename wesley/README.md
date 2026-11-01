# Wesley V81 多人候选（未发布）

线上基线为 GitHub 和广州 COS 一致的 V1.9.5。候选继承 Steam 上实际插件、私人构建、配置和未发布的维护改动。主包 Moons 7.0.18、Interiors 5.0.19，番外 Facto 1.2.1、Decrepit Store 1.0.2。17 个新增包、11 个复用前置。剧情锁启用，房间人数上限 50。维护起点 e472747；扳手从 dbc3406 更新到 d6222c8，开发分支 art/phase2b-game-preview，0.1.30-dev。

## 使用与回退
delivery/LethalGoofballsPack-Wesley 是提供给独立合法 V81 副本的完整 MOD 文件集。最终 1,308 个文件已逐个核对 SHA-256。evidence/baseline.json 和 evidence/changes.json 记录基础文件及增量。delivery/rollback 含被替换文件原件。只回退独立候选的命令：

    ./delivery/Undo-Candidate.ps1 -CandidateRoot ./delivery/LethalGoofballsPack-Wesley -Delivery ./delivery

管理端下次可从独立候选目录导入。pack-info 含来源、文件哈希、查看本地和历史页。维护者确定正式版本后，替换候选标题及草稿日期。接收端读库校验四个内容包资产匹配，Universal Wrench 显示 0.1.30-dev 并自研置顶。第三方作者保留。完整库、回退文件和原始素材仅在本机。

## 实机与检查边界
- V81 主客同机 LAN，实际 2 人。客户端拾取 Galetry 自然生成的录像带，经作者播放器提交，Asteroid-13 从锁定、隐藏变为可前往。原英文 route、confirm、返轨保存与重开均已执行。早期读档晚加入客户端看到默认锁；修复后退出重连，两端所有航线 flags 一致，Asteroid-13 开放而 Hyx 保持隐藏。
- Facto、Extort 和 Duckstroid 完成落地、真实入口进出及返轨抽查。Extort 初次因同编号多个入口造成探针误判，结算 UI 读取失效 Sprite.name 则真的打断返航。两处分别修复并复测。Duckstroid 首次出现加载、生成与着陆日志，随后玩家团灭、飞船离开，探针最后读到轨道；复测成功，未发现必须修改地图的证据。
- 扳手修复前取到剧情 LungProp，电源之心注册时抛异常。Extort 内原版设施电源长按 E 转换并扣 1 次，Bloody Apparatus 仅显示拾取，原物品及次数不变。QuickSell 的普通批量出售保护新增 91 个真实 prefab 名；显式 -a 或指定出售仍可绕过。复用原有救援、飞船和库存设置。
- 存档库遇空插件实例的问题已修；两端存档事件注册成功。室内 DLL 八个旧 DunGen 类型引用已指向 V81 实际程序集。特殊幽灵女孩的四槽硬编码改为读取实际数组。这些项目的静态及实机证据见 deps/UPSTREAM-AUDIT.md。
- 移除所有测试插件后通过游戏原生 LAN 菜单建立仅限本机的房间，看到船员上限 50 并进入飞船。进出建筑、返轨、存档与重连的证据来自 V81 隔离主客机，未再无辅助走完整航行。副本实际创建了独立数据目录 WesleyQaHost、WesleyQaPeer，未接触真实存档。
- 纯文字词库共 1,921 个独立译项，其中 438 项也进入终端正文表。154 篇日志含 153 种不同正文。原生终端实显《烧焦的便条》中文；未扫描敌人的图鉴界面返回资料未发现，该样本不算正文实显。328 项上下文译稿尚未逐篇人工校读。各类计数见 zh/coverage-summary.json。本轮不交付字幕、贴图、告示和配置菜单汉化。
- 通过限定范围的基础多人检查。感染复活长链、各地图独特任务、所有图鉴的显示及异地/更大规模联机均未穷举。源码分支是候选资料，不是正式发布。

复跑入口：deps/packages.lock.json、deps/Relink-InteriorDunGen.ps1、deps/compat、zh/README.md、qa/QA.md。publisher/ 下的 Build-Pages.ps1、Build-Delivery.ps1、ReceiverCheck.cs 用于生成页面、组包及接收端读库校验。
