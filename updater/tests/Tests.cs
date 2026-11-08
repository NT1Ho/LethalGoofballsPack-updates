using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;

class Downloads : HttpMessageHandler {
    public Manifest manifest;public Func<Manifest> provider;public readonly Dictionary<string,byte[]> blobs=new Dictionary<string,byte[]>();
    public int files,latest,tagged,active,maxActive;public bool corrupt,fail,authLeaked;public int latency;public Manifest staleLatest;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        if(request.Headers.Authorization!=null)authLeaked=true;
        byte[] data;
        if(request.RequestUri.ToString()==Channel.Latest||request.RequestUri.AbsolutePath.EndsWith("/manifest.json")) {
            bool isLatest=request.RequestUri.ToString()==Channel.Latest;if(isLatest)Interlocked.Increment(ref latest);else Interlocked.Increment(ref tagged);var m=isLatest&&staleLatest!=null?staleLatest:provider==null?manifest:provider();
            if(m==null)return new HttpResponseMessage(HttpStatusCode.NotFound){RequestMessage=request};
            data=Encoding.UTF8.GetBytes(Data.Json(m));
        } else {
            Interlocked.Increment(ref files);int n=Interlocked.Increment(ref active);lock(this){maxActive=Math.Max(maxActive,n);}
            try {
                if(latency>0)await Task.Delay(latency,token);
                if(fail)throw new HttpRequestException("simulated disconnect");
                data=blobs[request.RequestUri.ToString()];if(corrupt)data=Enumerable.Repeat((byte)88,data.Length).ToArray();
            } finally {Interlocked.Decrement(ref active);}
        }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data),RequestMessage=request};
    }
}
class ApiDouble:HttpMessageHandler {
    public Release published,current;public Manifest publishedManifest;public int creates,commits,uploads;public bool rejectUpload,loseCommitResponse,privateRepo,badDigest;public string user=Channel.Repo.Split('/' )[0];
    public int uploadLatency,activeUploads,maxUploads;public string releaseTitle,existingTag;public readonly List<DateTime> uploadStarts=new List<DateTime>();
    readonly Dictionary<string,byte[]> data=new Dictionary<string,byte[]>();public readonly List<string> order=new List<string>();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        string path=request.RequestUri.AbsolutePath;object result=null;
        if(request.Headers.Authorization==null)throw new Exception("Admin request missing token");
        if(path=="/user")result=new UserInfo {login=user};
        else if(path=="/repos/"+Channel.Repo)result=new RepoInfo {full_name=Channel.Repo,@private=privateRepo};
        else if(path.EndsWith("/releases/latest")){if(published==null)return new HttpResponseMessage(HttpStatusCode.NotFound);result=published;}
        else if(path.Contains("/releases/tags/")){string tag=path.Substring(path.LastIndexOf('/')+1);if(tag==existingTag)result=new Release {tag_name=tag};else if(published!=null&&published.tag_name==tag)result=published;else if(current!=null&&current.tag_name==tag)result=current;else if(publishedManifest!=null&&publishedManifest.files.Any(f=>f.url.Contains("/"+tag+"/")))result=new Release{id=-1,tag_name=tag};else return new HttpResponseMessage(HttpStatusCode.NotFound);}
        else if(request.Method==HttpMethod.Get&&path.EndsWith("/assets")){long id=Int64.Parse(path.Split('/').Reverse().Skip(1).First());result=current!=null&&current.id==id?current.assets:published!=null&&published.id==id&&published.assets!=null?published.assets:publishedManifest.files.GroupBy(f=>f.sha256).Select(g=>new ReleaseAsset{name=g.Key+".bin",size=g.First().size,digest="sha256:"+g.Key}).ToList();}
        else if(request.Method==HttpMethod.Post&&path.EndsWith("/releases")) {
            var dict=Data.Read<Dictionary<string,object>>(await request.Content.ReadAsStringAsync());
            if(!(bool)dict["draft"])throw new Exception("Release must start draft");
            releaseTitle=(string)dict["name"];creates++;order.Add("create-draft");current=new Release {id=creates,tag_name=(string)dict["tag_name"],draft=true,assets=new List<ReleaseAsset>()};result=current;
        } else if(request.RequestUri.Host=="uploads.github.com") {
            if(rejectUpload)return new HttpResponseMessage(HttpStatusCode.BadGateway);
            string name=Uri.UnescapeDataString(request.RequestUri.Query.Substring(6));
            int active=Interlocked.Increment(ref activeUploads);lock(this){maxUploads=Math.Max(maxUploads,active);uploadStarts.Add(DateTime.UtcNow);}
            if(uploadLatency>0&&name!="manifest.json")await Task.Delay(uploadLatency,token);
            byte[] b=await request.Content.ReadAsByteArrayAsync();
            string digest;using(var sha=SHA256.Create())digest=Data.Hex(sha.ComputeHash(b));
            var a=new ReleaseAsset{name=name,size=b.Length,digest="sha256:"+(badDigest?new string('0',64):digest)};
            lock(this){data[name]=b;current.assets.Add(a);uploads++;order.Add("upload-"+name);}Interlocked.Decrement(ref activeUploads);result=a;
        } else if(request.Method.Method=="PATCH") {
            if(!data.ContainsKey("manifest.json"))throw new Exception("Cannot publish before manifest");
            current.draft=false;published=current;publishedManifest=Data.Read<Manifest>(Encoding.UTF8.GetString(data["manifest.json"]));commits++;order.Add("publish");
            if(loseCommitResponse)throw new HttpRequestException("response lost");result=current;
        } else if(path.Contains("/releases/"))result=current;
        else throw new Exception("Unexpected API request "+path);
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Data.Json(result)),RequestMessage=request};
    }
}
class Tests {
    static int passed;static string runRoot;
    static string NewRoot() {
        string root=Path.Combine(runRoot,Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
        File.WriteAllText(Path.Combine(root,"Lethal Company.exe"),"fixture");File.WriteAllText(Path.Combine(root,"UnityPlayer.dll"),"fixture");File.WriteAllText(Path.Combine(root,"Lethal Company_Data","Managed","Assembly-CSharp.dll"),"fixture");return root;
    }
    static void Assert(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch{Assert(true,name);return;}throw new Exception("FAIL accepted: "+name);}
    static string Hash(byte[] b){using(var h=SHA256.Create())return Data.Hex(h.ComputeHash(b));}
    static PackFile F(Downloads d,string path,string content,string version="v1") {
        byte[] b=Encoding.UTF8.GetBytes(content);string h=Hash(b),url=Channel.Web+"/releases/download/"+version+"/"+h+".bin";d.blobs[url]=b;
        return new PackFile {path=path,sha256=h,size=b.Length,url=url};
    }
    static string Target(string root,string relative){return Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));}
    static void Put(string root,string relative,string content){string p=Target(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,content,new UTF8Encoding(false));}
    static UpdateResult Run(string root,Downloads d,Action guard=null,Action<string,string> versions=null){using(var t=new Transfer(d))return new Engine(root,guard??(()=>{})).Run(t,(s,p)=>{},versions).GetAwaiter().GetResult();}
    static void Main(string[] args) {
        if(args.Length>0&&args[0]=="--hold"){Thread.Sleep(60000);return;}
        try {
            runRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runs",DateTime.UtcNow.ToString("yyyyMMddHHmmss"));Directory.CreateDirectory(runRoot);
            if(args.Contains("--publisher")){Publishing();PublishingConcurrency();Console.WriteLine("Publisher regression checks passed.");return;}
            if(args.Contains("--badges")){Badges();Console.WriteLine("Badge regression checks passed.");return;}
            var timer=Stopwatch.StartNew();Paths();Updates();Retirement();Badges();MutableConfigs();Recovery();Publishing();PublishingConcurrency();GameProcess();timer.Stop();
            Console.WriteLine("RESULT: "+passed+" checks passed in "+timer.Elapsed.TotalSeconds.ToString("0.00")+" s; fixtures only; no real game files changed.");
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Badges() {
        string root=NewRoot(),statePath=Target(root,".lcpack-updater/state.json");
        var d=new Downloads();var a=F(d,"BepInEx/plugins/a.dll","a","v1");
        d.manifest=new Manifest{version="v1",files=new List<PackFile>{a}};Run(root,d);
        a=F(d,a.path,"a2","v2");d.manifest=new Manifest{version="v2",files=new List<PackFile>{a}};Run(root,d);
        var b=F(d,"BepInEx/plugins/b.dll","b","v2");d.manifest.files.Add(b);Run(root,d);
        Assert(Data.Load<LocalState>(statePath).updatedPaths.OrderBy(x=>x).SequenceEqual(new[]{a.path,b.path}),"same-version additions merge with existing NEW marks");
        // Reproduce an already-updated legacy client: current hashes but missing NEW entries.
        var state=Data.Load<LocalState>(statePath);
        var legacy=new {schema=1,version=state.version,files=state.files,updatedPaths=new[]{a.path}};
        Data.Atomic(statePath,Data.Json(legacy));d.files=0;Run(root,d);
        Assert(d.files==0&&Data.Load<LocalState>(statePath).updatedPaths.Contains(b.path),"legacy missing badge restored from verified backup with zero downloads");
        string previous=Data.Hash(statePath);Run(root,d);
        Assert(d.files==0&&Data.Hash(statePath)==previous,"latest check preserves badges without rewriting their meaning");
        var c=F(d,"BepInEx/plugins/c.dll","c","v2");d.manifest.files.Add(c);d.fail=true;
        Reject(()=>Run(root,d),"same-version failed download rejected");
        Assert(Data.Hash(statePath)==previous,"failed same-version update does not mark uninstalled MOD new");
        d.fail=false;d.manifest.files.Remove(c);d.manifest.version="v3";Run(root,d);
        Assert(Data.Load<LocalState>(statePath).updatedPaths.Count==0,"new version clears prior version badges when no files changed");
    }
    static void Paths() {
        string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);SafePath.NoLinks(desktop);Assert(Directory.Exists(desktop),"real Windows desktop / OneDrive path accepted");
        foreach(string p in new[]{"../evil.dll","BepInEx/../evil.dll","BepInEx/plugins/../../x","C:/x.dll","/BepInEx/plugins/a.dll","BepInEx\\plugins\\a.dll","BepInEx/plugins/a.dll:stream","BepInEx/plugins/CON.txt","BepInEx/plugins/aux","BepInEx/plugins/com1.dll","BepInEx/plugins/LPT².txt","BepInEx/plugins/a.","BepInEx/plugins/a ","BepInEx/plugins//a","BepInEx/plugins/./a","Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/test","BepInEx/LogOutput.log",".lcpack-updater/state.json"})Reject(()=>SafePath.Validate(p),"reject path "+p);
        Assert(SafePath.Validate("BepInEx/plugins/测试 MOD/a.dll").Contains("测试"),"Chinese paths allowed");
        var d=new Downloads();var f=F(d,"BepInEx/plugins/a.dll","a");
        Reject(()=>SafePath.ValidateManifest(new Manifest{version="v",files=new List<PackFile>{f,F(d,"bepinex/plugins/A.DLL","b")}}),"case duplicate");
        Reject(()=>SafePath.ValidateManifest(new Manifest{version="v",files=new List<PackFile>{f,F(d,"BepInEx/plugins/a.dll/b","b")}}),"file ancestor collision");
        Reject(()=>SafePath.ValidateManifest(new Manifest{version="v",files=new List<PackFile>{f},deletes=new List<string>{f.path}}),"file/delete conflict");
        Reject(()=>SafePath.DownloadUrl("https://evil.example/a"),"external download URL");
        Reject(()=>SafePath.DownloadUrl(Channel.Web+"/releases/download/../else/file"),"URL traversal normalization");
        Reject(()=>SafePath.DownloadUrl("http://github.com/"+Channel.Repo+"/releases/download/v/file"),"HTTP blocked");
        Reject(()=>SafePath.DownloadUrl(Channel.Web+"/releases/download/v/%2e%2e/evil"),"encoded URL blocked");
    }
    static void MutableConfigs() {
        string root=NewRoot();var initial=new Downloads();
        var config=F(initial,"BepInEx/config/Example.cfg","[General]\nPlayers = 50\n");var dll=F(initial,"BepInEx/plugins/a.dll","original DLL");
        initial.manifest=new Manifest{version="v1",files=new List<PackFile>{config,dll}};
        Put(root,config.path,"unmanaged preinstall config");Run(root,initial);
        Assert(File.ReadAllText(Target(root,config.path))=="unmanaged preinstall config","first managed install preserves existing personal config");
        string rewritten="# generated by MOD\n[General]\nPlayers = 6\nCache = local\n";Put(root,config.path,rewritten);
        var same=new Downloads{manifest=initial.manifest};same.blobs[config.url]=initial.blobs[config.url];var result=Run(root,same);
        Assert(result.latest&&same.files==0&&File.ReadAllText(Target(root,config.path))==rewritten,"runtime config rewrite causes zero downloads and preserves local settings");
        var next=new Downloads{manifest=new Manifest{version="v2",files=initial.manifest.files}};Run(root,next);
        Assert(next.files==0,"unchanged cloud config preserved across pack version bump");
        Put(root,dll.path,"corrupt DLL");var repair=new Downloads{manifest=next.manifest};repair.blobs[dll.url]=initial.blobs[dll.url];Run(root,repair);
        Assert(repair.files==1&&File.ReadAllText(Target(root,dll.path))=="original DLL","DLL tamper still repaired while configs preserved");
        var changed=new Downloads();var newConfig=F(changed,config.path,"[General]\nPlayers = 32\n","v3");changed.manifest=new Manifest{version="v3",files=new List<PackFile>{newConfig,dll}};Run(root,changed);
        Assert(changed.files==1&&File.ReadAllText(Target(root,config.path)).Contains("32"),"publisher config change still installed");
        Assert(Directory.GetFiles(Target(root,".lcpack-updater/backups"),"old-*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)==rewritten),"user config backed up before published replacement");
        File.Delete(Target(root,config.path));var missing=new Downloads{manifest=changed.manifest};missing.blobs[newConfig.url]=changed.blobs[newConfig.url];Run(root,missing);
        Assert(missing.files==1&&File.Exists(Target(root,config.path)),"missing config still restored");
    }
    static void Updates() {
        string root=NewRoot();var d=new Downloads();d.manifest=new Manifest{version="v1",files=new List<PackFile>{F(d,"BepInEx/plugins/a.dll","aaa"),F(d,"BepInEx/config/a.cfg","config")}};
        var seen=new List<Tuple<string,string>>();Action<string,string> report=(local,remote)=>seen.Add(Tuple.Create(local,remote));
        Put(root,"BepInEx/plugins/unrelated.dll","keep");var result=Run(root,d,versions:report);
        Assert(seen.Count==2&&String.IsNullOrEmpty(seen[0].Item1)&&seen[0].Item2==null&&seen[1].Item2=="v1"&&result.version=="v1","first install reports unknown local then cloud and installed version");
        Assert(result.downloaded==2&&File.ReadAllText(Target(root,"BepInEx/plugins/a.dll"))=="aaa","first install downloads missing files");
        Assert(Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).updatedPaths.Count==2,"first install remembers new files");
        Assert(!d.authLeaked,"client never sends publisher credentials");Assert(File.ReadAllText(Target(root,"BepInEx/plugins/unrelated.dll"))=="keep","unrelated file preserved");
        seen.Clear();var next=new Downloads{manifest=d.manifest};result=Run(root,next,versions:report);Assert(result.latest&&next.files==0,"latest check downloads zero files");
        Assert(Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).updatedPaths.Count==2,"NEW survives repeated latest checks");
        Assert(seen.Count==2&&seen[0].Item1=="v1"&&seen[0].Item2==null&&seen[1].Item1=="v1"&&seen[1].Item2=="v1"&&result.version=="v1","latest check reports verified installed version");
        Put(root,"BepInEx/plugins/a.dll","bbb");var repair=new Downloads{manifest=d.manifest};foreach(var p in d.blobs)repair.blobs[p.Key]=p.Value;Run(root,repair);Assert(repair.files==1,"same-size local modification detected by SHA256");
        seen.Clear();var inc=new Downloads();inc.manifest=new Manifest{version="v2",files=new List<PackFile>{F(inc,"BepInEx/plugins/a.dll","new-a","v2"),d.manifest.files[1]}};result=Run(root,inc,versions:report);Assert(result.downloaded==1&&inc.files==1,"incremental only downloads changed file");
        Assert(Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).updatedPaths.SequenceEqual(new[]{"BepInEx/plugins/a.dll"}),"next version marks only changed files");
        Assert(seen[1].Item1=="v1"&&seen[1].Item2=="v2"&&result.version=="v2","incremental reports old local and new remote until installed");
        Assert(Directory.GetFiles(Target(root,".lcpack-updater/backups"),"old-*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="aaa"),"old file backup retained");
        var deleted=new Downloads{manifest=new Manifest{version="v3",files=new List<PackFile>{inc.manifest.files[0]},deletes=new List<string>{"BepInEx/config/a.cfg","BepInEx/plugins/unrelated.dll"}}};result=Run(root,deleted);
        Assert(result.deleted==1&&!File.Exists(Target(root,"BepInEx/config/a.cfg")),"explicit managed deletion");Assert(result.skipped==1&&File.Exists(Target(root,"BepInEx/plugins/unrelated.dll")),"unmanaged explicit deletion skipped");
        Assert(Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).updatedPaths.Count==0,"deletion-only release clears prior NEW marks");
        Put(root,"BepInEx/plugins/a.dll","user edit");var remove=new Downloads{manifest=new Manifest{version="v4",deletes=new List<string>{"BepInEx/plugins/a.dll"}}};Reject(()=>Run(root,remove),"locally edited deletion blocked");Assert(File.ReadAllText(Target(root,"BepInEx/plugins/a.dll"))=="user edit","local edit not destroyed");
        seen.Clear();var bad=new Downloads{corrupt=true};bad.manifest=new Manifest{version="bad",files=new List<PackFile>{F(bad,"BepInEx/plugins/a.dll","expected")}};Reject(()=>Run(root,bad,versions:report),"corrupted download rejected");Assert(File.ReadAllText(Target(root,"BepInEx/plugins/a.dll"))=="user edit","hash failure leaves old file");
        Assert(seen[1].Item1=="v3"&&seen[1].Item2=="bad"&&Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).version=="v3","failed update cannot advance installed version");
        Assert(Data.Load<LocalState>(Target(root,".lcpack-updater/state.json")).updatedPaths.Count==0,"failed update leaves NEW records unchanged");
        var offline=new Downloads{fail=true};offline.manifest=bad.manifest;Reject(()=>Run(root,offline),"network failure rejected");Assert(File.ReadAllText(Target(root,"BepInEx/plugins/a.dll"))=="user edit","network failure leaves old file");
        var stop=new Downloads{manifest=d.manifest};Reject(()=>Run(root,stop,()=>{throw new IOException("game running");}),"running game blocks update");Assert(stop.latest==0&&stop.files==0,"running game blocked before network");
        var parallel=new Downloads{latency=80,manifest=new Manifest{version="parallel"}};for(int i=0;i<12;i++)parallel.manifest.files.Add(F(parallel,"BepInEx/plugins/"+i+".dll","payload-"+i));Run(NewRoot(),parallel);Assert(parallel.maxActive==4,"four concurrent downloads maximum");
        var empty=new Downloads();empty.manifest=new Manifest{version="empty",files=new List<PackFile>{F(empty,"BepInEx/config/empty.cfg","")}};Run(NewRoot(),empty);Assert(empty.files==1,"empty file checksum and install");
        string wrong=Path.Combine(runRoot,"wrong");Directory.CreateDirectory(wrong);Reject(()=>Run(wrong,new Downloads()),"wrong game directory rejected");Assert(!Directory.Exists(Path.Combine(wrong,".lcpack-updater")),"wrong directory not modified");
        string jroot=NewRoot(),outside=Path.Combine(runRoot,"outside");Directory.CreateDirectory(outside);Directory.CreateDirectory(Path.Combine(jroot,"BepInEx"));File.WriteAllText(Path.Combine(outside,"safe.txt"),"safe");
        using(var p=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J \""+Path.Combine(jroot,"BepInEx","plugins")+"\" \""+outside+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){p.StandardOutput.ReadToEnd();p.WaitForExit();Assert(p.ExitCode==0,"junction fixture created");}
        var jd=new Downloads();jd.manifest=new Manifest{version="j",files=new List<PackFile>{F(jd,"BepInEx/plugins/safe.txt","unsafe")}};Reject(()=>Run(jroot,jd),"junction target blocked");Assert(File.ReadAllText(Path.Combine(outside,"safe.txt"))=="safe","junction cannot escape game root");
    }
    static void Retirement() {
        foreach(bool managed in new[]{false,true}) {
            string root=NewRoot();var d=new Downloads();
            d.manifest=new Manifest{version="old",files=RetiredFiles.Snatch.Select(p=>F(d,p,"old")).ToList()};
            if(managed)Run(root,d);
            foreach(string p in RetiredFiles.Snatch)Put(root,p,"personal contents");
            const string shared="BepInEx/plugins/Lutschlon-EverythingCanDieAlternative/ui_images/SNATCHER.jpg";
            Put(root,shared,"keep shared");Put(root,"BepInEx/core/BepInEx.dll","keep dependency");
            d.manifest=new Manifest{version="retired",deletes=RetiredFiles.Snatch.ToList()};
            var r=Run(root,d);
            Assert(r.deleted==5&&r.skipped==0&&RetiredFiles.Snatch.All(p=>!File.Exists(Target(root,p))),"retired files removed including edited config; managed="+managed);
            var backup=Directory.GetDirectories(Target(root,".lcpack-updater/backups")).Single(p=>Data.Load<Journal>(Path.Combine(p,"journal.json")).changes.Any(c=>c.newHash==null));
            var journal=Data.Load<Journal>(Path.Combine(backup,"journal.json"));
            Assert(journal.changes.Count==5&&journal.changes.All(c=>File.ReadAllText(Path.Combine(backup,c.backup))=="personal contents"),"retirement retains exact backups; managed="+managed);
            Assert(File.ReadAllText(Target(root,shared))=="keep shared"&&File.ReadAllText(Target(root,"BepInEx/core/BepInEx.dll"))=="keep dependency","shared files preserved");
            Assert(Run(root,d).deleted==0,"retirement idempotent");
        }
        string rollback=NewRoot();foreach(string p in RetiredFiles.Snatch)Put(rollback,p,"original");
        var remove=new Downloads{manifest=new Manifest{version="retired",deletes=RetiredFiles.Snatch.ToList()}};
        using(var locked=new FileStream(Target(rollback,RetiredFiles.Snatch[1]),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>Run(rollback,remove),"locked retired config aborts");
        Assert(RetiredFiles.Snatch.All(p=>File.ReadAllText(Target(rollback,p))=="original"),"partial retirement rolls back");
        Assert(RetiredFiles.Contains(RetiredFiles.Snatch[0].ToUpperInvariant())&&!RetiredFiles.Contains(RetiredFiles.Snatch[0]+".other"),"retirement exact case insensitive paths only");
        var unchanged=new Downloads{manifest=new Manifest{version="unchanged"}};Run(rollback,unchanged);
        Assert(RetiredFiles.Snatch.All(p=>File.Exists(Target(rollback,p))),"no deletion without manifest instruction");
    }
    static void Recovery() {
        string root=NewRoot();Put(root,"BepInEx/plugins/a.dll","old-a");Put(root,"BepInEx/plugins/b.dll","old-b");var d=new Downloads();d.manifest=new Manifest{version="v2",files=new List<PackFile>{F(d,"BepInEx/plugins/a.dll","new-a"),F(d,"BepInEx/plugins/b.dll","new-b")}};
        using(var locked=new FileStream(Target(root,"BepInEx/plugins/b.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>Run(root,d),"locked second file fails install");
        Assert(File.ReadAllText(Target(root,"BepInEx/plugins/a.dll"))=="old-a"&&File.ReadAllText(Target(root,"BepInEx/plugins/b.dll"))=="old-b","partial install automatically rolled back");
        Assert(!File.Exists(Target(root,".lcpack-updater/state.json")),"rollback restores absent prior state");
        string stateRoot=NewRoot();var seed=new Downloads();seed.manifest=new Manifest{version="seed",files=new List<PackFile>{F(seed,"BepInEx/plugins/a.dll","old-a"),F(seed,"BepInEx/plugins/b.dll","old-b")}};Run(stateRoot,seed);
        string statePath=Target(stateRoot,".lcpack-updater/state.json"),stateBefore=Data.Hash(statePath);File.SetAttributes(statePath,FileAttributes.ReadOnly);
        try {
            var rd=new Downloads();rd.manifest=new Manifest{version="new",files=new List<PackFile>{F(rd,"BepInEx/plugins/a.dll","new-a"),F(rd,"BepInEx/plugins/b.dll","new-b")}};
            using(var locked=new FileStream(Target(stateRoot,"BepInEx/plugins/b.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>Run(stateRoot,rd),"failure with readonly prior state rejected");
            Assert(Data.Hash(statePath)==stateBefore&&File.ReadAllText(Target(stateRoot,"BepInEx/plugins/a.dll"))=="old-a"&&!Directory.Exists(Target(stateRoot,".lcpack-updater/pending")),"rollback preserves unchanged readonly state");
        }finally{File.SetAttributes(statePath,FileAttributes.Normal);}
        string archiveRoot=NewRoot();Directory.CreateDirectory(Target(archiveRoot,".lcpack-updater"));File.WriteAllText(Target(archiveRoot,".lcpack-updater/backups"),"blocks folder creation");var ad=new Downloads();ad.manifest=new Manifest{version="archive",files=new List<PackFile>{F(ad,"BepInEx/plugins/a.dll","installed")}};
        var ar=Run(archiveRoot,ad);Assert(ar.backupPending&&File.ReadAllText(Target(archiveRoot,"BepInEx/plugins/a.dll"))=="installed"&&Data.Load<Journal>(Target(archiveRoot,".lcpack-updater/pending/journal.json")).committed,"archive failure keeps committed update and backup");
        string crash=NewRoot(),pending=Target(crash,".lcpack-updater/pending");Directory.CreateDirectory(pending);
        Put(crash,"BepInEx/plugins/a.dll","new-a");File.WriteAllText(Path.Combine(pending,"old-0"),"old-a",new UTF8Encoding(false));
        Put(crash,"BepInEx/plugins/new.dll","new");
        var journal=new Journal {changes=new List<Change>{new Change{path="BepInEx/plugins/a.dll",existed=true,oldHash=Hash(Encoding.UTF8.GetBytes("old-a")),newHash=Hash(Encoding.UTF8.GetBytes("new-a")),backup="old-0"},new Change{path="BepInEx/plugins/new.dll",existed=false,newHash=Hash(Encoding.UTF8.GetBytes("new")),backup="old-1"}}};
        Data.Atomic(Path.Combine(pending,"journal.json"),Data.Json(journal));
        var recovered=new Downloads();recovered.manifest=new Manifest{version="old",files=new List<PackFile>{F(recovered,"BepInEx/plugins/a.dll","old-a")}};Run(crash,recovered);
        Assert(File.ReadAllText(Target(crash,"BepInEx/plugins/a.dll"))=="old-a"&&!File.Exists(Target(crash,"BepInEx/plugins/new.dll")),"crash journal recovery restores old and removes newly created file");Assert(recovered.files==0,"crash recovery before new download planning");
        string committed=NewRoot();string cp=Target(committed,".lcpack-updater/pending");Directory.CreateDirectory(cp);Data.Atomic(Path.Combine(cp,"journal.json"),Data.Json(new Journal{committed=true}));var cd=new Downloads{manifest=new Manifest{version="ok"}};Run(committed,cd);Assert(!Directory.Exists(cp)&&Directory.GetDirectories(Target(committed,".lcpack-updater/backups")).Length==1,"committed journal archived without rollback");
        string sr=NewRoot();Directory.CreateDirectory(Target(sr,".lcpack-updater"));Data.Atomic(Target(sr,".lcpack-updater/state.json"),"{bad json");Reject(()=>Run(sr,new Downloads{manifest=new Manifest{version="x"}}),"corrupt local management record rejected");
    }
    static void Publishing() {
        var fresh=new Manifest{version="V1.5"};var cached=new Downloads{manifest=fresh,staleLatest=new Manifest{version="old-cached"}};
        using(var api=new GitHubApi("test",new ApiDouble{published=new Release{tag_name="V1.5"}}))using(var transfer=new Transfer(cached)){
            var remote=Publisher.Remote(api,transfer).GetAwaiter().GetResult();Assert(remote.version=="V1.5"&&cached.latest==0&&cached.tagged==1,"admin reads exact release manifest instead of stale latest redirect");
        }
        var catalogWs=new Workspace(Path.Combine(runRoot,"catalog-cache"));var catalogDownload=new Downloads();
        var catalogFiles=new[]{F(catalogDownload,"BepInEx/pack-info/original-files.json","[]"),F(catalogDownload,"BepInEx/pack-info/download-sources.json","[ ]"),F(catalogDownload,"BepInEx/plugins/untouched.dll","do not download")};
        catalogWs.Current.releaseVersion="pending-name";catalogWs.Current.deletes.Add("BepInEx/plugins/old.dll");catalogWs.Save();string pendingHash=Data.Hash(Path.Combine(catalogWs.Root,"draft.json"));
        using(var transfer=new Transfer(catalogDownload))catalogWs.CacheCatalog(catalogFiles,transfer).GetAwaiter().GetResult();
        Assert(catalogDownload.files==2&&catalogFiles.Take(2).All(f=>Data.Hash(catalogWs.ObjectPath(f.sha256))==f.sha256),"cold admin downloads and verifies only two catalog records");
        Assert(Data.Hash(Path.Combine(catalogWs.Root,"draft.json"))==pendingHash&&catalogWs.Current.releaseVersion=="pending-name"&&catalogWs.Current.deletes.Count==1,"catalog hydration preserves pending draft and deletions");
        using(var transfer=new Transfer(catalogDownload))catalogWs.CacheCatalog(catalogFiles,transfer).GetAwaiter().GetResult();Assert(catalogDownload.files==2,"valid catalog cache avoids repeat downloads");
        File.WriteAllText(catalogWs.ObjectPath(catalogFiles[0].sha256),"damaged");using(var transfer=new Transfer(catalogDownload))catalogWs.CacheCatalog(catalogFiles,transfer).GetAwaiter().GetResult();Assert(catalogDownload.files==3&&Data.Hash(catalogWs.ObjectPath(catalogFiles[0].sha256))==catalogFiles[0].sha256,"damaged catalog cache repaired by checksum");
        string draftPath=Path.Combine(catalogWs.Root,"draft.json");File.SetAttributes(draftPath,FileAttributes.ReadOnly);
        try{Reject(()=>catalogWs.SetRemote(fresh),"draft save failure reported");Assert(catalogWs.Current.releaseVersion=="pending-name"&&Data.Hash(draftPath)==pendingHash,"failed reload leaves in-memory and disk drafts consistent");}finally{File.SetAttributes(draftPath,FileAttributes.Normal);}
        Assert(Publisher.VersionName(" V1.5 ")=="V1.5"&&Publisher.VersionName("")=="","version name trims and supports automatic mode");
        foreach(string invalid in new[]{"../v1","v 1","v/1","v..1","v1.","v1.lock",new string('a',65),"v1\nunsafe"})Reject(()=>Publisher.VersionName(invalid),"invalid version rejected: "+invalid.Replace('\n',' '));
        string sourceRoot=NewRoot(),source=Target(sourceRoot,"BepInEx/plugins/sample.dll");Put(sourceRoot,"BepInEx/plugins/sample.dll","one");
        var ws=new Workspace(Path.Combine(runRoot,"publish"));ws.Add(source,"BepInEx/plugins/sample.dll");Assert(Workspace.Infer(source)=="BepInEx/plugins/sample.dll","drag infers game-relative path");Assert(Workspace.Infer(Path.Combine(sourceRoot,"BepInEx"))=="BepInEx","drag folder retains BepInEx");
        Assert(File.ReadAllText(source)=="one","publisher does not change source");var restored=new Workspace(ws.Root);Assert(restored.Current.files.Count==1,"pending list survives restart");
        ws.SaveToken("test-token-not-real");Assert(ws.Token()=="test-token-not-real"&&!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(ws.Root,"credential.dat"))).Contains("test-token-not-real"),"publisher credential protected with Windows DPAPI");
        var apiHandler=new ApiDouble();var down=new Downloads{provider=()=>apiHandler.publishedManifest};
        ws.Current.releaseVersion="V1.5";ws.Save();Assert(new Workspace(ws.Root).Current.releaseVersion=="V1.5","pending version survives restart");Manifest first;
        using(var api=new GitHubApi("test-token",apiHandler))using(var transfer=new Transfer(down))first=Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();
        Assert(apiHandler.creates==1&&apiHandler.commits==1&&apiHandler.uploads==2,"publish uploads changed blob and manifest then commits");Assert(apiHandler.order[0]=="create-draft"&&apiHandler.order.Last()=="publish","incomplete release never visible");
        Assert(first.version=="V1.5"&&apiHandler.current.tag_name=="V1.5"&&apiHandler.releaseTitle=="LethalGoofballsPack V1.5"&&first.files[0].url.Contains("/V1.5/"),"custom name reaches manifest tag title and asset URL");Assert(String.IsNullOrEmpty(ws.Current.releaseVersion),"successful publish clears pending version");
        var api2=new ApiDouble{published=new Release{tag_name=first.version},publishedManifest=first};var down2=new Downloads{provider=()=>api2.publishedManifest};
        using(var api=new GitHubApi("test",api2))using(var transfer=new Transfer(down2))Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();Assert(api2.creates==0&&api2.uploads==0,"unchanged publish uploads nothing");
        Put(sourceRoot,"BepInEx/config/new.cfg","two");ws.Add(Target(sourceRoot,"BepInEx/config/new.cfg"),"BepInEx/config/new.cfg");
        var api3=new ApiDouble{published=new Release{tag_name=first.version},publishedManifest=first};var down3=new Downloads{provider=()=>api3.publishedManifest};Manifest second;
        using(var api=new GitHubApi("test",api3))using(var transfer=new Transfer(down3))second=Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();Assert(api3.uploads==2&&second.files[0].url==first.files[0].url,"publisher reuses unchanged release asset");
        ws.Current.releaseVersion="V1.6";var renameApi=new ApiDouble{published=new Release{tag_name=second.version},publishedManifest=second};Manifest renamed;
        using(var api=new GitHubApi("test",renameApi))using(var transfer=new Transfer(new Downloads{provider=()=>renameApi.publishedManifest}))renamed=Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();
        Assert(renamed.version=="V1.6"&&renameApi.uploads==1&&renamed.files.Select(f=>f.url).SequenceEqual(second.files.Select(f=>f.url)),"version-only release uploads manifest and reuses every MOD");
        ws.Current.releaseVersion="V1.6";var noChange=new ApiDouble{published=new Release{tag_name=renamed.version},publishedManifest=renamed};using(var api=new GitHubApi("test",noChange))using(var transfer=new Transfer(new Downloads{provider=()=>noChange.publishedManifest}))Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();Assert(noChange.creates==0,"identical named version is a no-op");
        ws.Current.releaseVersion="V1.5";var duplicate=new ApiDouble{published=new Release{tag_name=renamed.version},publishedManifest=renamed,existingTag="V1.5"};using(var api=new GitHubApi("test",duplicate))using(var transfer=new Transfer(new Downloads{provider=()=>duplicate.publishedManifest}))Reject(()=>Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult(),"historical version name cannot be overwritten");Assert(duplicate.creates==0&&duplicate.uploads==0&&ws.Current.baseVersion=="V1.6","duplicate name fails before cloud mutations");
        ws.Remove(new[]{"BepInEx/plugins/sample.dll"});Assert(ws.Current.deletes.Contains("BepInEx/plugins/sample.dll")&&File.ReadAllText(source)=="one","explicit deletion marks manifest only");
        var failWs=new Workspace(Path.Combine(runRoot,"publish-fail"));failWs.Add(source,"BepInEx/plugins/sample.dll");var failApi=new ApiDouble{rejectUpload=true};
        using(var api=new GitHubApi("test",failApi))using(var transfer=new Transfer(new Downloads()))Reject(()=>Publisher.Publish(failWs,api,transfer,s=>{}).GetAwaiter().GetResult(),"upload failure reported");Assert(failApi.commits==0&&failWs.Current.baseVersion=="","failed upload cannot promote release or change local base");
        var badDigestApi=new ApiDouble{badDigest=true};using(var api=new GitHubApi("test",badDigestApi))using(var transfer=new Transfer(new Downloads()))Reject(()=>Publisher.Publish(failWs,api,transfer,s=>{}).GetAwaiter().GetResult(),"server upload checksum mismatch rejected");Assert(badDigestApi.commits==0,"bad upload digest never published");
        var lostWs=new Workspace(Path.Combine(runRoot,"publish-lost"));lostWs.Add(source,"BepInEx/plugins/sample.dll");var lostApi=new ApiDouble{loseCommitResponse=true};
        using(var api=new GitHubApi("test",lostApi))using(var transfer=new Transfer(new Downloads()))Publisher.Publish(lostWs,api,transfer,s=>{}).GetAwaiter().GetResult();Assert(lostWs.Current.baseVersion==lostApi.published.tag_name,"lost publish response reconciled via release state");
        var staleApi=new ApiDouble{published=new Release{tag_name="elsewhere"},publishedManifest=new Manifest{version="elsewhere"}};
        using(var api=new GitHubApi("test",staleApi))using(var transfer=new Transfer(new Downloads{provider=()=>staleApi.publishedManifest}))Reject(()=>Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult(),"stale publisher refuses overwriting another release");
        using(var api=new GitHubApi("test",new ApiDouble{user="other"}))Reject(()=>api.ValidateAccount().GetAwaiter().GetResult(),"other publisher account blocked");
        using(var api=new GitHubApi("test",new ApiDouble{privateRepo=true}))Reject(()=>api.ValidateAccount().GetAwaiter().GetResult(),"private repo cannot break anonymous downloads");
    }
    static void PublishingConcurrency() {
        var ws=new Workspace(Path.Combine(runRoot,"publish-concurrent"));string source=NewRoot();
        for(int i=0;i<8;i++){string relative="BepInEx/plugins/test-"+i+".dll";Put(source,relative,"payload-"+i);ws.Add(Target(source,relative),relative);}
        var handler=new ApiDouble{uploadLatency=3900};
        using(var api=new GitHubApi("test",handler))using(var transfer=new Transfer(new Downloads()))Publisher.Publish(ws,api,transfer,s=>{}).GetAwaiter().GetResult();
        Assert(handler.maxUploads==4,"publisher uploads four files concurrently, never more");
        Assert(handler.uploadStarts.Zip(handler.uploadStarts.Skip(1),(a,b)=>(b-a).TotalMilliseconds).All(ms=>ms>=800),"all parallel upload starts share one global rate limit");
        Assert(handler.order[handler.order.Count-2]=="upload-manifest.json"&&handler.order.Last()=="publish"&&handler.uploads==9,"manifest and promotion wait for all concurrent uploads");
        Assert(handler.current.assets.Count==9&&handler.current.assets.Select(a=>a.name).Distinct().Count()==9,"parallel publication does not lose or duplicate assets");
    }
    static void GameProcess() {
        string root=NewRoot(),exe=Path.Combine(root,"Lethal Company.exe");File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location,exe,true);
        using(var process=Process.Start(new ProcessStartInfo(exe,"--hold"){UseShellExecute=false,CreateNoWindow=true})) {
            try {Thread.Sleep(200);Reject(Engine.CheckGameStopped,"actual named game process detected");}finally{if(!process.HasExited)process.Kill();process.WaitForExit();}
        }
    }
}

