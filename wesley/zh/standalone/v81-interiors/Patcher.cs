using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

public static class WesleyV81Interiors
{
    private static readonly HashSet<string> MovedTypes = new HashSet<string>
    {
        "DunGen.TileConnectionRule", "DunGen.Tags.Tag", "DunGen.Tile",
        "DunGen.Doorway", "DunGen.DoorwayPairFinder", "DunGen.Tags.TagContainer"
    };

    public static IEnumerable<string> TargetDLLs { get { yield return "Assembly-CSharp.dll"; } }

    public static void Patch(AssemblyDefinition game)
    {
        var module = game.MainModule;
        var dunGen = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == "DunGen");
        if (dunGen == null || !Directory.Exists(Paths.PluginPath)) return;

        var helper = Directory.EnumerateFiles(Paths.PluginPath, "WesleysInteriorShenanigans.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (helper == null) return;
        using (var interiors = AssemblyDefinition.ReadAssembly(helper))
        using (var dungeon = AssemblyDefinition.ReadAssembly(Path.Combine(Paths.ManagedPath, "DunGen.dll")))
        {
            int count = 0;
            foreach (var reference in interiors.MainModule.GetTypeReferences())
            {
                if (reference.Scope.Name != game.Name.Name || !MovedTypes.Contains(reference.FullName) ||
                    module.GetType(reference.FullName) != null || module.ExportedTypes.Any(type => type.FullName == reference.FullName)) continue;

                var definition = dungeon.MainModule.GetType(reference.FullName);
                if (definition == null || !definition.IsPublic) continue;

                // CLR resolves nested types through their forwarded declaring type.
                module.ExportedTypes.Add(new ExportedType(definition.Namespace, definition.Name, module, dunGen)
                {
                    Attributes = TypeAttributes.Public,
                    IsForwarder = true
                });
                count++;
            }
            if (count != 0) Logger.CreateLogSource("Wesley V81 Interiors").LogInfo($"Forwarded {count} legacy DunGen type references in memory.");
        }
    }
}
