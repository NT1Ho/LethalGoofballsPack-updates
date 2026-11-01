using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace LcPack {
public static class AdminInventory {
    static readonly Dictionary<string,Tuple<string,string>> plugins=new Dictionary<string,Tuple<string,string>>();
    static AdminInventory(){
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            if(new AssemblyName(e.Name).Name!="Mono.Cecil")return null;
            using(var stream=typeof(AdminInventory).Assembly.GetManifestResourceStream("Mono.Cecil.dll")){
                if(stream==null)return null;
                using(var memory=new MemoryStream()){stream.CopyTo(memory);return Assembly.Load(memory.ToArray());}
            }
        };
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Tuple<string,string> ReadPlugin(string path){
        using(var assembly=AssemblyDefinition.ReadAssembly(path)){
            var ids=assembly.MainModule.GetTypes().SelectMany(t=>t.CustomAttributes)
                .Where(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin"&&a.ConstructorArguments.Count==3).ToList();
            if(ids.Count!=1)return null;
            return Tuple.Create(Convert.ToString(ids[0].ConstructorArguments[1].Value),Convert.ToString(ids[0].ConstructorArguments[2].Value));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string[] PluginIds(string path){
        try{using(var a=AssemblyDefinition.ReadAssembly(path))return a.MainModule.GetTypes().SelectMany(t=>t.CustomAttributes)
            .Where(c=>c.AttributeType.FullName=="BepInEx.BepInPlugin"&&c.ConstructorArguments.Count==3)
            .Select(c=>Convert.ToString(c.ConstructorArguments[0].Value)).Distinct().ToArray();}
        catch(BadImageFormatException){return new string[0];} // Native DLLs have no BepInPlugin metadata.
    }
    public static List<ModGroup> Groups(Workspace ws){
        var groups=AdminCatalog.Build(ws);
        var files=ws.Current.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
        foreach(var group in groups){
            if(group.version!="未识别"&&!group.version.StartsWith("已更改"))continue;
            var found=new List<Tuple<string,string>>();
            foreach(string p in group.paths.Where(p=>p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))){
                PackFile f;if(!files.TryGetValue(p,out f)||f.size>64*1024*1024)continue;
                string path=ws.ObjectPath(f.sha256);
                if(!File.Exists(path))continue;
                Tuple<string,string> identity;
                if(!plugins.TryGetValue(f.sha256,out identity)){
                    try{SafePath.NoLinks(path);identity=Data.Hash(path)==f.sha256?ReadPlugin(path):null;}
                    catch(BadImageFormatException){identity=null;}
                    catch(IOException){identity=null;}
                    catch(ArgumentException){identity=null;}
                    plugins[f.sha256]=identity;
                }
                if(identity!=null)found.Add(identity);
            }
            if(found.Count==1){group.version=found[0].Item2;if(group.key.StartsWith("未归档-"))group.name=found[0].Item1;}
        }
        return groups;
    }
}
}
