# Wesley’s Moons 简体中文汉化 · 1.0.0 测试版

适用于《致命公司》V81、Wesley’s Moons 7.0.18 和它的官方依赖。汉化包自带中文运行库和字体。

## 用模组管理器安装

1. 新建一个《致命公司》配置档。在管理器中安装 Wesley’s Moons **7.0.18**，让管理器一并安装该版本需要的依赖。原作者提醒，r2modman 可能装不了较大的主包，此时使用 Gale 安装。
2. 选收到的 `NT1Ho-Wesley_Moons_ZH-1.0.0.zip`。Gale 的入口在「Import → local mod」，r2modman 或 Thunderstore Mod Manager 的入口在「设置 → Import local mod」。
3. 从同一个配置档启动模组游戏。

如果这个配置档已经单独安装 `LC Chinese Project` 或旧版 Wesley 汉化，请先在此配置档停用旧副本，避免加载同一插件两次。朋友也要单独导入收到的 ZIP，本地 Mod 随配置档分享时不会自动传给其他人。

## 手动安装

确认游戏已经装好 Wesley’s Moons 7.0.18 与其官方依赖。退出游戏并解压 ZIP：

1. 把包内的 `plugins` 文件夹改名为 `NT1Ho-Wesley_Moons_ZH`，将它放进游戏的 `BepInEx/plugins/`。
2. 把包内的 `patchers` 文件夹复制到游戏的 `BepInEx/`。这会把一个 V81 兼容补丁放进 `BepInEx/patchers/`。

## 使用与卸载

- 终端、星球路线、日志、道具与任务文字正常显示简体中文。原版命令、密码和解谜顺序按游戏提示操作。
- 录像、广播以及已覆盖的语音会自动出现中文字幕。对准或拿起受支持的资料时，按原来的 `Z` 查看。
- 用管理器安装的版本在相同配置档里卸载。手动安装的版本退出游戏后删除 `BepInEx/plugins/NT1Ho-Wesley_Moons_ZH/` 和 `BepInEx/patchers/WesleyV81Interiors.dll`。升级时，用新的 ZIP 替换原有这一份安装。正常游戏存档无需删除。
- 联机时字幕和阅读面板在每一端本机显示。想看中文字幕的玩家都应安装这份汉化。

如果遇到问题，把游戏版本、房主或客机、星球／房间、触发步骤和截图发给分享 ZIP 的朋友。遇到加载错误可附上 `BepInEx/LogOutput.log` 中对应几行。

[Wesley’s Moons 7.0.18 与官方依赖](https://thunderstore.io/c/lethal-company/p/Magic_Wesley/Wesleys_Moons/v/7.0.18/) · [Gale](https://github.com/Kesomannen/gale) · [LC Chinese Project 3.3.2](https://thunderstore.io/c/lethal-company/p/Aueser/LC_Chinese_Project/v/3.3.2/)
