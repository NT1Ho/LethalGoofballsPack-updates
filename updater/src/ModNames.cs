using System;
using System.Collections.Generic;
namespace LcPack {
public class ModFeature {
    public string Text{get;set;}public bool SelfDeveloped{get;set;}
    public string Tag{get{return SelfDeveloped?"自研":"";}}
}
public static class ModNames {
    static readonly Dictionary<string,string> descriptions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
        {"LethalGoofballs Diagnostics","本地故障记录、F8现场与截图；退出后在接收端备注和导出"},
        {"BetterStamina","优化体力消耗、恢复和负重影响"},
        {"BonkHitSFX","铲子等武器命中时播放 BONK 音效"},
        {"ButteryFixes","修复游戏中的多种问题"},
        {"Casino_v81_Patch","修复赌场在 V81 的兼容问题"},
        {"Celestial_Tint","增加轨道星球景观、光照和飞船外观"},
        {"CoilHeadStare","弹簧头缓慢转头凝视，挨打时晃动"},
        {"Coroner","结算时显示队员死亡原因"},
        {"Cute_Cosmetics","添加卡通、动物等可爱饰品"},
        {"EverythingCanDieAlternative","让更多怪物可以受伤和被击杀"},
        {"FairAI","怪物可触发地雷，也会被炮塔攻击"},
        {"FasterItemDropship","缩短物品快递的到达和停留时间"},
        {"FixCameraResolution","调整内部渲染分辨率和画面效果"},
        {"GeneralImprovements","优化操作、界面及多项游戏细节"},
        {"HotbarPlus","扩展物品栏，改善物品切换体验"},
        {"LCBetterClock","优化游戏时间显示"},
        {"LCMaxSoundsFix","提高同时发声上限，减少音效丢失"},
        {"LC_Chinese_Project","将游戏界面和文字汉化为简体中文"},
        {"LethalCasino","在公司星球添加赌场和多种小游戏"},
        {"LethalPresents","礼物盒可能开出怪物、地雷或炮塔"},
        {"LethalRegeneration","让受伤的玩家逐渐恢复生命值"},
        {"LobbyControl","管理大厅，允许在轨道中途加入"},
        {"MemeSuits","添加表情包主题宇航服"},
        {"Mirage","怪物模仿玩家声音，假人伪装成玩家"},
        {"MoreCompany","扩充大厅人数，支持更多玩家联机"},
        {"MoreCounterplay","弹簧头、八音盒、炮塔、黑豹及老鸟反制"},
        {"MoreMoneyStart","调整新开局的初始资金"},
        {"More_Suits","增加可更换的服装"},
        {"Natural_selection","生态链"},
        {"NaturalSelection","生态链"},
        {"KiwiBodies","让啄木鸟留下可搬运和出售的尸体"},
        {"Giant Sapsucker Bodies","让啄木鸟留下可搬运和出售的尸体"},
        {"NeedyCats","增加可抱起、抚摸和喂食的猫"},
        {"NewYearJester","八音盒播放《恭喜发财》，带春节外观"},
        {"NoDeathDespawn","全队死亡后保留飞船内废料"},
        {"OpenBodyCams","在飞船屏幕查看队友第一人称画面"},
        {"PigeonsCosmeticsAddon","添加帽子、肩饰、尾巴等配饰"},
        {"QuickSell","通过聊天指令快速出售废料"},
        {"RandomCosmetics","添加帽子、面具和身体配饰"},
        {"ReviveCompany_Patched","让玩家能够救活倒下的队友"},
        {"SellBodiesFixed","搬运怪物尸体并带回出售"},
        {"ShipLootPlus","统计飞船内废料的总价值"},
        {"SnatchinBrackenFORK","小黑抓人后拖走，队友可营救"},
        {"SnatchinBracken","小黑抓人后拖走，队友可营救"},
        {"SpaceShipDoor","在太空轨道打开飞船舱门"},
        {"SuitsPlus","添加多种角色和趣味主题宇航服"},
        {"SuitSaver","自动保存并恢复上次穿的服装"},
        {"Suit Saver","自动保存并恢复上次穿的服装"},
        {"TalkingHeads","说话时头变大，声音越大头越大"},
        {"TerrasScrap","增加可收集和出售的废料"},
        {"TooManyEmotes","增加表情动作、舞蹈和多人互动"},
        {"TooManyHats","添加多种帽子和趣味饰品"},
        {"TooManySuits","为服装架增加翻页，方便切换服装"},
        {"WarpBack","提供快捷返回飞船的功能"},
        {"YippeeMod","将囤积虫叫声替换为 Yippee"},
        {"BepInExPack","加载和运行游戏 MOD"}
    };
    static readonly Dictionary<string,string> support=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
        {"NaturalSelectionLib","支持怪物生态互动"},
        {"SpiderPositionFix","修复蜘蛛位置和动画同步"},
        {"CSync","同步房主的 MOD 配置"},
        {"IndoorHazardGuard","修复机关对怪物的伤害判定"},
        {"Indoor Hazard Guard","修复机关对怪物的伤害判定"},
        {"ChatCommandAPI","支持 MOD 使用聊天指令"},
        {"LethalModUtils","提供聊天指令等 MOD 所需的工具"},
        {"NiceChat","支持聊天指令输入和消息显示"},
        {"TooManyItems","修复物品保存、同步和飞船内掉落问题"},
        {"AutoHookGenPatcher","自动生成 MOD 所需的底层支持文件"},
        {"HookGenPatcher","生成 MOD 所需的底层支持文件"},
        {"DetourContext_Dispose_Fix","修复 MOD 底层资源释放问题"},
        {"FixPluginTypesSerialization","修复 MOD 数据读取问题"},
        {"MonkeyInjectionLibrary","支持 MOD 添加游戏对象功能"},
        {"MirageCore","支持 Mirage 的录音和语音同步"},
        {"Concentus","支持语音压缩与解压"},
        {"OpusDotNet","支持语音编码与解码"},
        {"NAudio","支持录音、音频处理和播放"},
        {"OdinSerializer","支持 MOD 数据保存和读取"},
        {"StaticNetcodeLib","支持 MOD 多人网络通信"},
        {"PathfindingLib","为怪物寻路提供性能优化支持"},
        {"MaskedInvisFix","修复假人伪装后的隐形问题"},
        {"MaskFixes","修复面具物品和假人的相关问题"},
        {"LethalConfig","在游戏内调整 MOD 设置"},
        {"LethalSettings","提供游戏内 MOD 设置入口"},
        {"LethalCompany_InputUtils","支持 MOD 快捷键和改键"},
        {"DawnLib","支持新增废料等 MOD 内容"},
        {"LethalLib","支持 MOD 添加物品和怪物"},
        {"LethalNetworkAPI","支持 MOD 在多人游戏中同步数据"},
        {"MoreMaterials","为服装提供材质和外观效果支持"},
        {"MonoDetour","提供 MOD 运行所需的底层支持"},
        {"MonoDetour_BepInEx_5","连接 BepInEx 与 MonoDetour"},
        {"其他配置","各 MOD 的设置文件"},
        {"整合包记录与公共文件","整合包版本、来源和校验记录"}
    };
    // Pack additions: Wrench V1.9.1 (pinned), MoreCounterplay V1.9,
    // KiwiBodies 2026-10-26 evening, IndoorHazardGuard earlier that afternoon.
    public static int OwnRank(string name){
        if(String.Equals(name,"UniversalWrench",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"Universal Wrench",StringComparison.OrdinalIgnoreCase))return 4;
        if(String.Equals(name,"MoreCounterplay",StringComparison.OrdinalIgnoreCase))return 3;
        if(String.Equals(name,"KiwiBodies",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"Giant Sapsucker Bodies",StringComparison.OrdinalIgnoreCase))return 2;
        if(String.Equals(name,"IndoorHazardGuard",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"Indoor Hazard Guard",StringComparison.OrdinalIgnoreCase))return 1;
        return 0;
    }
    public static bool IsMainMod(string name){return OwnRank(name)>0||descriptions.ContainsKey(name);}
    public static ModFeature[] Features(string name){
        if(String.Equals(name,"MoreCounterplay",StringComparison.OrdinalIgnoreCase))return new[]{
            new ModFeature{Text="弹簧头：小刀破坏弹簧，掉落头部"},
            new ModFeature{Text="八音盒：用重物压住盒盖"},
            new ModFeature{Text="炮塔：用小刀切断线路"},
            new ModFeature{Text="黑豹：用激光笔引导"},
            new ModFeature{Text="老鸟导电、吸引雷击",SelfDeveloped=true},
            new ModFeature{Text="电击枪控制老鸟、停机10秒",SelfDeveloped=true},
            new ModFeature{Text="修复八音盒放置物品报错、重复计重",SelfDeveloped=true},
            new ModFeature{Text="修复客户端八音盒放置提示消失",SelfDeveloped=true}
        };
        bool own=OwnRank(name)>0;
        return new[]{new ModFeature{Text=Description(name),SelfDeveloped=own}};
    }
    public static string Description(string name){if(String.Equals(name,"BuyableShotgunShells",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"Buyable Shotgun Shells",StringComparison.OrdinalIgnoreCase))return "终端购买霰弹枪子弹，每颗 100 Credits（房主价格同步）";if(OwnRank(name)==4)return "长按 E 修复损坏 Jetpack（500 Credits / 3 次）；原版圆环、齿轮提示及空间维修声";string value;if(descriptions.TryGetValue(name,out value)||support.TryGetValue(name,out value))return value;return name.StartsWith("MMHOOK_",StringComparison.OrdinalIgnoreCase)?"运行时生成的 MOD 挂钩支持库":"暂无用途说明";}
}
}
