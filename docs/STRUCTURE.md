# 文件结构与来源

| 位置 | 作用 | 修改原则 |
|---|---|---|
| `D:\Lethal company pack Updater\Mods\LethalGoofballsPack_V1.9` | 正式待分发运行树，926 文件 | 只改批准的增量 |
| 同目录 `LethalGoofballsPack.zip` | 玩家完整包 | 本轮 MOD 内容未变，ZIP 保持原样 |
| `updater/src` | 更新器、发布器源码 | 小范围修改，构建和相关回归测试 |
| `updater/assets`、`lib` | 图标、更新历史、Cecil 与许可证 | 保留资源名及引用 |
| `updater/tools` | 离线检查、打包、独立渠道、回退导出 | 不自动上传、不操作游戏 |
| `updater/tests` | 测试源码 | 运行产物不打入交接包 |
| `inventory` | 当前文件、配置、DLL、MOD 基线 | 是快照，不是上游最新版证明 |
| `pack-reference`（交接包中） | 原配置与发布来源记录 | 不是可运行的完整 MOD 包 |
| `work`、`outputs` | 原始下载、反编译、实验、历史交付 | 不作为最终版本猜测依据，不整体公开 |
| `%USERPROFILE%/.LethalGoofballsPackPublisher` | DPAPI 凭据、签名私钥、发布草稿及对象缓存 | 私人目录，绝不交接/入 GitHub |
| 游戏根目录 `.lcpack-updater` | 玩家更新状态、备份、自更新缓存 | 不打入 MOD 包，不随意删除 |

发布依据：签名清单 → 路径、大小、SHA-256、GitHub URL；COS 对象由 SHA 寻址。相同内容可放在不同必要目录，不应按 DLL 文件名随意删除。当前 MonoMod 两组同哈希副本位于 core 和 patchers，已保留。

正式游戏目录、工作副本、交付 ZIP、发布对象缓存各有用途，不通过整体移动它们来“统一目录”。其他 AI 不需要原 Codex 线程就能按文档添加 MOD；对私人自定义插件核心代码的继续开发需另取其维护者授权源码。
