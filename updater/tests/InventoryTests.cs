using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LcPack;
class InventoryTests {
    static int count;static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    static void Main(string[] args){try{
        var watch=System.Diagnostics.Stopwatch.StartNew();var real=ModInventory.Scan(args[0]);watch.Stop();
        Data.Atomic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"inventory-real.json"),Data.Json(real));
        Check(real.Any(r=>r.name=="SnatchinBrackenFORK"&&r.version=="2.0.4"&&r.status=="本地文件校验匹配"),"fork version comes from matching local files, not stale DLL version");
        Check(real.Any(r=>r.name=="NeedyCats"&&r.version=="1.2.5"),"actual cats package detected");
        Check(real.Count(r=>r.kind=="MOD")>=20&&real.Any(r=>r.kind=="依赖组件"),"MODs and dependencies classified separately");
        var installedPackages=Data.Load<List<PackageRecord>>(Path.Combine(args[0],"BepInEx","pack-info","download-sources.json"));
        Check(installedPackages.All(p=>ModNames.Description(p.package.Substring(p.package.IndexOf('-')+1))!="暂无用途说明"),"every installed package has a purpose description");
        Check(ModNames.Description("LethalCasino").Contains("公司星球")&&!ModNames.Description("LethalCasino").Contains("飞船"),"casino description identifies the Company moon");
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"inventory-runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
        foreach(string p in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,p),"fixture");
        string plugin="BepInEx/plugins/Example/Example.dll";Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root,plugin)));File.Copy(args[1],Path.Combine(root,plugin));
        var rows=ModInventory.Scan(root);Check(rows.Any(r=>r.name=="Example Plugin"&&r.version=="9.8.7"&&r.kind=="额外插件"),"unmanaged plugin identified without executing code");
        string info=Path.Combine(root,"BepInEx","pack-info");Directory.CreateDirectory(info);
        var source=new SourceFile{path=plugin,package="Author-Example",sha256=Data.Hash(Path.Combine(root,plugin))};
        Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{source}));Data.Atomic(Path.Combine(info,"download-sources.json"),Data.Json(new[]{new PackageRecord{package="Author-Example",version="3.2.1"}}));
        rows=ModInventory.Scan(root);Check(rows.Count==1&&rows[0].version=="3.2.1","matching package groups DLL once and uses package version");
        Check(!rows[0].isNew,"legacy installs without change records have no false NEW");
        Directory.CreateDirectory(Path.Combine(root,".lcpack-updater"));
        Data.Atomic(Path.Combine(root,".lcpack-updater","state.json"),Data.Json(new LocalState{version="v2",updatedPaths=new List<string>{plugin}}));
        rows=ModInventory.Scan(root);Check(rows[0].isNew,"changed plugin maps to NEW package row");
        Data.Atomic(Path.Combine(root,".lcpack-updater","state.json"),Data.Json(new LocalState{version="v3",updatedPaths=new List<string>{"BepInEx/pack-info/original-files.json"}}));
        rows=ModInventory.Scan(root);Check(!rows[0].isNew,"global catalog changes do not mark every MOD new");
        File.WriteAllText(Path.Combine(root,plugin),"modified");rows=ModInventory.Scan(root);Check(rows[0].version.Contains("未确认")&&rows[0].status.Contains("不同"),"changed DLL does not claim recorded version");
        File.Delete(Path.Combine(root,plugin));rows=ModInventory.Scan(root);Check(rows[0].status=="未找到运行文件"&&rows[0].version.Contains("未确认"),"stale manifest never proves installed MOD");
        string asset="BepInEx/plugins/Cosmetics/example.cosmetics",assetPath=Path.Combine(root,asset);Directory.CreateDirectory(Path.GetDirectoryName(assetPath));File.WriteAllText(assetPath,"cosmetic resource");
        var resource=new SourceFile{path=asset,package="Author-Cosmetics",sha256=Data.Hash(assetPath)};
        Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{resource}));Data.Atomic(Path.Combine(info,"download-sources.json"),Data.Json(new[]{new PackageRecord{package=resource.package,version="2.0.0"}}));
        using(var locked=new FileStream(assetPath,FileMode.Open,FileAccess.Read,FileShare.None)){
            var fast=ModInventory.Scan(root,false).Single(r=>r.name=="Cosmetics");Check(fast.version=="2.0.0"&&fast.status.Contains("来自安装记录"),"resource-only package shows recorded version without reading large assets");
        }
        File.WriteAllText(assetPath,"changed resource");Check(ModInventory.Scan(root,true).Single(r=>r.name=="Cosmetics").version.Contains("未确认"),"full resource verification still detects changed content");
        File.Delete(assetPath);Check(ModInventory.Scan(root,false).Single(r=>r.name=="Cosmetics").version.Contains("未确认"),"missing resource does not claim installation");
        Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{source}));Data.Atomic(Path.Combine(info,"download-sources.json"),Data.Json(new[]{new PackageRecord{package="Author-Example",version="3.2.1"}}));
        Directory.CreateDirectory(Path.Combine(root,"BepInEx","plugins","Other"));File.WriteAllText(Path.Combine(root,"BepInEx","plugins","Other","bad.dll"),"bad");
        rows=ModInventory.Scan(root);Check(rows.Any(r=>r.kind=="未识别 DLL"),"malformed DLL isolated without losing other results");
        source.path="BepInEx/plugins/../../outside.dll";Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{source}));rows=ModInventory.Scan(root);Check(rows.Any(r=>r.status.StartsWith("无法完整检查")),"record path traversal rejected");
        var ws=new Workspace(Path.Combine(root,"publisher"));File.Copy(args[1],Path.Combine(root,plugin));source.path=plugin;source.sha256=Data.Hash(Path.Combine(root,plugin));Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{source}));
        ws.Add(Path.Combine(root,plugin),plugin);ws.Add(Path.Combine(info,"original-files.json"),"BepInEx/pack-info/original-files.json");ws.Add(Path.Combine(info,"download-sources.json"),"BepInEx/pack-info/download-sources.json");
        var groups=AdminCatalog.Build(ws);var mod=groups.Single(g=>g.name=="Example");Check(mod.paths.SequenceEqual(new[]{plugin})&&mod.version=="3.2.1"&&mod.status=="待发布","admin group retains exact pending file mapping");
        File.WriteAllText(Path.Combine(root,plugin),"changed");ws.Add(Path.Combine(root,plugin),plugin);Check(AdminCatalog.Build(ws).Single(g=>g.name=="Example").version.StartsWith("已更改"),"admin replacement invalidates stale package version");
        ws.Remove(new[]{plugin});Check(AdminCatalog.Build(ws).Single(g=>g.name=="Example").status=="待删除","admin group deletion remains explicit");
        Console.WriteLine("RESULT: "+count+" inventory checks passed; actual scan "+real.Count+" rows in "+watch.Elapsed.TotalSeconds.ToString("0.00")+" s; mutations only in fixtures.");
    }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
