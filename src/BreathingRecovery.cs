using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalGoofballsPack.Maintenance
{
    internal sealed class BreathingRecovery : MonoBehaviour
    {
        double progress, lastTime;
        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = lastTime == 0 ? 0 : now - lastTime;
            lastTime = now;
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (!p || !p.IsOwner || !p.isPlayerControlled || p.isPlayerDead || p.health <= 0 || p.health >= 100 || Time.timeScale == 0f)
            {
                progress = 0f;
                return;
            }
            // Ten-point pulses. Changing areas preserves only the fraction already earned.
            progress += elapsed / (p.isInHangarShipRoom ? 1f : 3f);
            if (progress < 1f) return;
            int pulses = (int)progress;
            progress -= pulses;
            int amount = Mathf.Min(100 - p.health, Mathf.Min(pulses, 10) * 10);
            p.health += amount;
            HUDManager.Instance.UpdateHealthUI(p.health, false);
            if (p.criticallyInjured && p.health >= 10) p.MakeCriticallyInjured(false);
            if (p.IsServer) p.DamagePlayerClientRpc(-amount, p.health);
            else p.DamagePlayerServerRpc(-amount, p.health);
        }


    }

    [HarmonyPatch(typeof(PlayerControllerB), "DamageOnOtherClients")]
    internal static class RecoverySync
    {
        static bool Prefix(PlayerControllerB __instance, int damageNumber, int newHealthAmount)
        {
            if (damageNumber >= 0) return true;
            if (!__instance.IsOwner && !__instance.isPlayerDead) __instance.health = Mathf.Clamp(newHealthAmount, 1, 100);
            return false;
        }
    }
}
