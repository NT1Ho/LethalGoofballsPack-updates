using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace LcPack {
public class ModGroup {public string name,version,status,key;public long size;public string[] paths;}
public static class AdminCatalog {
    static T Read<T>(Workspace ws,string path,T fallback) {
        var file=ws.Current.files.FirstOrDefault(f=>f.path==path);if(file==null)return fallback;
        string source=ws.ObjectPath(file.sha256);if(!File.Exists(source))return fallback;
        try {if(new FileInfo(source).Length>8*1024*1024)return fallback;return Data.Load<T>(source);}catch{return fallback;}
    }
    public static List<ModGroup> Build(Workspace ws) {
        var records=Read(ws,"BepInEx/pack-info/original-files.json",new List<SourceFile>())??new List<SourceFile>();
        var packages=Read(ws,"BepInEx/pack-info/download-sources.json",new List<PackageRecord>())??new List<PackageRecord>();
        var owners=records.Where(f=>f!=null&&f.path!=null&&f.package!=null).GroupBy(f=>f.path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().package,StringComparer.OrdinalIgnoreCase);
        var known=ws.Current.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
        Func<string,string> key=path=>{
            string owner;if(owners.TryGetValue(path,out owner))return owner;
            string[] parts=path.Split('/');
            if(parts.Length>=3&&(parts[1]=="plugins"||parts[1]=="patchers"))return "未归档-"+(parts.Length>3?parts[2]:Path.GetFileNameWithoutExtension(path));
            if(path.StartsWith("BepInEx/config/"))return "其他配置";
            return "整合包记录与公共文件";
        };
        var result=new List<ModGroup>();
        foreach(var group in ws.Current.files.Select(f=>f.path).Concat(ws.Current.deletes).Distinct(StringComparer.OrdinalIgnoreCase).GroupBy(key)) {
            var paths=group.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();var present=paths.Where(known.ContainsKey).Select(p=>known[p]).ToList();
            var package=packages.FirstOrDefault(p=>p!=null&&p.package==group.Key);
            string version=package==null?(group.Key=="其他配置"||group.Key=="整合包记录与公共文件"?"不适用":"未识别"):package.version;
            var runtime=records.Where(f=>f!=null&&f.package==group.Key&&f.path!=null&&(f.path.StartsWith("BepInEx/plugins/")||f.path.StartsWith("BepInEx/patchers/")||f.path.StartsWith("BepInEx/core/"))).ToList();
            if(package!=null&&runtime.Any(f=>!known.ContainsKey(f.path)||known[f.path].sha256!=f.sha256))version="已更改（原 "+version+"）";
            string name=group.Key.Contains('-')?group.Key.Substring(group.Key.IndexOf('-')+1):group.Key;
            result.Add(new ModGroup{key=group.Key,name=name,version=version,paths=paths,size=present.Sum(f=>f.size),status=present.Count==0?"待删除":paths.Length>present.Count?"含删除项":present.Any(f=>String.IsNullOrEmpty(f.url))?"待发布":"已发布"});
        }
        return result.OrderBy(g=>g.name,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
}
