using System;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

internal static class MaskedAnimation
{
    const string RigPath = "ScavengerModel/metarig";
    sealed class Binding { public RuntimeAnimatorController controller; }
    static readonly ConditionalWeakTable<MaskedPlayerEnemy, Binding> bindings = new ConditionalWeakTable<MaskedPlayerEnemy, Binding>();
    static ManualLogSource log;

    internal static void Install(ManualLogSource logger)
    {
        log = logger;
        var patch = new Harmony("NT1Ho.CrewCompatibility.MaskedAnimation");
        var emote = AccessTools.TypeByName("TooManyEmotes.EmoteControllerMaskedEnemy");
        if (emote != null)
        {
            patch.Patch(AccessTools.PropertyGetter(emote, "isMoving"), prefix: Hook(nameof(ReadMovement)));
            patch.Patch(AccessTools.DeclaredMethod(emote, "CheckIfShouldStopEmoting"), postfix: Hook(nameof(StopWhenDisplaced)));
            patch.Patch(AccessTools.DeclaredMethod(emote, "PerformEmote"), prefix: Hook(nameof(RememberRig)));
        }
        patch.Patch(AccessTools.DeclaredMethod(typeof(MaskedPlayerEnemy), "HitEnemy"), prefix: Hook(nameof(BeforeHit)));
        patch.Patch(AccessTools.DeclaredMethod(typeof(MaskedPlayerEnemy), "KillEnemy"), prefix: Hook(nameof(BeforeDeath)));
    }
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(MaskedAnimation), name);
    static readonly int Moving = Animator.StringToHash("IsMoving");

    // V81 writes IsMoving on the creature rig. The auxiliary humanoid plays emotes only.
    static bool ReadMovement(MaskedPlayerEnemy ___maskedEnemy, ref bool __result)
    {
        var rig = ___maskedEnemy ? ___maskedEnemy.creatureAnimator : null;
        if (!rig || !rig.runtimeAnimatorController) return true;
        __result = rig.GetBool(Moving);
        return false;
    }
    static void StopWhenDisplaced(MaskedPlayerEnemy ___maskedEnemy, Vector3 ___emotedAtPosition, ref bool __result)
    {
        if (!__result && ___maskedEnemy)
            __result = (___maskedEnemy.transform.position - ___emotedAtPosition).sqrMagnitude > 0.01f;
    }
    static void RememberRig(MaskedPlayerEnemy ___maskedEnemy) => CacheController(___maskedEnemy);
    static void CacheController(MaskedPlayerEnemy __instance)
    {
        if (!__instance) return;
        var a = __instance.creatureAnimator;
        if (a && a.runtimeAnimatorController)
            bindings.GetOrCreateValue(__instance).controller = a.runtimeAnimatorController;
    }
    static void BeforeHit(MaskedPlayerEnemy __instance)
    {
        // HitEnemy sets animator triggers before it checks for lethal damage.
        if (!__instance.creatureAnimator) RestoreRig(__instance);
        CacheController(__instance);
    }
    static void BeforeDeath(MaskedPlayerEnemy __instance, bool destroy)
    {
        if (!destroy && (!__instance.creatureAnimator || !__instance.creatureAnimator.runtimeAnimatorController))
            RestoreRig(__instance);
    }
    static void RestoreRig(MaskedPlayerEnemy __instance)
    {
        var rig = __instance.transform.Find(RigPath)?.GetComponent<Animator>();
        if (!rig || (__instance.creatureAnimator && __instance.creatureAnimator != rig)) return;
        var controller = rig.runtimeAnimatorController;
        if (!controller && bindings.TryGetValue(__instance, out var known)) controller = known.controller;
        if (!controller && __instance.enemyType && __instance.enemyType.enemyPrefab)
            controller = __instance.enemyType.enemyPrefab.transform.Find(RigPath)?.GetComponent<Animator>()?.runtimeAnimatorController;
        if (!controller || !Array.Exists(controller.animationClips, clip => clip && clip.name == "MaskedPlayerDie")) return;
        log.LogWarning("Restoring Masked animation rig for network object " + __instance.NetworkObjectId +
            " (animator=" + (bool)__instance.creatureAnimator + ", controller=" + (bool)rig.runtimeAnimatorController + ")");
        __instance.creatureAnimator = rig;
        if (!rig.runtimeAnimatorController) rig.runtimeAnimatorController = controller;
    }
}
