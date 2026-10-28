# LethalGoofballsPack

适用于 Windows 11、Steam 正版《Lethal Company》V81 的中文多人整合包。当前整合包版本 **V1.9**。保留 MoreCompany、汉化、怪物生态、服装饰品及 H增强版 MoreCounterplay 等既有功能。

这是运行包更新与维护资料仓库，不包含游戏本体，也不是私人 MOD 源码公开仓库。

## 玩家使用

已有整合包：把接收端放在 Lethal Company.exe 所在目录，退出游戏后双击。它核对签名及文件 SHA，只下载缺失或变动内容；不是每次下载完整 ZIP。国内可通过广州 COS 获取更新，GitHub 保留备用。完整包根目录直接包含 BepInEx 等文件，无额外外层目录。

不要关闭 Windows 防护。不要把管理端、上传密钥或私人签名密钥交给普通玩家。整合包版本仍是 V1.9，接收端软件内部版本可独立升级。

## 开发与接手

- [AI_START_HERE.md](AI_START_HERE.md)：其他 AI 的起点。
- [MOD_ADDING_GUIDE.md](MOD_ADDING_GUIDE.md)：添加 MOD 的流程。
- [MOD 清单](docs/MOD_CATALOG.md)、[重要配置](docs/PRESERVED_CONFIG.md)。
- [文件结构](docs/STRUCTURE.md)、[发布流程](docs/RELEASING.md)。
- [独立开发](docs/FORKING.md)、[故障与回退](docs/TROUBLESHOOTING.md)。
- [更新记录](CHANGELOG.md)、[验证边界](docs/KNOWN_LIMITS.md)、[许可范围](docs/LICENSES.md)。

根目录 mod-update.signed.json / client-update.signed.json 是正式签名入口；Release manifest.json 为兼容入口，必须与签名 payload 一致。不要手工改签名文件、删除历史附件或重命名历史标签。

本次修复保持所有 MOD DLL、配置、完整运行 ZIP 内容不变。新增的开发基础是独立的本地交接包，不是要求玩家重新安装游戏。
