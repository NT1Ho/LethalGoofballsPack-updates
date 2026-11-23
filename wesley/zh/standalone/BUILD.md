# Wesley 汉化独立测试包构建记录

独立包和 V2.6 使用 `wesley/zh/display`、`wesley-image-reader` 和同一份翻译资源。独立包包含插件 WesleyDisplayZh 1.3.2 与 WesleyImageReader 0.1.4。1.3.2 的字幕数据沿用 1.3.1，覆盖清单为 31 段视频、226 个语音身份和 1,121 条字幕。Reader.wri 沿用 V2.6 的 298 条阅读记录，含 5 张中文手册图。已记录的语言疑点见 [1.3.1 验收记录](https://github.com/NT1Ho/LethalGoofballsPack-updates/blob/fb47980022aeae42faa53072000dc9ed9901f919/wesley/zh/subtitles/validation-v81-1.3.1.md)。

| 文件或模块 | 来源与用途 |
| --- | --- |
| 文字和交互词库、终端词库 | 选取已校对的 V2.6 安装候选内 6 份原文件。自定义词库放在中文运行库旁的 `custom-localization/`，终端单独从 `translations-clean/cfg/zh-CN/` 加载。 |
| LC Chinese Project 3.3.2 | Aueser 的原版 MIT 运行库。包含字体、基础文本词库和初始化所需的自绘启动警告图。Noto Sans SC 及其 TMP 字库随附 OFL 1.1。 [官方 ZIP](https://thunderstore.io/c/lethal-company/p/Aueser/LC_Chinese_Project/v/3.3.2/) · [源码标签](https://github.com/Auuueser/LC-Chinese-Project/tree/v3.3.2) |
| WesleyV81Interiors | 原 Interiors 5.0.19 的辅助组件引用了拆分前的 DunGen 类型。为了让装了官方原包的玩家仍可进对应室内，兼容模块在启动时将该组件实际引用的六个顶层类型指向 V81 的 `DunGen.dll`。游戏和官方 MOD 文件保留原字节。 |
| 字幕和阅读运行对象 | 干净 V81 初始化会提前销毁共用的 BepInEx 组件。两个汉化功能改用独立的 Unity 对象，通过场景切换恢复。字幕、谜题、剧情与交互规则继续沿用已有实现。 |

## 重建独立 ZIP

准备独立的原版 V81 与 Moons 7.0.18 官方依赖作为编译参考。现有整合包版的 JLL 目录布局也受支持。锁定的中文运行库 ZIP 从官方获取，其 SHA-256 由组包脚本核对。

```powershell
dotnet build wesley/zh/display/WesleyDisplayZh.csproj -c Release -p:GameRoot="<V81 隔离目录>" -o "<输出目录>"
dotnet build wesley-image-reader/WesleyImageReader.csproj -c Release -p:GameRoot="<V81 隔离目录>" -o "<输出目录>"
dotnet build wesley/zh/standalone/v81-interiors/WesleyV81Interiors.csproj -c Release -p:GameRoot="<V81 隔离目录>" -o "<输出目录>"
python wesley/zh/subtitles/check.py
python wesley/zh/standalone/package.py --candidate "<校对完成的 V2.6 完整候选 ZIP>" --runtime "<Aueser-LC_Chinese_Project-3.3.2.zip>" --binaries "<输出目录>" --output "<输出目录>/NT1Ho-Wesley_Moons_ZH-1.0.0.zip"
```

更新翻译时先按 `wesley/zh` 与 `wesley-image-reader` 的现有流程更新共用术语、词库及阅读数据，再从新版完整候选组装。`package.py` 只提取明确列出的文件，并检验 ZIP CRC 及输出每项 SHA-256。

## 并入整合包

使用同一构建的 `WesleyDisplayZh.dll` 和 `WesleyImageReader.dll` 覆盖 V2.6 原组件位置，继续由 V2.6 已安装的一份 `LC Chinese Project` 加载同样的 6 份词库。`Reader.wri` 仍放在阅读器旁边。V2.6 已适配 Interiors 的 DunGen 引用，保留该适配即可。
