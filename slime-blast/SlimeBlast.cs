using System;
using System.Runtime.CompilerServices;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack;

[BepInPlugin(Id, "Hygrodere Explosion Response", "1.0.0")]
[BepInDependency("lcpack.IndoorHazardGuard", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class SlimeBlast : BaseUnityPlugin
{
    private const string Id = "NT1Ho.LethalGoofballsPack.SlimeBlast";
    private void Awake() => new Harmony(Id).PatchAll(typeof(SlimeBlast).Assembly);
}

[HarmonyPatch]
internal static class SlimeExplosion
{
    private sealed class Blast
    {
        internal readonly Vector3 Origin;
        internal readonly float Range;
        internal readonly int Occluders;
        private Collider[]? hitboxes;

        internal Blast(Vector3 origin, float range, bool goThroughCar)
        {
            Origin = origin;
            Range = range;
            Occluders = goThroughCar ? 1 << 8 : (1 << 8) | (1 << 30);
        }

        internal bool Reaches(BlobAI blob)
        {
            if (Range <= 0) return false;
            hitboxes ??= Physics.OverlapSphere(Origin, Range, 1 << 19, QueryTriggerInteraction.Collide);
            foreach (var collider in hitboxes)
            {
                if (!collider || collider.GetComponentInChildren<EnemyAICollisionDetect>()?.mainScript != blob) continue;
                // The native close blast may cross a room wall. The one-hit rule still requires a clear hit.
                if (!Physics.Linecast(Origin, collider.transform.position + Vector3.up * .3f,
                                      Occluders, QueryTriggerInteraction.Ignore))
                    return true;
            }
            return false;
        }
    }

    [ThreadStatic] private static Blast? activeBlast;
    private static readonly ConditionalWeakTable<BlobAI, object> dying = new();

    [HarmonyPrefix, HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
    private static void Enter(Vector3 explosionPosition, float damageRange, bool goThroughCar, out Blast? __state)
    {
        __state = activeBlast;
        activeBlast = new Blast(explosionPosition, damageRange, goThroughCar);
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
    private static Exception? Leave(Exception? __exception, Blast? __state)
    {
        activeBlast = __state;
        return __exception;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyAfter("lcpack.IndoorHazardGuard", "nwnt.EverythingCanDieAlternative")]
    [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.HitEnemyOnLocalClient))]
    private static bool Hit(EnemyAI __instance, int force, PlayerControllerB? playerWhoHit)
    {
        if (activeBlast == null || __instance is not BlobAI blob || playerWhoHit != null || force <= 0) return true;
        if (!blob.IsOwner || !blob.IsSpawned || blob.isEnemyDead || dying.TryGetValue(blob, out _) ||
            !activeBlast.Reaches(blob)) return false;

        // Use the owner/server's vanilla destroy-on-death path; additional colliders and FairAI see the same pending death.
        dying.Add(blob, new object());
        blob.KillEnemyOnOwnerClient(overrideDestroy: true);
        return false;
    }
}
