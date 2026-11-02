using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LcPack {
public class ResultFile { public string path,before,sha256; }
public class ModResult {
    public string package,version,fromVersion,baseHash,registeredBase,kind,summary,tests,instructions;
    public int revision;
    public bool? own;
    public string[] dependencies;
    public List<ResultFile> files;
}
public class CumulativeBundle {
    public string format="LGP-CUMULATIVE-1",baseVersion,baseHash;
    public List<ModResult> results=new List<ModResult>();
}
public static class Cumulative {
    public const string Index="pack-update.json",Notes="累计更新说明.txt",ArchiveName="标准累计更新.zip";
    const string Sources="BepInEx/pack-info/download-sources.json",Owners="BepInEx/pack-info/original-files.json";
    static readonly StringComparer CI=StringComparer.OrdinalIgnoreCase;
    static readonly string[] kinds={"新增","升级","修复","优化","移除"};
    public static string Home(Workspace ws){return CI.Equals(ws.Root,Workspace.DefaultRoot)?@"D:\Lethal company pack Updater\本地新增Mods":Path.Combine(ws.Root,"本地新增Mods");}
    public static string Inbox(Workspace ws){string p=Path.Combine(Home(ws),"待发布");SafePath.NoLinks(p);Directory.CreateDirectory(p);return p;}
    static bool Hash(string s){return Regex.IsMatch(s??"",@"\A[a-f0-9]{64}\z");}
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    static IEnumerable<string> Receipts(Workspace ws){
        foreach(var dir in Directory.GetDirectories(Inbox(ws)).OrderBy(s=>s,CI)){
            SafePath.NoLinks(dir);string path=Path.Combine(dir,"result.json");SafePath.NoLinks(path);
            Require(File.Exists(path),"交付缺少 result.json："+dir);yield return path;
        }
    }
    static ModResult Receipt(string path){Require(new FileInfo(path).Length<=1024*1024,"登记资料过大");return Data.Load<ModResult>(path);}
    public static void ValidateReceipt(string json,string baseline){Validate(Data.Read<ModResult>(json),baseline);}
    static void Validate(ModResult m,string baseline){
        Require(m!=null&&Hash(m.baseHash)&&m.baseHash==baseline,"成果基线不匹配，请重新核对并登记");
        Require(Regex.IsMatch(m.package??"",@"\A[A-Za-z0-9_]+-[A-Za-z0-9_]+\z")&&m.revision>0&&!String.IsNullOrWhiteSpace(m.version),"组件身份、版本或登记序号无效");
        Require(kinds.Contains(m.kind)&&!String.IsNullOrWhiteSpace(m.summary)&&!String.IsNullOrWhiteSpace(m.tests),m.package+" 缺少类别、实际说明或验证记录");
        Require(m.dependencies!=null&&m.dependencies.All(d=>Regex.IsMatch(d??"",@"\A[\w]+-[\w]+(?:-[0-9]+(?:\.[0-9]+){1,3})?\z")),m.package+" 依赖应填写作者-组件，可附最低版本");
        Require(m.files!=null&&m.files.Count>0&&m.files.Count<=10000,m.package+" 缺少文件清单");
        foreach(var f in m.files){
            Require(f!=null,"存在空文件项");SafePath.Validate(f.path);
            bool removedPackageInfo=m.kind=="移除"&&f.sha256==null&&f.path.StartsWith("BepInEx/pack-info/"+m.package+"/",StringComparison.OrdinalIgnoreCase);
            Require((removedPackageInfo||Regex.IsMatch(f.path,@"^BepInEx/(plugins|config|patchers)/",RegexOptions.IgnoreCase))&&!Regex.IsMatch(f.path,@"(?:^|/)(?:\.git|bin|obj|logs?|cache|saves?|diagnostics|tests?)(?:/|$)|\.(?:pdb|log|bak|old|tmp|sav|exe|ps1|cmd|bat|cs|zip)$|LethalDiagnostics\.dll$|nt1ho\.lethaldiagnostics\.cfg$",RegexOptions.IgnoreCase),"此路径不属于可登记的最终 MOD 文件："+f.path);
            Require((f.before==null||Hash(f.before))&&(f.sha256==null||Hash(f.sha256))&&f.before!=f.sha256,"文件新旧哈希无效或无变化："+f.path);
        }
    }
    static void Check(CumulativeBundle b,AdminWorkspace w){
        Require(b!=null&&b.format=="LGP-CUMULATIVE-1"&&w.Base!=null&&b.baseHash==w.Store.Current.baseManifestHash&&b.baseVersion==w.Store.Current.baseVersion,"累计包与本地正式基线不匹配");
        Require(b.results!=null&&b.results.Count<=2000,"累计清单无效");
        var ids=new HashSet<string>(CI);var paths=new HashSet<string>(CI);var baseline=w.Base.files.ToDictionary(f=>f.path,CI);
        foreach(var m in b.results){Validate(m,b.baseHash);Require(ids.Add(m.package),"重复登记："+m.package);
            foreach(var f in m.files){PackFile old;baseline.TryGetValue(f.path,out old);
                Require(paths.Add(f.path),"多个成果声明相同文件："+f.path);
                Require((old==null?null:old.sha256)==f.before,"与正式文件身份不符："+f.path);
            }
        }
    }
    public static string Description(CumulativeBundle b){
        var s=new StringBuilder();s.AppendLine("累计更新说明 · 正式基线 "+b.baseVersion);s.AppendLine("清单 SHA-256 "+b.baseHash);
        if(b.results.Count==0)s.AppendLine("尚无已登记的待发布成果。");
        foreach(var kind in kinds)foreach(var m in b.results.Where(r=>r.kind==kind)){
            s.AppendLine().AppendLine("【"+kind+"】"+m.package+" · "+(String.IsNullOrWhiteSpace(m.fromVersion)?"":m.fromVersion+" → ")+m.version).AppendLine(m.summary).AppendLine("验证："+m.tests);
            if(m.dependencies.Length>0)s.AppendLine("依赖："+String.Join("、",m.dependencies));
            if(!String.IsNullOrWhiteSpace(m.instructions))s.AppendLine("安装说明："+m.instructions);
            foreach(var f in m.files)s.AppendLine((f.sha256==null?"删除":f.before==null?"新增":"替换")+"  "+f.path);
        }return s.ToString();
    }
    public static string Build(AdminWorkspace w){
        var b=new CumulativeBundle{baseVersion=w.Store.Current.baseVersion,baseHash=w.Store.Current.baseManifestHash};
        Require(!String.IsNullOrEmpty(b.baseVersion)&&Hash(b.baseHash),"请先采用或核对正式清单");
        var docs=Receipts(w.Store).Select(p=>new{path=p,m=Receipt(p)}).ToList();
        foreach(var d in docs)Require(d.m!=null&&!String.IsNullOrEmpty(d.m.package),"累计区包含无效登记");
        var files=new Dictionary<string,string>();
        foreach(var g in docs.GroupBy(d=>d.m.package,CI).OrderBy(g=>g.Key,CI)){
            Require(g.GroupBy(d=>d.m.revision).All(r=>r.Count()==1),"同一组件的登记序号重复："+g.Key);
            var latest=g.OrderByDescending(d=>d.m.revision).First();Validate(latest.m,latest.m.baseHash);
            if(latest.m.baseHash!=b.baseHash){
                string previous=Path.Combine(w.Store.Root,"baseline-"+latest.m.baseHash+".json");
                Require(File.Exists(previous)&&Data.Hash(previous)==latest.m.baseHash,"缺少登记时的正式基线，请重新确认："+g.Key);
                // Reuse a result across releases only if its original package and owned paths are unchanged.
                var old=Data.Load<Manifest>(previous);var now=w.Base.files.ToDictionary(f=>f.path,CI);
                foreach(var meta in new[]{Owners,Sources}){
                    Func<Manifest,string> rows=manifest=>{
                        var f=manifest.files.SingleOrDefault(x=>CI.Equals(x.path,meta));if(f==null)return "";
                        string cache=w.Store.ObjectPath(f.sha256);Require(File.Exists(cache)&&Data.Hash(cache)==f.sha256,"缺少基线组件归属："+g.Key);
                        return String.Join("\n",Data.Load<List<Dictionary<string,object>>>(cache).Where(r=>CI.Equals(Val(r,"package"),g.Key)).Select(r=>Data.Json(r)).OrderBy(s=>s,StringComparer.Ordinal));
                    };
                    Require(rows(old)==rows(w.Base),"组件正式资料已变化，请重新确认完整成果："+g.Key);
                }
                foreach(var f in latest.m.files){PackFile n;now.TryGetValue(f.path,out n);Require((n==null?null:n.sha256)==f.before,"正式文件已变化，请重新确认："+f.path);}
                latest.m.registeredBase=latest.m.baseHash;latest.m.baseHash=b.baseHash;
            }
            b.results.Add(latest.m);
            foreach(var f in latest.m.files.Where(f=>f.sha256!=null)){
                string file=SafePath.Target(Path.Combine(Path.GetDirectoryName(latest.path),"payload"),f.path);
                Require(File.Exists(file)&&Data.Hash(file)==f.sha256,"登记文件已变化："+f.path);files[f.sha256]=file;
            }
        }
        Check(b,w);
        string root=Home(w.Store),output=Path.Combine(root,ArchiveName),tmp=output+"."+Guid.NewGuid().ToString("N");
        SafePath.NoLinks(output);string notes=Description(b);
        try{using(var zip=ZipFile.Open(tmp,ZipArchiveMode.Create)){
            Write(zip,Index,Data.Json(b));Write(zip,Notes,notes);
            foreach(var f in files)zip.CreateEntryFromFile(f.Value,"objects/"+f.Key,CompressionLevel.Fastest);
        }Read(w,tmp);Data.Replace(tmp,output);Data.Atomic(Path.Combine(root,Notes),notes);return output;}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static void Write(ZipArchive zip,string name,string text){using(var w=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)))w.Write(text);}
    static string Text(ZipArchiveEntry e){Require(e!=null&&e.Length<=4*1024*1024,"累计包缺少描述或说明过大");using(var r=new StreamReader(e.Open())){var buf=new char[8192];var s=new StringBuilder();int n;while((n=r.Read(buf,0,buf.Length))>0){Require(s.Length+n<=4*1024*1024,"实际说明过大");s.Append(buf,0,n);}return s.ToString();}}
    public static bool Standard(string path){using(var z=ZipFile.OpenRead(path))return z.GetEntry(Index)!=null;}
    public static CumulativeBundle Read(AdminWorkspace w,string path){
        SafePath.NoLinks(path);using(var zip=ZipFile.OpenRead(path)){
            string json=Text(zip.GetEntry(Index));Require(Data.Read<Dictionary<string,object>>(json).ContainsKey("format"),"缺少标准累计格式标识");var b=Data.Read<CumulativeBundle>(json);Check(b,w);
            var obj=b.results.SelectMany(m=>m.files).Where(f=>f.sha256!=null).Select(f=>f.sha256).Distinct().ToList();
            Require(obj.Count<=10000&&zip.Entries.Sum(e=>e.Length)<=8L*1024*1024*1024,"累计归档超过数量或容量上限");
            Require(zip.Entries.Count==obj.Count+2&&Text(zip.GetEntry(Notes))==Description(b),"ZIP 含多余文件或中文说明与登记不符");
            foreach(var hash in obj){
                var e=zip.GetEntry("objects/"+hash);Require(e!=null&&e.Length<=2L*1024*1024*1024,"累计 ZIP 对象缺失或超过限额");
                string dest=w.Store.ObjectPath(hash),tmp=dest+"."+Guid.NewGuid().ToString("N");SafePath.NoLinks(dest);
                try{using(var src=e.Open())using(var dst=File.Create(tmp)){byte[] buf=new byte[65536];int n;while((n=src.Read(buf,0,buf.Length))>0){Require(dst.Length+n<=e.Length,"文件长度超过声明");dst.Write(buf,0,n);}Require(dst.Length==e.Length,"文件长度不符");}
                    Require(Data.Hash(tmp)==hash,"累计 ZIP 文件哈希不符："+hash);
                    if(!File.Exists(dest)||Data.Hash(dest)!=hash)Data.Replace(tmp,dest);
                }finally{if(File.Exists(tmp))File.Delete(tmp);}
            }return b;
        }
    }
    static string Val(Dictionary<string,object> r,string key){object v;return r.TryGetValue(key,out v)?Convert.ToString(v):"";}
    public static void Apply(AdminWorkspace w,CumulativeBundle b,string tag,bool save){
        Check(b,w);Require(!w.Pending&&b.results.Count>0,"没有可导入的成果，或有待恢复发布");tag=Publisher.VersionName(tag);
        Require(tag!=""&&tag!=b.baseVersion,"请填写与正式版本不同的新版本号");var ws=w.Store;string receipt=Data.Json(b);
        if(ws.Current.cumulative!=null){
            Require(ws.Current.cumulative==receipt&&ws.Current.releaseVersion==tag,"已有另一份累计候选，请恢复导入前草稿再整理");
            VerifySelection(ws,b);return;
        }
        Require(String.IsNullOrEmpty(ws.Current.releaseVersion)||ws.Current.releaseVersion==tag,"已有草稿的候选版本不同");
        var next=Data.Read<Draft>(Data.Json(ws.Current));var current=next.files.ToDictionary(f=>f.path,CI);
        var owners=AdminPackage.Metadata(ws,Owners);var packs=AdminPackage.Metadata(ws,Sources);var pages=w.Pages();
        var history=pages.History.FirstOrDefault(h=>h.Version==tag);if(history==null){history=new HistoryRelease{Version=tag,PublishedUtc=DateTimeOffset.Now.ToString("o"),Changes=new List<HistoryEntry>()};pages.History.Insert(0,history);}
        foreach(var m in b.results){
            var previous=packs.SingleOrDefault(r=>CI.Equals(Val(r,"package"),m.package));
            if(!String.IsNullOrEmpty(m.fromVersion))Require(previous!=null&&Val(previous,"version")==m.fromVersion,"候选组件起始版本不匹配："+m.package);
            string key=m.package.Substring(m.package.IndexOf('-')+1);
            foreach(var f in m.files){
                PackFile old;current.TryGetValue(f.path,out old);var owner=owners.SingleOrDefault(r=>CI.Equals(Val(r,"path"),f.path));
                Require(owner==null||CI.Equals(Val(owner,"package"),m.package),"路径属于其他组件："+f.path);
                Require((old==null?null:old.sha256)==f.before||(old==null?null:old.sha256)==f.sha256,"与未发布草稿冲突："+f.path);
                if(old==null&&next.deletes.Contains(f.path,CI)&&f.sha256!=null)throw new IOException("草稿已标记删除："+f.path);
                if(owner!=null)owners.Remove(owner);
                next.deletes.RemoveAll(p=>CI.Equals(p,f.path));current.Remove(f.path);
                if(f.sha256==null){next.deletes.Add(f.path);continue;}
                string src=ws.ObjectPath(f.sha256);Require(File.Exists(src)&&Data.Hash(src)==f.sha256,"累计对象缺失或损坏："+f.path);
                current.Add(f.path,new PackFile{path=f.path,sha256=f.sha256,size=new FileInfo(src).Length});
                owners.Add(new Dictionary<string,object>{{"package",m.package},{"path",f.path},{"source",owner==null?f.path:Val(owner,"source")},{"sha256",f.sha256}});
            }
            if(previous!=null)packs.Remove(previous);
            if(m.kind=="移除")Require(!owners.Any(r=>CI.Equals(Val(r,"package"),m.package))&&m.files.All(f=>f.sha256==null),"移除组件仍有登记文件："+m.package);
            else {
                Require(owners.Any(r=>CI.Equals(Val(r,"package"),m.package)),"组件没有安装文件："+m.package);
                var row=previous??new Dictionary<string,object>();row["package"]=m.package;row["version"]=m.version;row["dependencies"]=m.dependencies;row.Remove("url");row.Remove("sha256");row["source"]="标准累计更新";packs.Add(row);
                var page=pages.Find(key);if(page==null){page=new PageMod{Key=key,Name=key,Purpose=m.summary,Order=pages.Mods.Count};pages.Mods.Add(page);}
                if(m.own.HasValue)page.Own=m.own.Value;page.New=true;
            }
            var h=new HistoryEntry{Kind=m.kind,Mod=key,Summary=m.summary+"（"+(String.IsNullOrEmpty(m.fromVersion)?"":m.fromVersion+" → ")+m.version+"）"};if(!history.Changes.Any(x=>x.Mod==h.Mod&&x.Kind==h.Kind&&x.Summary==h.Summary))history.Changes.Add(h);
        }
        CheckDependencies(packs,b);
        pages.Validate();foreach(var item in new[]{Tuple.Create(Owners,Data.Json(owners)),Tuple.Create(Sources,Data.Json(packs)),Tuple.Create(PageContent.RelativePath,Data.Json(pages))}){
            string temp=Path.Combine(ws.Root,"cumulative-"+Guid.NewGuid().ToString("N")+".tmp");
            try{Data.Atomic(temp,item.Item2);current[item.Item1]=ws.CacheSource(temp,item.Item1);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        next.files=current.Values.ToList();next.releaseVersion=tag;next.cumulative=receipt;AdminWorkspace.Candidate(next,tag);
        if(save){Data.Atomic(Path.Combine(ws.Root,"draft-before-cumulative-"+Guid.NewGuid().ToString("N")+".json"),Data.Json(ws.Current));ws.CommitDraft(next);}
    }
    static void CheckDependencies(List<Dictionary<string,object>> packs,CumulativeBundle b){
        var touched=new HashSet<string>(b.results.Select(m=>m.package),CI);
        foreach(var pkg in packs){object deps;if(!pkg.TryGetValue("dependencies",out deps)||deps==null)continue;
            foreach(string dep in ((System.Collections.IEnumerable)deps).Cast<string>()){
                var parts=dep.Split('-');string id=String.Join("-",parts.Take(2));if(!touched.Contains(id)&&!touched.Contains(Val(pkg,"package")))continue;
                var installed=packs.FirstOrDefault(r=>CI.Equals(Val(r,"package"),id));Version min,found;
                Require(installed!=null&&(parts.Length==2||(Version.TryParse(parts.Last(),out min)&&Version.TryParse(Val(installed,"version"),out found)&&found>=min)),Val(pkg,"package")+" 缺少依赖："+dep);
            }
        }
    }
    public static void VerifySelection(Workspace ws,CumulativeBundle b){
        var files=ws.Current.files.ToDictionary(f=>f.path,CI);
        var packages=AdminPackage.Metadata(ws,Sources);CheckDependencies(packages,b);
        foreach(var mod in b.results){var entry=packages.SingleOrDefault(r=>CI.Equals(Val(r,"package"),mod.package));
            Require(mod.kind=="移除"?entry==null:entry!=null&&Val(entry,"version")==mod.version,"候选组件版本偏离登记："+mod.package);}
        foreach(var f in b.results.SelectMany(m=>m.files)){PackFile actual;files.TryGetValue(f.path,out actual);Require((actual==null?null:actual.sha256)==f.sha256&&(f.sha256!=null||ws.Current.deletes.Contains(f.path,CI)),"候选已偏离登记成果："+f.path);}
    }
    public static void Archive(Workspace ws,string receipt,string tag){
        if(receipt==null)return;
        string gate=Path.Combine(Home(ws),"登记.lock");SafePath.NoLinks(gate);Directory.CreateDirectory(Home(ws));
        using(var lease=new FileStream(gate,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))ArchiveLocked(ws,receipt,tag);
    }
    static void ArchiveLocked(Workspace ws,string receipt,string tag){
        var bundle=Data.Read<CumulativeBundle>(receipt);
        string dest=Path.Combine(Home(ws),"已发布",Publisher.VersionName(tag));SafePath.NoLinks(dest);Directory.CreateDirectory(dest);
        string archivedZip=Path.Combine(dest,ArchiveName);
        if(!File.Exists(archivedZip)){
            string tmp=archivedZip+"."+Guid.NewGuid().ToString("N");
            try{using(var z=ZipFile.Open(tmp,ZipArchiveMode.Create)){
                Write(z,Index,receipt);Write(z,Notes,Description(bundle));
                foreach(string hash in bundle.results.SelectMany(m=>m.files).Select(f=>f.sha256).Where(h=>h!=null).Distinct()){
                    string src=ws.ObjectPath(hash);SafePath.NoLinks(src);Require(Data.Hash(src)==hash,"归档对象损坏："+hash);
                    z.CreateEntryFromFile(src,"objects/"+hash,CompressionLevel.Fastest);
                }
            }File.Move(tmp,archivedZip);}finally{if(File.Exists(tmp))File.Delete(tmp);}
        }
        var waiting=Receipts(ws).Select(p=>new{path=p,m=Receipt(p)}).ToList();
        foreach(var accepted in bundle.results){
            var original=Data.Read<ModResult>(Data.Json(accepted));
            if(original.registeredBase!=null){original.baseHash=original.registeredBase;original.registeredBase=null;}
            var group=waiting.Where(d=>d.m.package==accepted.package).OrderBy(d=>d.m.revision).ToList();
            if(!group.Any(d=>Data.Json(d.m)==Data.Json(original)))continue;
            // Move the matched latest receipt last so an interrupted archive can be resumed.
            foreach(var d in group.Where(x=>x.m.revision<=accepted.revision)){
                var source=Path.GetDirectoryName(d.path);Directory.Move(source,Path.Combine(dest,Path.GetFileName(source)));
            }
        }
        Data.Atomic(Path.Combine(dest,Index),receipt);Data.Atomic(Path.Combine(dest,Notes),Description(bundle));
        if(!Directory.GetDirectories(Inbox(ws)).Any())Build(new AdminWorkspace(ws));
        else{
            string current=Path.Combine(Home(ws),ArchiveName);
            if(File.Exists(current)){bool same;using(var z=ZipFile.OpenRead(current))same=Text(z.GetEntry(Index))==receipt;
                if(same){File.Move(current,Path.Combine(dest,"整理时的累计包-"+Guid.NewGuid().ToString("N")+".zip"));
                    Data.Atomic(Path.Combine(Home(ws),Notes),"已发布 "+tag+"。尚有未发布登记，请在管理端整理累计区并核对新基线。");}}
        }
    }
}
}
