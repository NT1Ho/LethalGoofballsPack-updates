using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack.Maintenance
{
    [HarmonyPatch(typeof(TimeOfDay), "Awake")]
    internal static class DayClock
    {
        private static void Postfix(TimeOfDay __instance) => __instance.globalTimeSpeedMultiplier *= .5f;
    }

    // DoorBreach groups knives, firearms and shovels under PLAYER.
    internal static class LockedDoorRules
    {
        internal static void Install(Harmony h)
        {
            var health = AccessTools.TypeByName("DoorBreach.Functional.DoorHealth");
            h.Patch(AccessTools.Method(health, "HitDoorServer"), prefix: new HarmonyMethod(typeof(LockedDoorRules), nameof(IsLockedShovelHit)));
            h.Patch(AccessTools.Method(health, "BreakDoorClient"), postfix: new HarmonyMethod(typeof(LockedDoorRules), nameof(Unlock)));
        }

        private static bool IsLockedShovelHit(Component __instance, int playerWhoHit, ref int damage)
        {
            int index = playerWhoHit - 4096; // DoorBreach.ActionSource.PLAYER
            var players = StartOfRound.Instance.allPlayerScripts;
            if ((uint)index >= players.Length) return false;
            var p = players[index];
            var door = __instance.GetComponent<DoorLock>();
            if (!door || !door.isLocked || door.GetComponent<AnimatedObjectTrigger>().boolValue || !p || !p.isPlayerControlled || p.isPlayerDead ||
                !(p.currentlyHeldObjectServer is Shovel shovel) || shovel.itemProperties.itemName != "Shovel" ||
                (p.transform.position - door.transform.position).sqrMagnitude > 16f) return false;
            damage = 1;
            return true;
        }

        private static void Unlock(Component __instance, ref int ____health)
        {
            var door = __instance.GetComponent<DoorLock>();
            if (door.isLocked) door.UnlockDoor();
            ____health = 5;
        }
    }
}
