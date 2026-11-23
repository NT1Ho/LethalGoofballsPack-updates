# Wesley 中文媒体字幕

字幕随现有 Wesley 汉化的 WesleyDisplayZh 1.3.1 交付。基础为 V2.6 本地候选中的 V81、Wesley Moons 7.0.18、Interiors 5.0.19、Shenanigans 1.0.25 与 V81TestChn 3.3.2。WesleyImageReader 与原版开场播报沿用各自现有路径。

## 播放与显示

- 录像带和预览视频读取 Unity 的 VideoPlayer.time，普通音频读取 AudioSource.timeSamples。一次性音频没有独立公开的进度接口，按本地 DSP 时钟和音调跟踪。实际停止、静音、距离变化或媒体替换均按当前播放状态重新判断。
- 视频和语音共用查表和 HUD。以媒体原名和帧数或音频时长区分同名资源。播放入口负责注册，切入场景后补查已自动开始的媒体，之后仅更新活跃播放对象。
- 对白淡黄，画面文字白色。两句各能排成一行时可同时显示，超出两行时优先保留画面线索。采用原有简中字体。背景框用当前渲染文字的范围决定大小，黑色透明度为 50%。字幕位置跟随当前物品栏上沿和 HUD 比例。
- 取音源的启用、静音、监听器音量、3D 混合、最小／最大距离与衰减值估计本地可听性。两个音源同时可听时，当前字幕优先保持到明显更响的音源出现。语音的最终混音器效果和单个 PlayOneShot 的独立播放位置由 Unity 持有，外部的可听性与一次性语音进度为估算。

V81 原版没有通用的外置媒体时间轴显示入口。现有中文插件已含原版飞船开场介绍，因此继续由它负责。KasetAltyazi 1.2.1 的发布包有 253 个文本资源，包含 29 个视频索引及 224 个语音索引。实际代码采用定时扫描播放器、媒体播放时钟及音源距离判定。本项目使用这些索引辅助资源核查，时间轴和中文译文依据 V81 本地原素材和对应的项目文稿。 [KasetAltyazi 发布页](https://thunderstore.io/c/lethal-company/p/IssizlerCommunity/KasetAltyazi/) · [Unity VideoPlayer.time](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Video.VideoPlayer-time.html) · [Unity AudioSource.timeSamples](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource-timeSamples.html)

## 覆盖和证据

从当前候选的 Wesley 资源检出 94 个视频引用，按内容哈希归并为 86 份。扫描到 8,610 个音频资源引用，分流提取用于第一轮转录的 2,176 份不同音频数据。空字幕、音乐、呼吸和环境效果均记录在核查范围内。分批对视频抽帧，使用离线 OCR、原声转录和游戏内现有文稿互相核对。优先人工校改坎宁安和哈林顿日志、广播、幽暗对讲机、贝鲁纳、受害者、韦斯特曼和谜题线索。

候选含 31 段视频、222 段语音，共 1,117 条时间轴。复核了首轮的 23 段部分覆盖资源、61 项待复核音频、53 项初筛视频，也从其余转录中找回埃特恩的《清除协议》录音。按原作日志校订清除协议、鹰计划与胡桃夹子计划的用词。格拉塔尔的厂区广播加入可确认的提示和第 4 个字母的线索。阿西迪尔的评级、菲利特里奥斯广告免责声明与统一领录像画面文字按实际素材补全。

[媒体清单](media-index.tsv) 包含帧数或毫秒时长、内容哈希、所在资源包和入轨状态。[逐项收尾核查表](closure-review.tsv) 记录复核结论及仍难辨认的具体片段。「部分字幕」保留未能确认的内容及范围。「未能转写」用于原音和多种离线处理均未给出一致措辞的资源。「原画数字」用于画面本身已清楚显示的倒数。分类结果反映现有素材证据，现有听录仅用于和本地音轨互相印证，原作文件留在本地。实际触发与联机测试另见验收记录。按实际音轨和地图原稿确认火焰之河、溺水室、尤特里尔和全设施净化。 [亚特兰蒂卡](https://wesleysmoons.wiki.gg/wiki/Atlantica) · [莱卡罗](https://wesleysmoons.wiki.gg/wiki/Lecaro) · [塔拉索](https://wesleysmoons.wiki.gg/wiki/Thalasso) · [格拉塔尔](https://wesleysmoons.wiki.gg/wiki/Gratar)
## 维护

- 直接编辑 tracks.zh-CN.tsv。视频的身份列为帧数，音频为毫秒时长。每一行包含起止毫秒和对白 D／画面文字 W。两行字幕在同一字段中用反斜杠与 n 表示。每个媒体同一类型的时间段应有序且互不重叠。
- 优先更新同目录上一级的 terms.zh-CN.tsv，再核对涉及的正文、台词、代码和物品名称。通过 check.py 校验媒体身份、时间、代表性术语和固定线索。
- 使用同一目录上一级的 extract.py 增加参数 --media-fingerprints 读取更新前后的隔离安装。为每个版本使用单独缓存目录，通过 check.py --compare 前一缓存 后一缓存 报告媒体新增、更新和移除。听读与实机验收随实际媒体差异进行。
