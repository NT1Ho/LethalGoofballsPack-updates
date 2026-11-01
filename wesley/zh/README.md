# Wesley V81 简体中文（文字扩展，候选）

依赖本地 V81 TEST CHN 3.3.2（Aueser.LCChineseProject）。无需附加游戏插件。候选增量内，自定义 TMP 文字位于 BepInEx/config/V81TestChn/custom-localization/Wesley/；终端专用精确词表位于 BepInEx/plugins/V81TestChn/translations-clean/cfg/zh-CN/wesley-terminal-*.cfg。第一次 V81 检查发现仅写 custom 词库时，1Burnt note 的实际 TMP 仍显示英文。反编译确认内置终端正文使用另一张 ExactMap，再经 Trim 与换行归一；增加第二入口后，客户端真实 Terminal.LoadNewNode 显示相同日志的中文正文。对应原始记录在本次 evidence/ui-log.json 及 evidence/ui-log-final.json。该抽样不等于任意隐藏剧情或所有终端页面通过。

针对已锁定安装包读取 164 个 JSON/Unity 文件、11,235 个字段位置，另检查 Shenanigans 与 CustomStoryLogsFixed 的动态显示调用。覆盖总表只计入已选择的实际可显示字段、固定值进度提示与 154 条可推导拾取通知。2086 个去重源文本中，1921 项交付独立词条（438 项另写终端入口）；135 项复用基础汉化，25 个裸通用词或 UI/坐标标识避开全局替换。4 个生物编号及一个仅由点位组成的图形保留原样。154 篇日志对应 153 个去除首尾空白后的独立正文，均有中文；154 个英文标题中 150 个有双语词条。53 段关卡描述、14 个室内名、17 个敌人显示名、5 个天气名有词条。22 个图鉴字段有 21 个文字译项及一个原样图形。652/720 个物品名有独立词条，另 60 个复用基础汉化，其余为原标识/通用短词。参见 coverage-summary.json 的分项统计。重复内容、同一名称多处出现、两个加载入口不可相加算覆盖。

翻译工作使用离线英中模型作初译，对主要日志、敌人介绍、任务等再按全文上下文翻译。术语与 944 个短名的编辑修订、按上下文手校的操作、谜题和重点叙事另存。具体检查过 Cosmocos 的大小写和拼读、G.A.R.Y. 对话、计量谜题罗马数字及任务进度等；保留原英文终端名及变量。发布词条中 328 项仍为上下文译稿，未逐篇人工通读，尤其注意长日志人物关系、旧资料中的双关和音译。1593 项来自已编辑用语或其确定性拼接，不能计作 1593 次真人游戏验证。当前仅确认已抽样的显示路径，任务完成、存档、图鉴和其他文字的游戏证据请参见总测试矩阵。未查证的动态字符串不纳入分母。音频、视频、图片汉化不在最终范围内。

## 复跑

使用 Python 3.12、UnityPy 1.25.4，参数均可传绝对路径。将依赖锁表中的上游包放入候选目录后执行：

    python extract.py <候选/BepInEx/plugins> <本目录/private/v2> --unitypy <现有UnityPy路径>
    python build.py <候选>
    python verify.py <候选>

缓存按 bundle SHA256 命名；本次 164 个文件均无读取错误。切换上游版本时使用新的 private/v2 缓存目录并重新审核词条；复跑不可混入旧版本结果。build 通过英语源文本 SHA256 选中 translations.zh-CN.json 中的定稿，生成词库、private/coverage.tsv 与 private/locations.tsv。verify 按两种实际转义语法回读并核对键值、数值、控制变量、标签、容量与图鉴/日志收录，输出 coverage-summary.json。英文资源、反编译结果、模型文件、所有 private/ 临时文件仅留本机。源码仓库只保存自写脚本、术语、修订、哈希对应的译文和去原文的覆盖摘要。生成的玩家 cfg 内包含匹配显示所需的原英文。

回退只需移除上述 Wesley 子目录与两份 wesley-terminal-*.cfg，再重启游戏。基础翻译文件、游戏或作者原始数据均不需要恢复。星球/日志实际命令、存档键、提示原始触发器、门锁和任务机理不由汉化修改。
