using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace LethalGoofballsPack.Maintenance
{
    // Update polls the opposite door before it starts its one-second enemy check.
    // While the far side is being built, a failed lookup would otherwise run every frame.
    [HarmonyPatch(typeof(EntranceTeleport), "Update")]
    internal static class EntranceRetry
    {
        private static readonly AccessTools.FieldRef<EntranceTeleport, float> interval =
            AccessTools.FieldRefAccess<EntranceTeleport, float>("checkForEnemiesInterval");

        private static bool CheckExit(EntranceTeleport entrance)
        {
            bool found = entrance.FindExitPoint();
            if (!found) interval(entrance) = 1f;
            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var find = AccessTools.Method(typeof(EntranceTeleport), nameof(EntranceTeleport.FindExitPoint));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(find))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(EntranceRetry), nameof(CheckExit));
                }
                yield return instruction;
            }
        }
    }
}
