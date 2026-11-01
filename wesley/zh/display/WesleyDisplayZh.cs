using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using CustomStoryLogs.Tools;
using HarmonyLib;
using TMPro;
using WesleyMoonScripts.Components;

namespace LethalGoofballsPack.WesleyZh
{
    [BepInPlugin("NT1Ho.WesleyDisplayZh", "Wesley 简中显示补充", "1.1.0")]
    [BepInDependency("JacobG5.WesleyMoonScripts")]
    [BepInDependency("Yorimor.CustomStoryLogs")]
    [BepInDependency("Aueser.LCChineseProject")]
    internal sealed class Plugin : BaseUnityPlugin
    {
        private void Awake() => new Harmony("NT1Ho.WesleyDisplayZh").PatchAll(typeof(Plugin).Assembly);
    }

    [HarmonyPatch(typeof(LevelCassetteLoader), "TapeEnded")]
    internal static class CassetteNotification
    {
        // The stock translator also expands the moon inside this sentence.
        // Localize the two literals at their source, before that expansion.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr)
                {
                    if (Equals(instruction.operand, "Location: ")) instruction.operand = "地点：";
                    else if (Equals(instruction.operand, "Route Discovered!")) instruction.operand = "新航线";
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch]
    internal static class MoonInfo
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LethalLevelLoader.ExtendedLevel).Assembly.GetType("LethalLevelLoader.TerminalManager", true), "CreateLevelTerminalData");

        private delegate bool Exact(string source, out string translation);
        private static readonly Exact Lookup = AccessTools.MethodDelegate<Exact>(
            AccessTools.Method("V81TestChn.CustomLocalizationExtensionService:TryTranslateFastExact"));

        private static void Postfix(LethalLevelLoader.ExtendedLevel extendedLevel)
        {
            var node = extendedLevel.InfoNode;
            var page = node?.displayText?.Trim();
            if (page != null && page.Contains("\n----------------------\n") && Lookup(page, out var chinese))
                node.displayText = chinese;
        }
    }

    [HarmonyPatch(typeof(LogPlacementTool), "ActivateTool")]
    internal static class PlacementLabels
    {
        private static void Postfix(bool ___isToolActive, LogPlacementUI ___canvasUI)
        {
            if (!___isToolActive || !___canvasUI) return;
            foreach (var label in ___canvasUI.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.text.StartsWith("CustomStoryLogs Placement Tool", System.StringComparison.Ordinal))
                    label.text = "剧情日志放置工具\nAlt+L：开关工具　Alt+K：切换编辑";
                switch (label.text)
                {
                    case "Position": label.text = "位置"; break;
                    case "Rotation": label.text = "旋转"; break;
                    case "Enter text...": label.text = "输入文字…"; break;
                    case "Moon":
                    case "卫星条目": label.text = "当前星球"; break;
                    case "Save to Json": label.text = "保存为 JSON"; break;
                }
            }
        }
    }
}
