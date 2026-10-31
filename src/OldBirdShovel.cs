using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack.Maintenance
{
    // These volumes exist only during Shovel.HitShovel's synchronous native query.
    // Keeping them disabled otherwise preserves the original grab/zap/AI colliders.
    public sealed class OldBirdShovel : MonoBehaviour
    {
        internal static readonly List<OldBirdShovel> Birds = new List<OldBirdShovel>();
        internal readonly List<Collider> Shapes = new List<Collider>();
        internal RadMechAI Bird;
        internal void Init(RadMechAI bird)
        {
            Bird = bird;
            var bones = bird.GetComponentsInChildren<Transform>(true);
            foreach (string name in new[] { "LegLeft", "LegLeft2", "LegRight", "LegRight2" })
            {
                var bone = bones.First(x => x.name == name);
                var go = HitObject(bone);
                var c = go.AddComponent<CapsuleCollider>();
                c.direction = 1; c.radius = .48f;
                c.height = name.EndsWith("2") ? 2.85f : 2.71f;
                c.center = Vector3.up * (c.height * .5f);
                c.isTrigger = true; c.enabled = false; Shapes.Add(c);
            }
            var torso = HitObject(bones.First(x => x.name == "Torso"));
            var box = torso.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 1.5f, 0); box.size = new Vector3(1.8f, 3.2f, 3.3f);
            box.isTrigger = true; box.enabled = false; Shapes.Add(box);
            Birds.Add(this);
        }
        private GameObject HitObject(Transform bone)
        {
            var go = new GameObject("PackShovelHitOnly");
            go.transform.SetParent(bone, false); go.layer = 19;
            // Native Shovel deduplicates EnemyAICollisionDetect.mainScript per swing.
            var hit = go.AddComponent<EnemyAICollisionDetect>();
            hit.mainScript = Bird; hit.canCollideWithEnemies = false;
            go.AddComponent<ShovelOnlyMarker>();
            return go;
        }
        private void OnDestroy() => Birds.Remove(this);
    }
    public sealed class ShovelOnlyMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.Start))]
    internal static class BirdStart
    {
        static void Postfix(RadMechAI __instance)
        {
            if (__instance.GetComponent<OldBirdShovel>() == null) __instance.gameObject.AddComponent<OldBirdShovel>().Init(__instance);
        }
    }
    [HarmonyPatch(typeof(EnemyAICollisionDetect), "OnTriggerStay")]
    internal static class NoExtraContactAttack
    {
        static bool Prefix(EnemyAICollisionDetect __instance) => __instance.GetComponent<ShovelOnlyMarker>() == null;
    }
    [HarmonyPatch(typeof(Shovel), nameof(Shovel.HitShovel))]
    internal static class ShovelQuery
    {
        static void Prefix(bool cancel)
        {
            if (cancel) return;
            foreach (var b in OldBirdShovel.Birds)
                if (b != null && !b.Bird.isEnemyDead)
                    foreach (var c in b.Shapes) c.enabled = true;
            Physics.SyncTransforms();
        }
        static void Finalizer()
        {
            foreach (var b in OldBirdShovel.Birds)
                if (b != null) foreach (var c in b.Shapes) c.enabled = false;
        }
    }
}
