using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;

using System.Linq;
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
        patch.Patch(AccessTools.PropertySetter(typeof(Animator), "runtimeAnimatorController"), prefix: Hook(nameof(GuardMirageController)));
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
    // Mirage registers its hook after PreInit and can retain a destroyed Unity asset.
    // Only intercept a destructive write by that hook to the live Masked rig.
    static bool GuardMirageController(Animator __instance, ref RuntimeAnimatorController __0)
    {
        if (__0 || !__instance || __instance.name != "metarig") return true;
        var m = __instance.GetComponentInParent<MaskedPlayerEnemy>();
        if (!m || m.creatureAnimator != __instance) return true;
        var frames = new StackTrace(false).GetFrames();
        if (frames == null || !frames.Any(f => (f.GetMethod()?.DeclaringType?.FullName ?? "").StartsWith("Mirage.Hook.MaskedPlayerEnemy+", StringComparison.Ordinal))) return true;
        var bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name.IndexOf("mirage", StringComparison.OrdinalIgnoreCase) >= 0 && b.Contains("metarig_0.controller"));
        if (bundle) __0 = bundle.LoadAsset<RuntimeAnimatorController>("metarig_0.controller");
        log.LogWarning("Prevented Mirage clearing Masked controller at spawn; reloaded=" + (bool)__0 + " id=" + m.NetworkObjectId);
        return __0;
    }    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(MaskedAnimation), name);
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
