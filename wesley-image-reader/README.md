# Wesley 图片中文阅读

## 0.1.4 · 独立发行兼容

阅读界面优先使用原有系统字体。系统字体缺少中文字形时，使用随 LC Chinese Project 提供的 Noto Sans SC。`Reader.wri` 格式和内容沿用 0.1.3。

## 0.1.3 · V2.6 本地候选

在游戏配置的键盘「查看物品」按键上读取信息，默认 Z。瞄准环境中的文字表面，或手持有正文的便条时使用。再次按查看键关闭。长文用上下方向键和 Page Up / Page Down 滚动。

- 大军械库的导弹保险箱按实际屏幕材质显示当前的前置操作或任务完成状态。正在阅读时，屏幕换成已收录状态即更新正文，屏幕熄灭或换成未知状态即关闭。摩尔斯电码表保留原图上的字母、数字及点划。
- 秘密档案使用游戏的原版检视姿势，放大时同时显示封面中文。卡牌手册使用原版检视与 Q/E 翻页。普通可检视物品沿用游戏按键路径。
- 阅读器复用原有 IMGUI、系统中文字体和原版背景透明度。窗口展示标题与正文，长文还有滚动位置指示。按实际宽度断行，保留原有分段、清单和任务代码。

TextMeshPro 有亚洲文字断行表，接入时还需要为此窗口建立 Canvas 并接好中文字体资源。沿用已有 IMGUI 可直接在排版时测量现用字体，改动集中在阅读窗口。[Unity GUIStyle 测量](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/GUIStyle.CalcSize.html) · [TMP 断行设置](https://docs.unity3d.com/ja/Packages/com.unity.textmeshpro%403.0/api/TMPro.TMP_Settings.html)

构建命令为 `dotnet build WesleyImageReader.csproj -c Release -p:GameRoot="<隔离 V81>"`。执行 `python revise.py <0.1.2 的 Reader.wri> <0.1.3 输出 Reader.wri>` 生成匹配资源。脚本校验已安装 0.1.2 的数据哈希，并读取本目录的 `revision-0.1.3.json`。五张原尺寸中文手册页直接沿用。拾取和丢弃提示复用阅读条目标题，保留原始物品身份。

## 0.1.2 · 历史记录

独立的 V81 / BepInEx 5 本地阅读插件。将 WesleyImageReader.dll 与同版本 Reader.wri 放在同一插件目录。每个客户端只控制自己的画面。

- 场景招牌、门牌、屏幕、方向标记和柜台告示保留原版美术。手持阅读物或瞄准已收录的表面时，Z 打开中文正文，再按关闭；长文支持上下箭头及 Page Up / Page Down 滚动。大幅固定招牌按其世界尺寸允许适度远读；遮挡物仍会阻止读取。
- 卡牌手册五页直接显示中文。手持时 Z 交回原版检视以放大、收起，Q/E 翻页。阅读窗口不接管此时的 Z。
- 本版资源为 5 张原尺寸书页、290 份正文。后者含原有 244 项、恢复原图的 29 项和 17 项新增阅读条目。收录或存在映射，不表示每个场景的每种状态都已实测。

## 构建和维护

用 .NET SDK 构建本目录的 csproj，必要时通过 GameRoot 指向装有 BepInEx 的 V81。执行 `python prepare.py <原始交接ZIP> Reader.wri`。五页原图资产须自行持有；仓库仅含源码、数据映射、补充正文及验证记录。additional-readings.json 描述本次复核的屏幕状态和其他新增阅读物。识别使用实际显示的材质、纹理、尺寸和对象路径；可读取的多材质网格还区分准星命中的三角形。只在请求阅读时做射线判定，无网络消息及持续识图。

详见《验证记录.md》。磁盘上的 Wesley 原版资源不作修改。
