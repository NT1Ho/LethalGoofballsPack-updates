using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace LethalGoofballsPack.Wesley
{
    [BepInPlugin("NT1Ho.WesleyPackCompatibility", "Wesley Pack Compatibility", "1.0.3")]
    [BepInDependency("MaxWasUnavailable.LethalModDataLib", "1.2.2")]
    [BepInDependency("Aueser.LCChineseProject", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("JacobG5.WesleyMoonScripts")]
    [BepInDependency("MagicWesley.WesleysInteriorTools")]

    public sealed class ModDataRegistration : BaseUnityPlugin
    {
        private static ManualLogSource log;
        private void Awake()
        {
            log = Logger;
            var plugin = Chainloader.PluginInfos["MaxWasUnavailable.LethalModDataLib"];
            var collector = plugin.Instance.GetType().Assembly.GetType("LethalModDataLib.Features.ModDataAttributeCollector", true);
            var scan = AccessTools.Method(collector, "RegisterModDataAttributes", Type.EmptyTypes);
            var harmony = new Harmony(Info.Metadata.GUID);
            harmony.Patch(scan,
                transpiler: new HarmonyMethod(typeof(ModDataRegistration), nameof(FilterRegistryForScan)));
            HauntingPlayerSlots.Install(harmony);
            ResultsSprite.Install(harmony);
            LaboratoryNavigation.Install(harmony);

            ShopPrompts.Install(harmony);
            gameObject.AddComponent<JoinedRoutes>().Initialize(Logger);
        }

        private static IEnumerable<CodeInstruction> FilterRegistryForScan(IEnumerable<CodeInstruction> instructions)
        {
            var registry = AccessTools.PropertyGetter(typeof(Chainloader), nameof(Chainloader.PluginInfos));
            var filtered = AccessTools.Method(typeof(ModDataRegistration), nameof(InstantiatedPlugins));
            bool matched = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(registry))
                {
                    instruction.operand = filtered;
                    matched = true;
                }
                yield return instruction;
            }
            if (!matched) throw new MissingMethodException("Unrecognized LethalModDataLib registration code");
        }

        private static Dictionary<string, PluginInfo> InstantiatedPlugins()
        {
            var result = new Dictionary<string, PluginInfo>();
            foreach (var entry in Chainloader.PluginInfos)
            {
                if (entry.Value.Instance != null) result.Add(entry.Key, entry.Value);
                else log.LogInfo($"Skipping metadata-only plugin during ModData registration: {entry.Key}");
            }
            return result;
        }
    }
}



