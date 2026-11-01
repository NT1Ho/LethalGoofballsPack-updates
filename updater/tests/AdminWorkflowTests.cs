using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;

class AdminWorkflowTests {
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+(++passed)+" "+name);}
    static void Reject(Action action,string name){try{action();}catch(IOException){Check(true,name);return;}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Unexpected success: "+name);}
    static string Hash(byte[] bytes){using(var h=SHA256.Create())return Data.Hex(h.ComputeHash(bytes));}
    static void Entry(ZipArchive zip,string name,string text){using(var w=new StreamWriter(zip.CreateEntry(name).Open()))w.Write(text);}
    public class Service {
        public Dictionary<string,byte[]> Objects=new Dictionary<string,byte[]>(),Assets=new Dictionary<string,byte[]>();
        public string Feed="";public int PointerWrites,Creates;
        public bool FailBackup=true,Published,FailSecondAsset;
        public List<ReleaseAsset> Listing=new List<ReleaseAsset>();
    }
    public class Handler:HttpMessageHandler {
        readonly Service state;public Handler(Service s){state=s;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            var bytes=r.Content==null?null:await r.Content.ReadAsByteArrayAsync();
            lock(state){
                var uri=r.RequestUri;string path=uri.AbsolutePath;byte[] result=null;object json=null;var status=HttpStatusCode.OK;
                if(uri.Host.EndsWith(".myqcloud.com")){
                    if(r.Method==HttpMethod.Put){state.Objects[path]=bytes;if(path=="/lcpack/manifest.json")state.PointerWrites++;}
                    if(!state.Objects.TryGetValue(path,out result))status=HttpStatusCode.NotFound;
                }else if(uri.Host=="api.github.com"){
                    if(path=="/user")json=new{login="NT1Ho"};
                    else if(path=="/repos/"+Channel.Repo)json=new{full_name=Channel.Repo,@private=false,archived=false};
                    else if(path.Contains("/contents/")){
                        if(r.Method==HttpMethod.Put){var data=Data.Read<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));state.Feed=Encoding.UTF8.GetString(Convert.FromBase64String((string)data["content"]));json=new{};}
                        else json=new{sha="fixture",content=Convert.ToBase64String(Encoding.UTF8.GetBytes(state.Feed))};
                    }else if(path.EndsWith("/assets"))json=state.Listing;
                    else if(path.EndsWith("/releases")&&r.Method==HttpMethod.Post){
                        if(state.FailBackup)status=HttpStatusCode.ServiceUnavailable;
                        else {state.Creates++;json=new Release{id=1,tag_name="qa-new",draft=true,assets=state.Listing};}
                    }else if(path.Contains("/releases/")){
                        if(path.Contains("/tags/")&&state.Creates==0)status=HttpStatusCode.NotFound;
                        else {if(r.Method.Method=="PATCH")state.Published=true;json=new Release{id=1,tag_name="qa-new",draft=!state.Published,assets=state.Listing};}
                    }else throw new Exception("unexpected GitHub route "+path);
                }else if(uri.Host=="uploads.github.com"){
                    string name=Uri.UnescapeDataString(uri.Query.Substring(6));if(state.FailSecondAsset&&state.Assets.Count==1){state.FailSecondAsset=false;return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=r};}state.Assets.Add(name,bytes);
                    var asset=new ReleaseAsset{id=state.Listing.Count+1,name=name,size=bytes.Length,digest="sha256:"+Hash(bytes)};state.Listing.Add(asset);json=asset;
                }else if(uri.Host=="github.com"){
                    if(!state.Assets.TryGetValue(path.Split('/').Last(),out result))status=HttpStatusCode.NotFound;
                }else throw new Exception("unexpected route "+uri.Host);
                if(json!=null)result=Encoding.UTF8.GetBytes(Data.Json(json));
                var response=new HttpResponseMessage(status){RequestMessage=r,Content=new ByteArrayContent(result??new byte[0])};
                if(result!=null&&uri.Host.EndsWith(".myqcloud.com")){
                    using(var md5=MD5.Create())response.Headers.ETag=new EntityTagHeaderValue("\""+Data.Hex(md5.ComputeHash(result))+"\"");
                    response.Headers.Add("x-cos-meta-sha256",Hash(result));
                }
                return response;
            }
        }
    }
    [STAThread]static int Main(string[] args){
        try{Run(args);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Run(string[] args){
        string root=Path.Combine(Path.GetTempPath(),"lgp-admin-"+Guid.NewGuid().ToString("N").Substring(0,10));
        Directory.CreateDirectory(root);
        var ws=new Workspace(Path.Combine(root,"work"));var draft=new AdminWorkspace(ws);
        const string configPath="BepInEx/config/qa.cfg";string source=Path.Combine(root,"qa.cfg");
        File.WriteAllText(source,"# preserve\r\n[General]\r\nSpeed = 1\r\n",new UTF8Encoding(true));
        var file=ws.CacheSource(source,configPath);file.url=Channel.Web+"/releases/download/qa-base/"+file.sha256+".bin";
        var initial=new Manifest{version="qa-base",files=new List<PackFile>{file}};ws.SetRemote(initial);draft.RefreshBase();
        string old=Data.Hash(Path.Combine(ws.Root,"draft.json"));
        Reject(()=>draft.Import(new[]{Tuple.Create(source,"BepInEx/config/a.cfg"),Tuple.Create(source,"../bad")},s=>{}),"invalid second import rejected");
        Check(Data.Hash(Path.Combine(ws.Root,"draft.json"))==old&&draft.Changes().Count==0,"failed batch leaves disk and in-memory draft unchanged");
        var cfg=new AdminConfig(source);cfg.Settings.Single().Value="2";string modified=Path.Combine(root,"edit.cfg");cfg.Write(modified);
        Check(File.ReadAllBytes(modified).SequenceEqual(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# preserve\r\n[General]\r\nSpeed = 2\r\n"))),"configuration preserves BOM, comments and line endings");
        draft.Import(new[]{Tuple.Create(modified,configPath),Tuple.Create(source,"BepInEx/config/new.cfg")},s=>{});
        draft.Remove(new[]{"BepInEx/config/new.cfg"});Check(!ws.Current.deletes.Any()&&draft.Changes().Single().Kind=="替换","remove new candidate without receiver delete instruction");
        draft.Remove(new[]{configPath});Check(draft.Changes().Single().Kind=="删除","managed removal visible");
        draft.Restore(new[]{configPath});Check(draft.Changes().Count==0&&ws.Current.files.Single().url==file.url,"undo restores exact baseline URL and identity");
        var stale=new AdminWorkspace(new Workspace(ws.Root));draft.SetVersion("qa-next");Reject(()=>stale.SetVersion("qa-stale"),"stale workspace cannot overwrite newer candidate");string zipFile=Path.Combine(root,"sample.zip");
        using(var z=ZipFile.Open(zipFile,ZipArchiveMode.Create)){Entry(z,"manifest.json","{\"name\":\"Sample\",\"version_number\":\"1.0.2\",\"dependencies\":[]}");Entry(z,"plugins/Sample/resource.txt","resource");}
        using(var plan=new AdminPackage(ws,zipFile,"QA",s=>{})){draft.Import(plan.Files,s=>{},plan.Removed);}
        Check(AdminCatalog.Build(ws).Any(g=>g.key=="QA-Sample"&&g.version=="1.0.2"),"ZIP produces receiver package ownership and version metadata");
        if(args.Length>0){
            string game=Path.Combine(args[0],"qa-game");Directory.CreateDirectory(Path.Combine(game,"Lethal Company_Data/Managed"));
            foreach(string n in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(game,n),"fixture");
            foreach(var f in ws.Current.files){string dest=Path.Combine(game,f.path);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(ws.ObjectPath(f.sha256),dest,true);}
        }
        var upgradeWs=new Workspace(Path.Combine(root,"upgrade"));
        foreach(var f in ws.Current.files){string dest=upgradeWs.ObjectPath(f.sha256);if(!File.Exists(dest))File.Copy(ws.ObjectPath(f.sha256),dest);}
        upgradeWs.SetRemote(AdminWorkspace.Candidate(ws.Current,"qa-old"));var upgrade=new AdminWorkspace(upgradeWs);
        string newer=Path.Combine(root,"upgrade.zip");using(var z=ZipFile.Open(newer,ZipArchiveMode.Create)){Entry(z,"manifest.json","{\"name\":\"Sample\",\"version_number\":\"1.1.0\",\"dependencies\":[]}");Entry(z,"plugins/Sample/new.txt","new");}
        using(var plan=new AdminPackage(upgradeWs,newer,"QA",s=>{}))upgrade.Import(plan.Files,s=>{},plan.Removed);
        Check(upgradeWs.Current.deletes.Contains("BepInEx/plugins/Sample/resource.txt")&&AdminCatalog.Build(upgradeWs).Any(g=>g.key=="QA-Sample"&&g.version=="1.1.0"),"package upgrade records new version and removes its retired owned file");
        Reject(()=>{using(var plan=new AdminPackage(upgradeWs,newer,"Other",s=>{})){};},"ZIP cannot claim a different package's installed path");
        var owned=AdminCatalog.Build(ws).Single(g=>g.key=="QA-Sample").paths;
        using(var plan=AdminPackage.ForRemoval(ws,owned))draft.Import(plan.Files,s=>{},plan.Removed);
        Check(!AdminCatalog.Build(ws).Any(g=>g.key=="QA-Sample"),"whole-package removal clears package records");
        draft.Restore(draft.Changes().Select(c=>c.Path).ToArray());
        string bad=Path.Combine(root,"bad.zip");using(var z=ZipFile.Open(bad,ZipArchiveMode.Create)){Entry(z,"manifest.json","{\"name\":\"Sample\",\"version_number\":\"1.0.0\"}");Entry(z,"../outside.dll","x");}
        Reject(()=>{using(var z=new AdminPackage(ws,bad,"QA",s=>{})){};},"archive traversal rejected before candidate changes");
        draft.Import(new[]{Tuple.Create(modified,configPath)},s=>{});
        Reject(()=>draft.Prepare(s=>{}),"missing candidate version history blocks preflight");
        var pages=draft.Pages();pages.History.Insert(0,draft.SuggestHistory("qa-next",AdminCatalog.Build(ws)));draft.SavePages(pages);
        var target=draft.Prepare(s=>{});Check(target.version=="qa-next"&&draft.Pages().History[0].Version=="qa-next","history suggestion round-trips through shared page parser");
        string zip=Path.Combine(root,"candidate.zip");draft.ExportCandidate(zip,s=>{});
        using(var z=ZipFile.OpenRead(zip)){Check(z.GetEntry("candidate-manifest.json")!=null&&z.Entries.Count==4,"candidate ZIP contains only manifest, human summary and deduplicated changed blobs");}
        string install=Path.Combine(root,"install.zip");draft.ExportInstall(install,s=>{}).GetAwaiter().GetResult();using(var z=ZipFile.OpenRead(install))Check(z.Entries.Select(e=>e.FullName).OrderBy(x=>x).SequenceEqual(target.files.Select(f=>f.path).OrderBy(x=>x)),"full install archive has exactly approved game-relative paths");Reject(()=>draft.ExportInstall(install,s=>{}).GetAwaiter().GetResult(),"existing installation package never overwritten");
        var changed=ws.Current.files.Single(f=>f.path==configPath);string objectPath=ws.ObjectPath(changed.sha256);File.WriteAllText(objectPath,"broken");Reject(()=>draft.Prepare(s=>{}),"preflight rejects corrupt staged blob");
        draft.Import(new[]{Tuple.Create(modified,configPath)},s=>{});Check(Data.Hash(objectPath)==changed.sha256,"reimport repairs corrupt cache");
        string backup=Path.Combine(root,"draft-copy.json");Data.Atomic(backup,Data.Json(ws.Current));draft.Adopt(initial);draft.RestoreDraft(backup);
        Check(draft.Prepare(s=>{}).version=="qa-next","saved candidate can be recovered after adoption");
        if(args.Length>1){
            draft.Import(new[]{Tuple.Create(args[1],"BepInEx/plugins/Example.dll")},s=>{});
            Check(AdminInventory.Groups(ws).Any(g=>g.name=="Example Plugin"&&g.version=="9.8.7"),"read new unregistered DLL version without executing plugin code");
        }
        // Fresh first-release fixture exercises real signing, upload, commit and backup with no network transport.
        var simWs=new Workspace(Path.Combine(root,"simulation"));var sim=new AdminWorkspace(simWs);
        typeof(Cloud).GetMethod("Configure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{null});
        var account=CosSettings.Save(simWs,"https://fixture-123.cos.ap-guangzhou.myqcloud.com","test-id","test-key");simWs.SaveToken("fixture-token");
        sim.SetVersion("qa-new");sim.Import(new[]{Tuple.Create(source,configPath)},s=>{});
        var show=new PageContent();show.History.Add(sim.SuggestHistory("qa-new",AdminCatalog.Build(simWs)));sim.SavePages(show);
        var service=new Service();Func<HttpMessageHandler> transport=()=>new Handler(service);
        Reject(()=>AdminRelease.Run(sim,s=>{},transport).GetAwaiter().GetResult(),"simulated GitHub failure after real signed COS transaction");
        Check(service.PointerWrites==1&&File.Exists(sim.JobFile)&&simWs.Current.baseVersion=="qa-new","restartable job and committed baseline retained");
        Reject(()=>sim.SetVersion("conflict"),"unfinished release locks edits");
        var reopened=new AdminWorkspace(new Workspace(simWs.Root));service.FailBackup=false;
        service.Feed=CosSettings.Sign(account,"mods",Data.Json(new Manifest{version="unexpected"}));Reject(()=>AdminRelease.Run(reopened,s=>{},transport).GetAwaiter().GetResult(),"divergent signed GitHub pointer refuses recovery overwrite");service.Feed="";
        service.FailSecondAsset=true;Reject(()=>AdminRelease.Run(reopened,s=>{},transport).GetAwaiter().GetResult(),"GitHub interrupted after its first verified attachment");
        Check(service.PointerWrites==1&&service.Assets.Count>0&&reopened.Pending,"partial GitHub release and durable resume journal retained");
        var retryMessages=new List<string>();AdminRelease.Run(reopened,s=>{lock(retryMessages)retryMessages.Add(s);},transport).GetAwaiter().GetResult();
        Check(retryMessages.Any(s=>s.StartsWith("GitHub · 文件")&&!s.Contains("已复用 0")),"resumed transfer excludes previously uploaded attachment");
        string payload=Cloud.Verify(service.Feed,"mods");
        Check(service.PointerWrites==1&&service.Creates==1&&!reopened.Pending&&service.Published,"resume finishes backup without re-committing COS");
        Check(payload==Cloud.Verify(Encoding.UTF8.GetString(service.Objects["/lcpack/manifest.json"]),"mods"),"actual COS and GitHub signed payloads converge in memory");
        Check(Encoding.UTF8.GetString(service.Assets["manifest.json"])==payload,"published simulated GitHub attachment matches signed payload");
        if(args.Length>0){Directory.CreateDirectory(args[0]);File.WriteAllText(Path.Combine(args[0],"qa-signed.json"),service.Feed);File.WriteAllText(Path.Combine(args[0],"qa-public.json"),Data.Json(account.channel));File.WriteAllText(Path.Combine(args[0],"qa-pages.json"),Data.Json(show));}
        Console.WriteLine("RESULT "+passed+" workflow checks; simulated services only.");
    }
}
