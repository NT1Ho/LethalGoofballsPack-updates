using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace LethalGoofballsPack.Wesley
{
    internal static class HauntingPlayerSlots
    {
        internal static void Install(Harmony harmony)
        {
            var ghost=AccessTools.TypeByName("WesleysInteriorTools.EnemyAiChanges.SpecialGhostGirl");
            var choose=AccessTools.Method(ghost,"ChoosePlayerToHaunt");
            harmony.Patch(choose,transpiler:new HarmonyMethod(typeof(HauntingPlayerSlots),nameof(DynamicSlotBounds)));
        }

        // First loop also indexes stats. The weights array and second loop index only player slots.
        private static int StatSlots() => Math.Min(StartOfRound.Instance.allPlayerScripts.Length,
                                                  StartOfRound.Instance.gameStats.allPlayerStats.Length);
        private static int PlayerSlots() => StartOfRound.Instance.allPlayerScripts.Length;

        private static IEnumerable<CodeInstruction> DynamicSlotBounds(IEnumerable<CodeInstruction> input)
        {
            var il=input.ToList();
            var indexes=Enumerable.Range(0,il.Count-1).Where(i=>il[i].opcode==OpCodes.Ldc_I4_4).ToArray();
            if(indexes.Length!=3 || il[indexes[0]+1].opcode!=OpCodes.Clt ||
                il[indexes[1]+1].opcode!=OpCodes.Newarr || !Equals(il[indexes[1]+1].operand,typeof(int)) ||
                il[indexes[2]+1].opcode!=OpCodes.Clt)
                throw new InvalidOperationException("Unexpected SpecialGhostGirl player-loop IL");
            for(int i=0;i<indexes.Length;i++){
                var op=il[indexes[i]];
                op.opcode=OpCodes.Call;
                op.operand=AccessTools.Method(typeof(HauntingPlayerSlots),i==0?nameof(StatSlots):nameof(PlayerSlots));
            }
            return il;
        }
    }
}
