using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WesleyImageReader
{
    [HarmonyPatch]
    internal static class InspectTips
    {
        private const string CanonicalTip = "Inspect: [Z]";

        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod() =>
            AccessTools.Method("ButteryFixes.Utility.ScriptableObjectOverrides:OverrideItems");

        private static void Prefix()
        {
            if (!StartOfRound.Instance) return;
            foreach (var item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (!item || item.toolTips == null) continue;
                var tips = Normalize(item.toolTips);
                if (tips != item.toolTips) item.toolTips = tips;
            }
        }

        // ButteryFixes uses the English label to decide whether to append an inspect tip.
        internal static string[] Normalize(string[] source)
        {
            var first = Array.FindIndex(source, IsInspectTip);
            if (first < 0) return source;
            var duplicate = Array.FindIndex(source, first + 1, IsInspectTip) >= 0;
            if (!duplicate && source[first] == CanonicalTip) return source;

            var tips = new List<string>(source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                if (i == first) tips.Add(CanonicalTip);
                else if (!IsInspectTip(source[i])) tips.Add(source[i]);
            }
            return tips.ToArray();
        }

        private static bool IsInspectTip(string tip)
        {
            if (tip == null) return false;
            var colon = tip.IndexOfAny(new[] { ':', '：' });
            if (colon == -1 || !tip.Substring(colon + 1).Trim().Equals("[Z]", StringComparison.OrdinalIgnoreCase))
                return false;
            var label = tip.Substring(0, colon).Trim();
            return label.Equals("Inspect", StringComparison.OrdinalIgnoreCase) || label == "查看" || label == "检查";
        }
    }

    [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.Start))]
    internal static class FilePickupTip
    {
        private static void Postfix(GrabbableObject __instance)
        {
            if (!string.IsNullOrEmpty(__instance.customGrabTooltip)) return;
            var reading = Targets.InspectCaption(__instance.itemProperties);
            if (reading != null) __instance.customGrabTooltip = "拾取" + reading.title + "：[E]";
        }
    }

    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.ChangeControlTipMultiple))]
    [HarmonyAfter("Aueser.LCChineseProject")]
    internal static class FileNameControlTip
    {
        private static void Postfix(HUDManager __instance, bool holdingItem, Item itemProperties)
        {
            if (!holdingItem) return;
            var reading = Targets.InspectCaption(itemProperties);
            if (reading == null) return;
            var tip = __instance.controlTipLines[0];
            if (tip && !string.IsNullOrEmpty(itemProperties.itemName))
                tip.text = tip.text.Replace(itemProperties.itemName, reading.title);
        }
    }
}
