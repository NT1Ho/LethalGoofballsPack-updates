# LethalGoofballsPack：AI 从这里开始

这是 Windows 11、Steam 正版《Lethal Company》V81 的私人维护整合包。当前整合包版本 **V1.9**；接收端内部软件版本 **1.12.29.0**，两者不是同一个编号。用户曾称五人包，目前沿用 MoreCompany 的多人设置，不硬性限制五人。

先读本文件、`MOD_ADDING_GUIDE.md`、`docs/PRESERVED_CONFIG.md`、`inventory/MOD_CATALOG.json` 和 `docs/KNOWN_LIMITS.md`。不要扫描全部历史工作目录或重做旧功能。

## 主要功能与可信基线

简体中文、多人大厅、动作与服装饰品、怪物拟声、第一人称监控、废料与赌场、手动救援、自然回血、生态互动、太空活动、怪物尸体出售。以 `inventory/PACK_FILES.json`、当前签名清单和完整 ZIP 为实际交付依据。`BepInEx/pack-info/local-*.json` 中旧版本/未发布字段有历史记录性质，不能用它们推断当前在线状态。

`pack-reference` 只存配置和来源元数据；实际运行文件来自单独的 `LethalGoofballsPack.zip`，不包含 Steam 游戏文件。将 ZIP 解压到**独立工作目录**制作自己的包；不要覆盖原作者的正式目录。新增内容先做差异清单，除明确要求之外其余文件 SHA 必须保持不变。

## 自定义成果

- H增强版 MoreCounterplay：保留原作者反制，添加老鸟导电引雷与电击枪停机；含八音盒模型层级、重量与客户端提示修复。源码由独立维护项目负责；本开发包只消费已验证 DLL，不包含其私人源码。
- IndoorHazardGuard：只限制室内机关对怪物的伤害；室外老鸟导弹不受室内人数限制。依赖 FairAI 与 EverythingCanDieAlternative。
- KiwiBodies：复用 V81 啄木鸟模型，死亡事件后产生可搬运、保存、出售尸体。最终参数及测试边界见清单和已交付说明。
- Mirage / MoreCompany：保留假人继承玩家饰品的本地改版。不能用官方同版本 DLL 盲目覆盖。
- 只改配置的第三方 MOD 不标成自研。修改过的 DLL 以 SHA 标识，BepInPlugin 版本号可能未变。

## 开发与验证

1. 读 `docs/STRUCTURE.md`，确认生产树、独立工作副本和发布缓存彼此不同。
2. 使用现有配置、原始包 manifest、实际 DLL 元数据核对依赖；Optional/SoftDependency 不等于必装。
3. 下载官方指定版本，保留许可证和资源；没有证据就不要断言 V81 兼容。
4. 按 `MOD_ADDING_GUIDE.md` 做一次增量修改；不覆盖整个 config。
5. 构建更新器：Windows PowerShell 运行 `updater/Build.ps1`。只编译可用 `-SkipTests`；正式发布不能以此替代相关测试。
6. 用隔离安装目录验证文件安装、增量、失败回滚。MOD 游戏内验证需本人允许、使用合法游戏副本；正在游戏时不得启动、关闭或控制它。
7. 明确区分：静态检查、模拟 HTTP、真实下载、单机/同机双进程、异地多人测试。不能互相替代。

## 交接与独立发布

朋友先使用 `updater/tools/New-Fork.ps1` 创建自己的源码副本和仓库绑定，详见 `docs/FORKING.md`。该脚本不复制生产凭据、公钥绑定、缓存或正式 EXE。第一次设置生成自己的签名身份，无需原作者私钥。

正式频道仍是 NT1Ho 的仓库与广州 COS。不要把朋友的项目指向这个频道，不要复制 `.LethalGoofballsPackPublisher`。不要发布私人 MoreCounterplay 源码、游戏程序集、密钥、录音或存档。

未知许可证不代表许可。NewYearJester 歌曲授权是原用户的确认，不等于朋友拥有可转授的授权。公开分发前见 `docs/LICENSES.md`。

版本号只由项目所有者决定。本轮维护保留 V1.9。修改原作者渠道的同版本清单，必须使用原有合法签名机制；禁止改内容后沿用旧签名。
