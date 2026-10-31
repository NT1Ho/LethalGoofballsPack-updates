# V1.9.2 六项本地维护（未发布）

基线：本机 Steam Lethal Company V81 / LethalGoofballsPack V1.9.2。仅本地安装和本地 Git 提交，不推送 GitHub/COS/更新器，不上传完整包。

## 改动

1. `LethalGoofballsPack.Maintenance.dll`：给老鸟腿骨和机身增加仅供原版铲子命中查询使用的碰撞体。原版仅爪部有 `EnemyAICollisionDetect`，腿部/机身碰撞体不能直接接收 `IHittable`。新增碰撞体在 `Shovel.HitShovel` 内短暂启用，结束立即关闭，且不执行接触攻击。伤害仍走原版与现有 ECDA/H MoreCounterplay；原生单次去重、视线遮挡和距离判定保留。未改抓取碰撞盒、HP、伤害、售价、其他怪物。
2. 同一小补丁在原版操作提示行显示真实保险状态及 `枪内弹药：0/1/2 / 2`。保留丢弃和装填行。未安装 ShotgunAmmoHUD/Better_Shotgun_Tooltip，避免重复 UI；没有修改射击/装填/保险行为。
3. 单层双侧加宽；安装下表五个功能 MOD 及三项缺失依赖。
4. `OpJosMod.ReviveCompany.cfg`：DeadBodyWeight 3.25 → 2.125。原版增加负重为 `(weight-1)*105`，所以实际搬运从 236.25 → 118.125 磅，正好 50%。怪物尸体配置不动。
5. Touchscreen：E 操作（含 Ping），鼠标左键 Flash，额外快捷切换键保持未绑定。原终端功能保留。
6. `MegaPiggy.BuyableShotgunShells.cfg`：ShotgunShellPrice 100 → 80。其余经济值不动。

## 官方包与依赖

| 包 | 版本 |
|---|---|
| mborsh/Wider_Ship_Mod | 1.4.3 |
| TestAccount666/ShipWindows | 2.11.1 |
| AtomicStudio/Moved_Magnet_Switch | 1.5.0 |
| mattymatty/Matty_Fixes | 1.1.41 |
| TheDeadSnake/Touchscreen | 1.1.5 |
| LethalCompanyModding/VertexLibrary | 1.1.0 |
| TestAccount666/TestAccountCore | 1.18.0 |
| WhiteSpike/Interactive_Terminal_API | 1.3.3 |

来源：各包的 `https://thunderstore.io/c/lethal-company/p/<namespace>/<name>/` 与官方 experimental package API。下载时核对最新稳定包及 manifest 依赖；所需 BepInEx、DawnLib 0.9.26、MonkeyInjectionLibrary 1.0.3、InputUtils 已存在，未重复安装或升级。

参考实现：
- https://github.com/xntkrnl/WiderShipMod
- https://github.com/Test-Account666/LethalCompany-ShipWindows/
- https://github.com/mattymatty97/LTC_MattyFixes
- https://github.com/TheDeadSnake/LC-Touchscreen
- https://thunderstore.io/c/lethal-company/p/oddninja/ShotgunAmmoHUD/
- https://thunderstore.io/c/lethal-company/p/AtomicStudio/Better_Shotgun_Tooltip/

## 兼容处理

- ShipWindows 不覆盖 Celestial Tint 的天空；保留正常购买规则，终端输入 `windows` 购买。未开启额外语音包。没有双层船。
- Moved Magnet Switch 使用自身 Wider Ship 兼容分支。
- Matty Fixes 保留储物柜/船体落地保护；关闭其全局物品高度、旋转修正，防止覆盖已验收的扳手、便携地雷、电源之心位置。未开启无关徽章/粒子/雷达修正。现有 GI 对应物品修复配置本就关闭，不额外修改 GI。
- 保留 SpaceShipDoor、OpenBodyCams、GeneralImprovements 及所有既有 DLL。
- Steam 的 Universal Wrench **0.1.24-dev**（较任务历史中的 0.1.23 更新）整目录受哈希保护，不覆盖 DLL、资源或音效。

## 构建、安装与恢复

`src/PackMaintenance.csproj` 引用本机 V81 程序集，Release 构建。`Stage.ps1` 只组装所需增量文件；`Install.ps1` 检查 Steam 游戏未运行，备份被替换文件，再逐项安装并比对 SHA-256。`install-plan.json` 记录哪些文件原先存在，备份目录可恢复旧文件；新增目录可按该清单撤销。

`tests/` 是隔离双进程的运行时验证探针，强制要求隔离标记，**不安装到 Steam、不进入增量包**。原版反编译资料、第三方资源、测试存档及日志不纳入本地源码提交。

历史发布清单和正式目录保持原样；本地新增资料明确标记“未发布”，不伪造正式版本或发布状态。
