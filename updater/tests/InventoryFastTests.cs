using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LcPack;
class InventoryFastTests {
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static void Main(string[] args) {
        for(int i=0;i<3;i++){
            var sw=Stopwatch.StartNew();var full=ModInventory.Scan(args[0]);long slow=sw.ElapsedMilliseconds;
            sw.Restart();var fast=ModInventory.Scan(args[0],false);long quick=sw.ElapsedMilliseconds;
            Check(full.Select(r=>r.name+":"+r.version+":"+r.isNew).SequenceEqual(fast.Select(r=>r.name+":"+r.version+":"+r.isNew)),"full and quick lists agree for actual package");
            Console.WriteLine("BENCH full="+slow+" ms, quick="+quick+" ms, rows="+fast.Count);
        }
        string root=Path.Combine(Path.GetTempPath(),"lcpack-inventory-fast-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
        foreach(string p in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,p),"fixture");
        string folder=Path.Combine(root,"BepInEx/plugins/Test");Directory.CreateDirectory(folder);
        string dll="BepInEx/plugins/Test/a.dll",asset="BepInEx/plugins/Test/audio";
        File.Copy(args[1],Path.Combine(root,dll));File.WriteAllText(Path.Combine(root,asset),"asset");
        string info=Path.Combine(root,"BepInEx/pack-info");Directory.CreateDirectory(info);
        Data.Atomic(Path.Combine(info,"download-sources.json"),Data.Json(new[]{new PackageRecord{package="Author-Test",version="1.0.0"}}));
        Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(new[]{dll,asset}.Select(p=>new SourceFile{package="Author-Test",path=p,sha256=Data.Hash(Path.Combine(root,p))}).ToArray()));
        using(var locked=new FileStream(Path.Combine(root,asset),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
            Check(ModInventory.Scan(root,false).Single().version=="1.0.0","quick list does not read asset contents");
            Check(ModInventory.Scan(root).Single().version.Contains("未确认"),"full resource verification remains available");
        }
        File.WriteAllText(Path.Combine(root,dll),"changed");
        Check(ModInventory.Scan(root,false).Single().version.Contains("未确认"),"quick list detects changed DLL without a stale cache");
        File.Copy(args[1],Path.Combine(root,dll),true);File.Delete(Path.Combine(root,asset));
        Check(ModInventory.Scan(root,false).Single().status.Contains("缺少"),"quick list detects missing resources");
        File.Delete(Path.Combine(root,dll));Check(ModInventory.Scan(root,false).Single().status=="未找到运行文件","quick list detects removed plugin");
    }
}
