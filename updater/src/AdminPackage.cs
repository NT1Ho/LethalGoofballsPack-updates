using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace LcPack {
// Thunderstore archives have no mandatory author field. The operator confirms the package ID.
public sealed class AdminPackage:IDisposable {
    const string sources="BepInEx/pack-info/download-sources.json", records="BepInEx/pack-info/original-files.json";
    readonly string temp=Path.Combine(Path.GetTempPath(),"lgp-package-"+Guid.NewGuid().ToString("N"));
    public readonly List<Tuple<string,string>> Files=new List<Tuple<string,string>>();
    public readonly List<string> Removed=new List<string>();
    public readonly HashSet<string> Modified=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string Version,Key;
    static string Text(Dictionary<string,object> row,string key){object v;return row.TryGetValue(key,out v)?Convert.ToString(v):"";}
    static List<Dictionary<string,object>> Metadata(Workspace ws,string name){
        var f=ws.Current.files.SingleOrDefault(x=>x.path==name);if(f==null)return new List<Dictionary<string,object>>();
        string path=ws.ObjectPath(f.sha256);
        if(!File.Exists(path)||f.size>8*1024*1024||Data.Hash(path)!=f.sha256)throw new IOException("请先核对渠道并取得组件登记资料。");
        return Data.Load<List<Dictionary<string,object>>>(path);
    }
    string TempFile(){return Path.Combine(temp,Guid.NewGuid().ToString("N"));}
    AdminPackage(){Directory.CreateDirectory(temp);}
    public static AdminPackage ForRemoval(Workspace ws,string[] paths){
        var plan=new AdminPackage();
        try{
            var removed=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
            var owners=Metadata(ws,records);var packages=Metadata(ws,sources);
            var present=new HashSet<string>(ws.Current.files.Select(f=>f.path),StringComparer.OrdinalIgnoreCase);
            var dropped=new HashSet<string>(owners.GroupBy(r=>Text(r,"package")).Where(g=>g.Any(r=>removed.Contains(Text(r,"path")))&&g.All(r=>!present.Contains(Text(r,"path"))||removed.Contains(Text(r,"path")))).Select(g=>g.Key));
            if(dropped.Count>0){
                owners.RemoveAll(r=>dropped.Contains(Text(r,"package")));packages.RemoveAll(r=>dropped.Contains(Text(r,"package")));
                if(!removed.Contains(records))plan.JsonFile(records,owners);
                if(!removed.Contains(sources))plan.JsonFile(sources,packages);
            }
            plan.Removed.AddRange(paths);return plan;
        }catch{plan.Dispose();throw;}
    }
    void JsonFile(string target,object data){string path=TempFile();Data.Atomic(path,Data.Json(data));Files.Add(Tuple.Create(path,target));}
    public AdminPackage(Workspace ws,string archive,string author,Action<string> progress){
        Directory.CreateDirectory(temp);
        try{
            if(!Regex.IsMatch(author??"",@"\A[A-Za-z0-9_]{1,80}\z"))throw new IOException("作者标识只能包含英文字母、数字及下划线。");
            SafePath.NoLinks(archive);var packages=Metadata(ws,sources);var owners=Metadata(ws,records);
            var owned=owners.ToDictionary(r=>Text(r,"path"),StringComparer.OrdinalIgnoreCase);
            var present=ws.Current.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
            using(var zip=ZipFile.OpenRead(archive)){
                var index=zip.GetEntry("manifest.json");if(index==null||index.Length>65536)throw new IOException("请选择根目录含 manifest.json 的单组件 ZIP。");
                Dictionary<string,object> info;using(var r=new StreamReader(index.Open()))info=Data.Read<Dictionary<string,object>>(r.ReadToEnd());
                string name=Text(info,"name");Version=Text(info,"version_number");Key=author+"-"+name;
                if(!Regex.IsMatch(name,@"\A[A-Za-z0-9_]{1,100}\z")||!Regex.IsMatch(Version,@"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][a-zA-Z0-9.-]+)?\z"))throw new IOException("组件名称或包版本无效。");
                var previous=owners.Where(r=>Text(r,"package")==Key).ToList();
                var bySource=previous.Where(r=>Text(r,"source").Length>0).ToDictionary(r=>Text(r,"source"),StringComparer.OrdinalIgnoreCase);
                owners.RemoveAll(r=>Text(r,"package")==Key);
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries=zip.Entries.Where(e=>!e.FullName.EndsWith("/")).ToList();
                if(entries.Count>10000||entries.Sum(e=>e.Length)>2L*1024*1024*1024)throw new IOException("组件 ZIP 超过文件数或解压大小限制。");
                foreach(var entry in entries){
                    string src=entry.FullName;SafePath.Validate("BepInEx/plugins/"+src);
                    string target;
                    if(bySource.ContainsKey(src))target=Text(bySource[src],"path");
                    else if(src.StartsWith("BepInEx/",StringComparison.OrdinalIgnoreCase))target=src;
                    else if(Regex.IsMatch(src,@"^(plugins|patchers|config|core)/",RegexOptions.IgnoreCase))target="BepInEx/"+src;
                    else if(Regex.IsMatch(src,@"^(manifest\.json|icon\.png|readme.*|changelog.*|licen[cs]e.*)$",RegexOptions.IgnoreCase))target="BepInEx/pack-info/"+Key+"/"+src;
                    else target="BepInEx/plugins/"+Key+"/"+src;
                    SafePath.Validate(target);
                    if(target.Equals(sources,StringComparison.OrdinalIgnoreCase)||target.Equals(records,StringComparison.OrdinalIgnoreCase)||target.Equals(PageContent.RelativePath,StringComparison.OrdinalIgnoreCase))throw new IOException("组件不能覆盖整合包公共资料。");
                    if(!paths.Add(target))throw new IOException("组件有重复目标："+target);
                    if(owned.ContainsKey(target)?Text(owned[target],"package")!=Key:present.ContainsKey(target))throw new IOException("目标属于其他组件或现有未登记文件："+target);
                    if(owned.ContainsKey(target)&&present.ContainsKey(target)&&present[target].sha256!=Text(owned[target],"sha256"))Modified.Add(target);
                    string tmp=TempFile();progress("解包 "+src);
                    using(var input=entry.Open())using(var output=File.Create(tmp)){
                        byte[] buffer=new byte[65536];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(output.Length+n>entry.Length)throw new InvalidDataException("文件超出声明大小。");output.Write(buffer,0,n);}
                        if(output.Length!=entry.Length)throw new InvalidDataException("压缩包文件不完整。");
                    }
                    Files.Add(Tuple.Create(tmp,target));owners.Add(new Dictionary<string,object>{{"path",target},{"source",src},{"package",Key},{"sha256",Data.Hash(tmp)}});
                }
                foreach(var old in previous){
                    string path=Text(old,"path");
                    if(paths.Contains(path)||!present.ContainsKey(path))continue;
                    if(present[path].sha256!=Text(old,"sha256"))throw new IOException("新包不再包含已被本地修改的文件，请先单独处理："+path);
                    Removed.Add(path);
                }
                packages.RemoveAll(r=>Text(r,"package")==Key);
                object deps;info.TryGetValue("dependencies",out deps);
                if(deps!=null&&(!(deps is System.Collections.IList)||((System.Collections.IList)deps).Cast<object>().Any(x=>!(x is string))))throw new IOException("组件依赖列表格式无效。");
                packages.Add(new Dictionary<string,object>{{"package",Key},{"version",Version},{"sha256",Data.Hash(archive)},{"dependencies",deps??new string[0]},{"source","本地导入 ZIP"}});
            }
            JsonFile(records,owners);JsonFile(sources,packages);
        }catch{Dispose();throw;}
    }
    public void Dispose(){
        if(!Directory.Exists(temp))return;
        foreach(string f in Directory.GetFiles(temp))File.Delete(f);Directory.Delete(temp);
    }
}
}
