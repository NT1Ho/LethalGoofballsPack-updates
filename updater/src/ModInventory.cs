using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace LcPack {
public class InstalledMod {public string name,version,kind,status,path;public bool isNew;}
public class SourceFile {public string path,package,sha256;}
public class PackageRecord {public string package,version;public string[] dependencies;}
public static class ModInventory {
    static readonly object gate=new object();static Assembly parser;
    public static List<InstalledMod> Scan(string root) {return Scan(root,true);}
    public static List<InstalledMod> Scan(string root,bool verifyResources) {
        lock(gate)if(parser==null){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll")){byte[] bytes=new byte[stream.Length];int offset=0,n;while((n=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=n;parser=Assembly.Load(bytes);}AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>e.Name==parser.FullName?parser:null;}
        return Read(root,verifyResources);
    }
    static bool Runtime(string path){return path!=null&&(path.StartsWith("BepInEx/plugins/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("BepInEx/patchers/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("BepInEx/core/",StringComparison.OrdinalIgnoreCase)||path=="winhttp.dll");}
    static IEnumerable<string> Files(string root,List<InstalledMod> rows) {
        if(!Directory.Exists(root))return new string[0];
        try {SafePath.NoLinks(root);var result=new List<string>(Directory.GetFiles(root,"*.dll"));foreach(string dir in Directory.GetDirectories(root))result.AddRange(Files(dir,rows));return result;}
        catch(Exception e){rows.Add(new InstalledMod{name=Path.GetFileName(root),kind="目录",status="无法扫描："+e.Message,path=root,version="未知"});return new string[0];}
    }
    static T Records<T>(string path,T empty,List<InstalledMod> rows) {
        if(!File.Exists(path))return empty;
        try {SafePath.NoLinks(path);if(new FileInfo(path).Length>8*1024*1024)throw new IOException("记录过大");return Data.Load<T>(path);}
        catch(Exception e){rows.Add(new InstalledMod{name=Path.GetFileName(path),kind="安装记录",status="无法读取："+e.Message,path=path,version="未知"});return empty;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static List<InstalledMod> Read(string root,bool verifyResources) {
        Engine.CheckGameDirectory(root);root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var rows=new List<InstalledMod>();var owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updated=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {string statePath=Path.Combine(root,".lcpack-updater","state.json");if(File.Exists(statePath)){SafePath.NoLinks(statePath);if(new FileInfo(statePath).Length<=8*1024*1024){var state=Data.Load<LocalState>(statePath);if(state!=null&&state.updatedPaths!=null)updated.UnionWith(state.updatedPaths);}}}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        string info=Path.Combine(root,"BepInEx","pack-info");
        var files=Records(Path.Combine(info,"original-files.json"),new List<SourceFile>(),rows)??new List<SourceFile>();
        var packages=Records(Path.Combine(info,"download-sources.json"),new List<PackageRecord>(),rows)??new List<PackageRecord>();
        var dependencies=new HashSet<string>(packages.Where(p=>p!=null).SelectMany(p=>p.dependencies??new string[0]).Select(d=>d.Substring(0,Math.Max(0,d.LastIndexOf('-')))),StringComparer.OrdinalIgnoreCase);
        foreach(var package in packages.Where(p=>p!=null&&!String.IsNullOrEmpty(p.package)).GroupBy(p=>p.package).Select(g=>g.First())) {
            var entries=files.Where(f=>f!=null&&f.package==package.package&&Runtime(f.path)).ToList();if(entries.Count==0)continue;
            int existing=0,missing=0,changed=0,verified=0;string warning=null;
            foreach(var f in entries) {
                try {
                    string path=SafePath.Target(root,f.path);owned.Add(f.path);
                    if(!File.Exists(path)){missing++;continue;}existing++;
                    // The list verifies plugin identity, not large audio/model resources.
                    // Installation still performs full SHA-256 verification in Engine.
                    if(verifyResources||f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){verified++;if(Data.Hash(path)!=f.sha256)changed++;}
                }catch(Exception e){warning=e.Message;}
            }
            string name=package.package.Substring(package.package.IndexOf('-')+1);
            bool resourceOnly=!entries.Any(f=>f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase));
            string status=existing==0?"未找到运行文件":missing>0?"缺少 "+missing+" 个文件":changed>0?"有 "+changed+" 个文件与记录不同":verifyResources?"本地文件校验匹配":resourceOnly?"资源包版本来自安装记录；资源仅检查存在":"DLL 已校验；资源仅检查存在";
            if(warning!=null)status="无法完整检查："+warning;
            rows.Add(new InstalledMod{name=name,version=(verified>0||resourceOnly)&&existing>0&&missing==0&&changed==0&&warning==null?package.version:"未确认（记录 "+package.version+"）",kind=package.package=="BepInEx-BepInExPack"?"加载框架":dependencies.Contains(package.package)&&!ModNames.IsMainMod(name)?"依赖组件":"MOD",status=status,path=String.Join("; ",entries.Where(f=>f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Select(f=>f.path).DefaultIfEmpty(entries[0].path))});
        }
        var dlls=Files(Path.Combine(root,"BepInEx","plugins"),rows).Concat(Files(Path.Combine(root,"BepInEx","patchers"),rows)).ToList();
        string loader=Path.Combine(root,"BepInEx","core","BepInEx.dll");if(File.Exists(loader))dlls.Add(loader);
        foreach(string file in dlls) {
            string relative=file.Substring(root.Length+1).Replace('\\','/');if(owned.Contains(relative))continue;
            try {
                SafePath.NoLinks(file);if(new FileInfo(file).Length>64*1024*1024)throw new IOException("DLL 超过扫描大小限制");
                using(var assembly=AssemblyDefinition.ReadAssembly(file)) {
                    var plugins=assembly.MainModule.GetTypes().SelectMany(t=>t.CustomAttributes).Where(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin").ToList();
                    foreach(var plugin in plugins)rows.Add(new InstalledMod{name=Convert.ToString(plugin.ConstructorArguments[1].Value),version=Convert.ToString(plugin.ConstructorArguments[2].Value),kind="额外插件",status="读取 DLL 元数据；未确认加载",path=relative});
                    if(plugins.Count==0)rows.Add(new InstalledMod{name=assembly.Name.Name,version=assembly.Name.Version.ToString(),kind=relative.StartsWith("BepInEx/patchers/")?"补丁/支持库":"支持库",status="程序集版本；未确认加载",path=relative});
                }
            }catch(Exception e){rows.Add(new InstalledMod{name=Path.GetFileName(file),version="未知",kind="未识别 DLL",status="无法读取："+e.GetType().Name,path=relative});}
        }
        foreach(var row in rows) {
            var package=packages.FirstOrDefault(p=>p!=null&&p.package!=null&&p.package.Substring(p.package.IndexOf('-')+1)==row.name);
            row.isNew=row.status!="未找到运行文件"&&(package!=null?files.Any(f=>f!=null&&f.package==package.package&&updated.Contains(f.path)):(row.path!=null&&updated.Contains(row.path)));
        }
        return rows.OrderBy(r=>r.kind=="MOD"?0:r.kind=="额外插件"?1:2).ThenBy(r=>r.name,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
}
