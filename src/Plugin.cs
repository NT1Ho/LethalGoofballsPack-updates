using BepInEx;
using UnityEngine;
using HarmonyLib;

namespace LethalGoofballsPack.Maintenance
{
    [BepInPlugin("NT1Ho.LethalGoofballsPack.Maintenance", "LethalGoofballsPack scoped maintenance", "1.0.4")]
    [BepInDependency("Toskan4134.LethalRegeneration", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("TestAccount666.DoorBreach", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("NT1Ho.LethalGoofballsPack.Maintenance");
            harmony.PatchAll();
            var oldHeal = AccessTools.TypeByName("LethalRegeneration.patches.HUDManagerPatch");
            if (oldHeal != null)
            {
                harmony.Unpatch(AccessTools.Method(typeof(HUDManager), "SetClock"), AccessTools.Method(oldHeal, "healingPostfix"));
                gameObject.AddComponent<BreathingRecovery>();
            }
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("TestAccount666.DoorBreach"))
                LockedDoorRules.Install(harmony);
        }
        private void LateUpdate()
        {
            var player = GameNetworkManager.Instance?.localPlayerController;
            if (player == null || player.isPlayerDead || player.inTerminalMenu || HUDManager.Instance == null) return;
            var gun = player.currentlyHeldObjectServer as ShotgunItem;
            if (gun == null || !gun.isHeld || gun.isPocketed) return;
            SetTip(1, $"射击：[LMB]  枪内弹药：{gun.shellsLoaded} / 2");
            SetTip(3, gun.safetyOn ? "保险：已锁定（无法射击）[Q]" : "保险：已解除（允许射击）[Q]");
        }

        private static void SetTip(int index, string text)
        {
            var hud = HUDManager.Instance;
            if (hud.controlTipLines[index].text != text) hud.ChangeControlTip(index, text);
        }
    }
}
