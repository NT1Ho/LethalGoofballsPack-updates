# 朋友创建自己的整合包

只想本地添加 MOD：将原 ZIP 解压到一个新目录，让 AI 读 AI_START_HERE.md 和 MOD_ADDING_GUIDE.md，再做增量修改。**不要运行原作者接收端更新自己的改版**，它会按原频道修复文件。

需要自己的在线发布：

1. 以 Windows PowerShell 运行：`updater/tools/New-Fork.ps1 -Destination D:/MyLcPackUpdater -Repository YourName/YourPack`。目标必须不存在。此脚本只复制允许的源文件，改自己的仓库和本机私密目录，不动原项目，不发送网络请求。
2. 注册自己的 GitHub 仓库与 COS 桶。COS 可用广州或香港、公有读私有写；费用自行承担，不使用原作者账号。
3. 在新目录执行 `Build.ps1 -SkipTests`，打开临时管理端，在本机填自己的 COS 域名/上传密钥。第一次保存会生成自己的签名密钥。
4. 把新私密目录中的 `cos-channel.json`（仅公钥/域名）复制到新项目根目录，重新构建。该私密目录名称写在脚本生成的说明中。
5. 需要 GitHub 备份时，在 Windows PowerShell 运行 `tools/Set-GitHubCredential.ps1`，使用自己仓库 Contents: Read and write 的 token。密钥在本机隐藏输入，不发给 AI。工具核对仓库所有者及可写权限后 DPAPI 保存。
6. 将**独立整合包目录**全部拖入新管理端，使用自己的版本号；不可导入原作者 draft.json 或 signed manifest。首次发布会建立自己的对象、地址与签名。
7. 完成必要测试后，只发送重新绑定并构建的接收端。首次未绑定 COS 的临时构建不能交给玩家。

原正式发布程序不在交接包中，避免误用原频道。交接包提供源码，可直接编译出管理端和接收端。

独立生成测试已验证：源码、默认私密目录和仓库均隔离，两个 EXE 可构建。未替朋友创建收费账号、仓库或真实发布渠道。
