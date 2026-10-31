using BepInEx;
using UnityEngine;
using HarmonyLib;

namespace LethalGoofballsPack.Maintenance
{
    [BepInPlugin("NT1Ho.LethalGoofballsPack.Maintenance", "LethalGoofballsPack scoped maintenance", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake() => new Harmony("NT1Ho.LethalGoofballsPack.Maintenance").PatchAll();
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
