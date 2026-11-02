using BepInEx;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack {
    [BepInPlugin(Id, "Ship Edge Reverb", "1.0.0")]
    [BepInDependency(Wide, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Deck, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ShipEdgeReverb : BaseUnityPlugin {
        const string Id = "NT1Ho.ShipEdgeReverb";
        const string Wide = "mborsh.WiderShipMod", Deck = "MelanieMelicious.2StoryShip";

        static Transform shipZones;

        void Awake() {
            if (!Chainloader.PluginInfos.ContainsKey(Wide) || !Chainloader.PluginInfos.ContainsKey(Deck)) return;
            new Harmony(Id).Patch(AccessTools.Method(typeof(AudioReverbTrigger), "OnTriggerStay"),
                prefix: new HarmonyMethod(typeof(ShipEdgeReverb), nameof(AllowPlayerStay)));
        }

        static bool AllowPlayerStay(AudioReverbTrigger __instance, Collider other) {
            if (!other.CompareTag("Player") || !other.TryGetComponent<PlayerControllerB>(out var player)) return true;
            var active = player.currentAudioTrigger;
            if (!active || active == __instance || !active.isActiveAndEnabled ||
                active.usePreset >= 0 || __instance.usePreset >= 0) return true;
            var ship = StartOfRound.Instance;
            if (!ship || !ship.elevatorTransform) return true;
            if (!shipZones || shipZones.parent != ship.elevatorTransform)
                shipZones = ship.elevatorTransform.Find("ReverbTriggers");
            if (!shipZones || !__instance.transform.IsChildOf(shipZones)) return true;
            if (!active.transform.IsChildOf(shipZones)) {
                // A level volume can overlap the hull and its disembark strip. Once off board,
                // neither a redundant exit nor a deck box that cannot board the player should
                // take ambience back from a still-occupied level zone. Native level callbacks
                // and genuine boarding transitions remain untouched.
                if (player.isInElevator || player.isInHangarShipRoom || active.isShipRoom ||
                    active.setInElevatorTrigger || !ship.shipBounds ||
                    (__instance.setInElevatorTrigger && ship.shipBounds.bounds.Contains(player.transform.position)))
                    return true;
                return !active.TryGetComponent<BoxCollider>(out var levelBox) || !levelBox.enabled ||
                    !levelBox.isTrigger || !Touches(levelBox, other);
            }

            if (!active.TryGetComponent<BoxCollider>(out var current) || !current.enabled || !current.isTrigger ||
                !__instance.TryGetComponent<BoxCollider>(out var incoming) || !incoming.isTrigger) return true;
            // Let native code handle a real exit, including disabled door volumes and teleports.
            if (!Touches(current, other)) return true;

            var center = other.bounds.center;
            int oldRank = Rank(active, current, center), newRank = Rank(__instance, incoming, center);
            if (newRank != oldRank) return newRank > oldRank;
            float oldVolume = Volume(current), newVolume = Volume(incoming);
            if (newVolume != oldVolume) return newVolume < oldVolume;
            return incoming.transform.GetSiblingIndex() < current.transform.GetSiblingIndex();
        }

        static int Rank(AudioReverbTrigger zone, BoxCollider box, Vector3 center) {
            // Exit strips take ownership when the body center crosses them. Merely brushing a
            // strip from the deck should not repeatedly detach the player from the ship.
            if (!zone.setInElevatorTrigger) return (box.ClosestPoint(center)-center).sqrMagnitude < 0.000001f ? 9 : 0;
            // Narrow exterior regions define deck space more precisely than the broad cabin volume.
            // Within either role, honor authored ambience before a geometry-only occupancy strip.
            bool audio = zone.reverbPreset || (zone.audioChanges != null && zone.audioChanges.Length != 0);
            bool environment =
                zone.setInsideAtmosphere || zone.toggleLocalFog || zone.enableCurrentLevelWeather ||
                zone.disableAllWeather || zone.weatherEffect >= 0;
            return (zone.isShipRoom ? 1 : 5) + (audio ? 2 : environment ? 1 : 0);
        }
        static bool Touches(BoxCollider box, Collider player) {
            return Physics.ComputePenetration(box, box.transform.position, box.transform.rotation,
                player, player.transform.position, player.transform.rotation, out _, out _);
        }

        static float Volume(BoxCollider box) {
            var size = Vector3.Scale(box.size, box.transform.lossyScale);
            return Mathf.Abs(size.x * size.y * size.z);
        }
    }
}