using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LcPack {
public sealed class DraftChange {
    public string Path {get;set;}
    public string Kind {get;set;}
    public PackFile Before,After;
    public string OldSize {get{return Before==null?"—":AdminWorkspace.Size(Before.size);}}
    public string NewSize {get{return After==null?"—":AdminWorkspace.Size(After.size);}}
}
public sealed class AdminWorkspace {
    public readonly Workspace Store;
    public Manifest Base {get;private set;}
    static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
    public AdminWorkspace(Workspace store){Store=store;RefreshBase();}
    public void RefreshBase(){Base=Store.Baseline();}
    public static string Size(long bytes){
        if(bytes<1024)return bytes+" B";
        if(bytes<1048576)return (bytes/1024.0).ToString("0.#")+" KB";
        return (bytes/1048576.0).ToString("0.#")+" MB";
    }
    public string JobFile {get{return Path.Combine(Store.Root,"admin-release.pending.json");}}
    public bool Pending {get{return File.Exists(JobFile)||File.Exists(Path.Combine(Store.Root,"page-publish.pending.json"));}}
    void Editable(){
        if(Pending)throw new IOException("有未完成的发布，请到发布中心继续或核对恢复状态。");
        if(Base==null)throw new IOException("缺少这份旧草稿的基线，请先在发布中心核对正式版本。");
    }
    Draft BeginEdit(){
        Editable();
        if(!String.IsNullOrEmpty(Store.Current.baseVersion))Store.RememberBaseline(Base);
        return Data.Read<Draft>(Data.Json(Store.Current));
    }
    static Dictionary<string,PackFile> Index(IEnumerable<PackFile> files){return files.ToDictionary(f=>f.path,Paths);}
    public List<DraftChange> Changes(){
        if(Base==null)return new List<DraftChange>();
        var before=Index(Base.files);var after=Index(Store.Current.files);
        var changes=before.Keys.Union(after.Keys,Paths).OrderBy(k=>k,Paths).Select(path=>{
            PackFile a,b;before.TryGetValue(path,out a);after.TryGetValue(path,out b);
            return new DraftChange{Path=path,Before=a,After=b,Kind=a==null?"新增":b==null?"删除":a.sha256!=b.sha256||a.size!=b.size?"替换":a.url!=b.url?"引用变更":""};
        }).Where(c=>c.Kind!="").ToList();
        foreach(string path in Store.Current.deletes.Except(Base.deletes,Paths).Where(p=>!before.ContainsKey(p)))
            changes.Add(new DraftChange{Path=path,Kind="删除指令"});
        foreach(string path in Base.deletes.Except(Store.Current.deletes,Paths).Where(p=>!after.ContainsKey(p)))
            changes.Add(new DraftChange{Path=path,Kind="撤回删除"});
        return changes;
    }
    public static Manifest Candidate(Draft draft,string version){
        var m=Data.Read<Manifest>(Data.Json(new Manifest{version=version,files=draft.files,deletes=draft.deletes}));
        foreach(var f in m.files)if(String.IsNullOrEmpty(f.url))f.url=Channel.Web+"/releases/download/"+version+"/"+f.sha256+".bin";
        SafePath.ValidateManifest(m);
        return m;
    }
    void Commit(Draft next){
        Candidate(next,"candidate"); // Reuse the receiver's limits and path-collision checks.
        Store.CommitDraft(next);
    }
    public void SetVersion(string version){
        var next=BeginEdit();next.releaseVersion=Publisher.VersionName(version.Trim());Commit(next);
    }
    public void Import(IEnumerable<Tuple<string,string>> items,Action<string> progress,IEnumerable<string> removed=null){
        var next=BeginEdit();var files=Index(next.files);var before=Index(Base.files);
        var seen=new HashSet<string>(Paths);
        foreach(var item in items){
            string target=SafePath.Validate(item.Item2);
            if(!seen.Add(target))throw new IOException("本批有重复目标："+target);
            progress("正在校验 "+target);
            var file=Store.CacheSource(item.Item1,target);
            PackFile old;
            if(before.TryGetValue(target,out old)&&old.sha256==file.sha256)file=old;
            files[target]=file;next.deletes.RemoveAll(p=>Paths.Equals(p,target));
        }
        if(removed!=null)foreach(string path in removed){
            SafePath.Validate(path);
            if(seen.Contains(path))throw new IOException("同批加入和删除了相同文件："+path);
            files.Remove(path);next.deletes.RemoveAll(p=>Paths.Equals(p,path));
            if(before.ContainsKey(path)||Base.deletes.Contains(path,Paths))next.deletes.Add(path);
        }
        next.files=files.Values.ToList();Commit(next);
    }
    public void Remove(IEnumerable<string> paths){
        using(var plan=AdminPackage.ForRemoval(Store,paths.ToArray()))Import(plan.Files,s=>{},plan.Removed);
    }
    public void Restore(IEnumerable<string> paths){
        var next=BeginEdit();var files=Index(next.files);var before=Index(Base.files);
        foreach(string path in paths.Distinct(Paths)){
            SafePath.Validate(path);files.Remove(path);next.deletes.RemoveAll(p=>Paths.Equals(p,path));
            PackFile old;
            if(before.TryGetValue(path,out old))files.Add(path,old);
            else if(Base.deletes.Contains(path,Paths))next.deletes.Add(path);
        }
        next.files=files.Values.ToList();Commit(next);
    }
    public void Observe(Manifest remote){
        if(remote==null)throw new IOException("尚未读取到正式清单。");
        if(Workspace.Fingerprint(remote)==Store.Current.baseManifestHash){
            Store.RememberBaseline(remote);RefreshBase();
        }
    }
    public void ObserveSignedPayload(string verifiedPayload){
        var remote=Data.Read<Manifest>(verifiedPayload);SafePath.ValidateManifest(remote);
        using(var sha=System.Security.Cryptography.SHA256.Create()){
            string hash=Data.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(verifiedPayload)));
            if(hash==Store.Current.baseManifestHash){
                string path=Path.Combine(Store.Root,"baseline-"+hash+".json");SafePath.NoLinks(path);
                if(!File.Exists(path)||Data.Hash(path)!=hash)Data.Atomic(path,verifiedPayload);
                RefreshBase();return;
            }
        }
        Observe(remote);
    }
    public void Adopt(Manifest remote){
        if(Pending)throw new IOException("请先处理未完成的发布。");
        if(Base!=null&&!String.IsNullOrEmpty(Store.Current.baseVersion))Store.RememberBaseline(Base);
        if(File.Exists(Path.Combine(Store.Root,"draft.json"))) {
            string backup=Path.Combine(Store.Root,"draft-before-sync-"+Guid.NewGuid().ToString("N")+".json");
            Data.Atomic(backup,Data.Json(Store.Current));
        }
        Store.SetRemote(remote);RefreshBase();
    }
    public PageContent Pages(){
        var f=Store.Current.files.FirstOrDefault(x=>Paths.Equals(x.path,PageContent.RelativePath));
        if(f==null)return new PageContent{History=HistoryWindow.ReadHistory()};
        string path=Store.ObjectPath(f.sha256);
        if(!File.Exists(path)||Data.Hash(path)!=f.sha256)throw new IOException("页面资料尚未缓存，请先读取正式资料。");
        return PageContent.Load(path);
    }
    public void SavePages(PageContent pages){
        pages.Validate();
        string tmp=Path.Combine(Store.Root,"page-edit-"+Guid.NewGuid().ToString("N")+".tmp");
        try{Data.Atomic(tmp,Data.Json(pages));Import(new[]{Tuple.Create(tmp,PageContent.RelativePath)},s=>{});}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public HistoryRelease SuggestHistory(string tag,List<ModGroup> groups){
        string version=Publisher.VersionName(tag);
        if(version.Length==0)throw new IOException("请先在发布中心保存候选版本名。");
        var owners=new Dictionary<string,string>(Paths);
        foreach(var group in groups)foreach(string path in group.paths)owners[path]=group.name;
        var changes=Changes().Where(c=>c.Path!=PageContent.RelativePath).GroupBy(c=>owners.ContainsKey(c.Path)?owners[c.Path]:c.Path);
        var release=new HistoryRelease{Version=version,PublishedUtc=DateTimeOffset.Now.ToString("o"),Changes=new List<HistoryEntry>()};
        foreach(var group in changes)release.Changes.Add(new HistoryEntry{Kind="更新",Mod=group.Key,Summary=String.Join("、",group.GroupBy(c=>c.Kind).Select(g=>g.Key+" "+g.Count()+" 个文件"))});
        if(release.Changes.Count==0)throw new IOException("没有需要生成说明的内容变更。");
        return release;
    }
    public Manifest Prepare(Action<string> progress){
        if(Base==null)throw new IOException("请先取得草稿基线。");
        var changes=Changes();
        if(changes.Count==0)throw new IOException("候选与基线一致，没有待发布内容。");
        string tag=Publisher.VersionName(Store.Current.releaseVersion??"");
        bool pagesOnly=changes.All(c=>c.Path==PageContent.RelativePath);
        if(String.IsNullOrEmpty(tag)&&pagesOnly)tag=Store.Current.baseVersion;
        if(String.IsNullOrEmpty(tag)||(!pagesOnly&&tag==Store.Current.baseVersion))throw new IOException("整合包内容已变更，请填写明确的新版本名。");
        if(tag.StartsWith("updater-v",StringComparison.OrdinalIgnoreCase))throw new IOException("此版本名属于接收端更新通道。");
        var manifest=Candidate(Store.Current,tag);
        if(manifest.files.Count==0)throw new IOException("整合包不能为空。");
        foreach(var c in changes.Where(c=>c.After!=null)){
            var f=c.After;string file=Store.ObjectPath(f.sha256);SafePath.NoLinks(file);progress("正在校验 "+c.Path);
            if(!File.Exists(file)||new FileInfo(file).Length!=f.size||Data.Hash(file)!=f.sha256)throw new IOException("候选文件未通过校验："+c.Path);
        }
        if(changes.Any(c=>Paths.Equals(c.Path,"BepInEx/plugins/NT1Ho-LethalDiagnostics/LethalDiagnostics.dll")||Paths.Equals(c.Path,"BepInEx/config/nt1ho.lethaldiagnostics.cfg")))
            throw new IOException("诊断插件和启停配置由接收端管理，请从整合包候选撤销该项。");
        if(!manifest.files.Any(f=>f.path==PageContent.RelativePath))throw new IOException("候选需要包含接收端展示资料与历史记录。");
        var pages=Pages();
        if(!pagesOnly||tag!=Store.Current.baseVersion){
            if(!manifest.files.Any(f=>f.path==PageContent.RelativePath)||!pages.History.Any(h=>h.Version==tag&&h.Changes.Count>0))
                throw new IOException("请在接收端资料中保存 "+tag+" 的更新记录。");
        }
        return manifest;
    }
    public async Task<string> CachedFile(PackFile file){
        string path=Store.ObjectPath(file.sha256);SafePath.NoLinks(path);
        if(File.Exists(path)&&new FileInfo(path).Length==file.size&&Data.Hash(path)==file.sha256)return path;
        if(String.IsNullOrEmpty(file.url))throw new IOException("候选缓存缺失或损坏，请重新导入："+file.path);
        string tmp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try{using(var transfer=new Transfer())await transfer.Download(file,tmp,null).ConfigureAwait(false);Data.Replace(tmp,path);return path;}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public void RestoreDraft(string file){
        if(Pending)throw new IOException("请先处理未完成的发布。");
        SafePath.NoLinks(file);var next=Data.Load<Draft>(file);Candidate(next,"candidate");
        foreach(var f in next.files.Where(f=>String.IsNullOrEmpty(f.url)))if(!File.Exists(Store.ObjectPath(f.sha256))||Data.Hash(Store.ObjectPath(f.sha256))!=f.sha256)throw new IOException("副本引用的候选文件缺失："+f.path);
        Data.Atomic(Path.Combine(Store.Root,"draft-before-restore-"+Guid.NewGuid().ToString("N")+".json"),Data.Json(Store.Current));
        Commit(next);RefreshBase();
    }
    public async Task ExportInstall(string destination,Action<string> progress) {
        if(File.Exists(destination))throw new IOException("安装包请使用新文件名，以免覆盖已有整包。");
        var manifest=Changes().Count==0?Base:Prepare(progress);
        if(manifest==null||manifest.files.Count==0)throw new IOException("没有可导出的完整清单。");
        SafePath.ValidateManifest(manifest);
        string tmp=destination+".partial-"+Guid.NewGuid().ToString("N");
        try{
            using(var stream=new FileStream(tmp,FileMode.CreateNew))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)){
                int count=0;
                foreach(var file in manifest.files){
                    progress("安装包 "+(++count)+"/"+manifest.files.Count+" · "+file.path);
                    string source=await CachedFile(file).ConfigureAwait(false);
                    zip.CreateEntryFromFile(source,file.path,CompressionLevel.Fastest);
                }
            }
            File.Move(tmp,destination);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public void ExportCandidate(string destination,Action<string> progress){
        var manifest=Prepare(progress);
        string tmp=destination+".partial-"+Guid.NewGuid().ToString("N");
        try{
            using(var stream=new FileStream(tmp,FileMode.CreateNew))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)){
                using(var w=new StreamWriter(zip.CreateEntry("candidate-manifest.json").Open()))w.Write(Data.Json(manifest));
                var changes=Changes();
                using(var w=new StreamWriter(zip.CreateEntry("候选说明.txt").Open(),new UTF8Encoding(true))){
                    w.WriteLine("本地候选材料 · "+manifest.version+"（未发布）");w.WriteLine("基线 "+Store.Current.baseVersion+" / "+Store.Current.baseManifestHash);
                    foreach(var c in changes)w.WriteLine(c.Kind+"  "+c.Path);
                }
                foreach(var file in changes.Where(c=>c.After!=null).Select(c=>c.After).GroupBy(f=>f.sha256).Select(g=>g.First()))
                    zip.CreateEntryFromFile(Store.ObjectPath(file.sha256),"objects/"+file.sha256+".bin",CompressionLevel.Fastest);
            }Data.Replace(tmp,destination);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
}
