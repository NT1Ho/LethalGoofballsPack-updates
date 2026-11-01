using HarmonyLib;
using UnityEngine;
using GameNetcodeStuff;

namespace LethalGoofballsPack.Wesley
{
    internal static class ExteriorPlatform
    {
        internal static void Install(Harmony harmony) =>
            harmony.Patch(AccessTools.Method(typeof(AudioReverbTrigger), "OnTriggerStay"),
                prefix: new HarmonyMethod(typeof(ExteriorPlatform), nameof(PreferPlatform)));

        private static bool PreferPlatform(AudioReverbTrigger __instance, Collider other)
        {
            if (!__instance.isShipRoom || !other.CompareTag("Player") ||
                !other.TryGetComponent<PlayerControllerB>(out var player)) return true;

            var outside = player.currentAudioTrigger;
            if (!outside || outside == __instance || outside.transform.parent != __instance.transform.parent ||
                outside.name != "MelMel.OutsideShip0" ||
                (__instance.name != "ShipDoorClosed" && __instance.name != "ShipDoorOpened")) return true;

            // The wider two-story interior box overlaps its narrow outside ledge.
            // Keep the outside state only while this player's collider actually touches the ledge trigger.
            var box = outside.GetComponent<BoxCollider>();
            return !box || !box.enabled || !outside.isActiveAndEnabled ||
                !Physics.ComputePenetration(box, box.transform.position, box.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _);
        }
    }
}
