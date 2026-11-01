using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack.Wesley
{
    internal static class ResultsSprite
    {
        internal static void Install(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue("Aueser.LCChineseProject", out var plugin)) return;
            var type = plugin.Instance.GetType().Assembly.GetType("V81TestChn.EndGameLocalizationService", true);
            var patch = new HarmonyMethod(typeof(ResultsSprite), nameof(SafeNames));
            harmony.Patch(AccessTools.Method(type, "FindStatsBoxesImage"), transpiler: patch);
            harmony.Patch(AccessTools.Method(type, "ScoreStatsBoxesCandidate"), transpiler: patch);
        }

        // Managed null checks on Sprite wrappers do not detect destroyed native sprites.
        // Keep the end-of-round RPC and localization itself running.
        private static string ReadLiveName(Object item) => item ? item.name : string.Empty;

        private static IEnumerable<CodeInstruction> SafeNames(IEnumerable<CodeInstruction> instructions)
        {
            var readName = AccessTools.PropertyGetter(typeof(Object), nameof(Object.name));
            var safeName = AccessTools.Method(typeof(ResultsSprite), nameof(ReadLiveName));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(readName))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = safeName;
                }
                yield return instruction;
            }
        }
    }
}
