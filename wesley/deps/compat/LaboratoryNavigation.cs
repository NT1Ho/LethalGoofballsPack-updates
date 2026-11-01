using System.Collections.Generic;
using HarmonyLib;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LethalGoofballsPack.Wesley
{
    internal static class LaboratoryNavigation
    {
        internal static void Install(Harmony harmony) =>
            harmony.Patch(AccessTools.Method(typeof(NavMeshSurface), "CollectSources"),
                postfix: new HarmonyMethod(typeof(LaboratoryNavigation), nameof(OmitUnreadableDecor)));

        private static void OmitUnreadableDecor(NavMeshSurface __instance, List<NavMeshBuildSource> __result)
        {
            var generator = RoundManager.Instance?.dungeonGenerator;
            if (!generator || generator.Generator?.DungeonFlow?.name != "LabFlow" ||
                __instance.gameObject != generator.Generator.CurrentDungeon?.gameObject ||
                __instance.collectObjects != CollectObjects.Children) return;

            // Unity already skips these decorations during baking. Keep walkable geometry and collider sources.
            __result.RemoveAll(source => source.shape == NavMeshBuildSourceShape.Mesh &&
                source.sourceObject is Mesh mesh && !mesh.isReadable &&
                (mesh.name == "RollingChair" || mesh.name == "DeskAddonComponent" ||
                 mesh.name == "Cube" || mesh.name == "Cube.002"));
        }
    }
}

