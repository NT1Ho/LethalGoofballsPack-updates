using BepInEx.Bootstrap;
using HarmonyLib;
using WesleyMoonScripts.Components;

namespace LethalGoofballsPack.Wesley
{
    internal static class ShopPrompts
    {
        private delegate bool Lookup(string original, out string translated);
        private static Lookup translate;

        internal static void Install(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue("Aueser.LCChineseProject", out var plugin)) return;
            var service = plugin.Instance.GetType().Assembly.GetType("V81TestChn.TranslationService", true);
            translate = (Lookup)AccessTools.Method(service, "TryTranslateFastExact").CreateDelegate(typeof(Lookup));
            harmony.Patch(AccessTools.Method(typeof(ItemShop), nameof(ItemShop.OnNetworkSpawn)),
                prefix: new HarmonyMethod(typeof(ShopPrompts), nameof(TranslateStatus)));
            harmony.Patch(AccessTools.Method(typeof(ItemShop), "SyncBuyableClientRpc"),
                postfix: new HarmonyMethod(typeof(ShopPrompts), nameof(TranslateOffer)));
        }

        private static void TranslateStatus(ItemShop __instance)
        {
            if (__instance.purchasedString == "Purchased!") __instance.purchasedString = "已购买";
            if (__instance.emptyString == "Empty") __instance.emptyString = "空货位";
        }

        private static void TranslateOffer(ItemShop __instance, int index)
        {
            if ((uint)index >= __instance.spawnPositions.Length) return;
            var slot = __instance.spawnPositions[index];
            if (!slot.item || !slot.buyInteract.interactable) return;
            var name = slot.item.itemProperties.itemName;
            if (translate(name, out var localized)) name = localized;
            slot.buyInteract.hoverTip = $"购买{name}（{slot.price} 积分）: [E]";
        }
    }
}

