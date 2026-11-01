using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class ClientFeed:HttpMessageHandler {
    public string text;public byte[] payload;public int downloads;public bool corrupt,offline;public string query;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        if(request.Headers.Authorization!=null)throw new Exception("Credentials leaked");
        if(offline)throw new HttpRequestException("offline fixture");
        bool feed=request.RequestUri.GetLeftPart(UriPartial.Path)==SelfUpdate.Feed;if(!feed)downloads++;else query=request.RequestUri.Query;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=feed?(HttpContent)new StringContent(text):new ByteArrayContent(corrupt?new byte[payload.Length]:payload)});
    }
}
class SelfUpdateTests {
    static int passed;static string dir,candidate,runner;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    static void Reject(Action action,string text){try{action();}catch{Check(true,text);return;}throw new Exception("Accepted: "+text);}
    static string Root() {
        string root=Path.Combine(dir,"游戏 with spaces "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
        foreach(string file in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,file),"fixture");
        File.Copy(candidate,Path.Combine(root,Channel.ClientName));Directory.CreateDirectory(SelfUpdate.Home(root));return root;
    }
    static void Prepare(string root,string exe) {
        var feed=new ClientFeed{payload=File.ReadAllBytes(exe)};
        using(var transfer=new Transfer(feed))SelfUpdate.Prepare(root,SelfUpdate.Describe(exe),transfer,(s,p)=>{}).GetAwaiter().GetResult();
    }
    static Process ReadyChild(string path,string args){return Process.Start(new ProcessStartInfo(runner,"--ready-child "+args){UseShellExecute=false,CreateNoWindow=true});}
    static void SetState(string root,ClientRelease active,ClientRelease previous=null){Data.Atomic(Path.Combine(SelfUpdate.Home(root),"current.json"),Data.Json(new ClientState{active=active,previous=previous}));}
    static void Main(string[] args) {
        if(args.Length==1&&args[0]=="--child-fail"){Environment.ExitCode=7;return;}
        if(args.Length==5&&args[0]=="--ready-child") {SelfUpdate.Ready(Encoding.UTF8.GetString(Convert.FromBase64String(args[2])),args[3]);Thread.Sleep(1500);return;}
        try {
            candidate=args[0];runner=Assembly.GetExecutingAssembly().Location;dir=Path.Combine(Path.GetTempPath(),"LcPackSelfTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var release=SelfUpdate.Describe(candidate);SelfUpdate.Validate(release);Check(!SelfUpdate.IsNewer(release),"same version ignored");
            release.version="1.4.0.0";Check(!SelfUpdate.IsNewer(release),"no downgrade");release.version="99.0.0.0";Check(SelfUpdate.IsNewer(release),"new version selected");
            release=SelfUpdate.Describe(candidate);release.file.url="https://evil.example/a.exe";Reject(()=>SelfUpdate.Validate(release),"untrusted executable host blocked");
            release=SelfUpdate.Describe(candidate);release.file.path="../other.exe";Reject(()=>SelfUpdate.Validate(release),"arbitrary target blocked");
            release=SelfUpdate.Describe(candidate);release.file.size=11*1024*1024;Reject(()=>SelfUpdate.Validate(release),"oversized executable rejected");
            release=SelfUpdate.Describe(candidate);string root=Root(),home=SelfUpdate.Home(root),target=Path.Combine(root,Channel.ClientName),hash=Data.Hash(target);
            Check(!home.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&home.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),"runtime cache is outside the game directory");
            Check(SelfUpdate.Home(root+Path.DirectorySeparatorChar)==home&&SelfUpdate.Home(root.ToLowerInvariant())==home,"equivalent game paths share one cache");
            Reject(()=>SelfUpdate.Ready(root,"../../outside"),"startup ticket traversal blocked");
            var feed=new ClientFeed{text=Data.Json(release)};
            using(var transfer=new Transfer(feed))Check(!SelfUpdate.Check(root,transfer,(s,p)=>{}).GetAwaiter().GetResult()&&feed.downloads==0&&feed.query.StartsWith("?check="),"latest check: zero binary downloads and cache-busted feed");
            feed=new ClientFeed{text=new string('x',16385)};using(var transfer=new Transfer(feed))Reject(()=>transfer.ClientLatest().GetAwaiter().GetResult(),"oversized feed rejected");
            feed=new ClientFeed{payload=File.ReadAllBytes(candidate)};
            using(var transfer=new Transfer(feed)) {
                SelfUpdate.Prepare(root,release,transfer,(s,p)=>{}).GetAwaiter().GetResult();SelfUpdate.ValidateRuntime(root,SelfUpdate.CachedExe(root,release));
                Check(feed.downloads==1,"verified download and runtime metadata ready");
                SelfUpdate.Prepare(root,release,transfer,(s,p)=>{}).GetAwaiter().GetResult();Check(feed.downloads==1,"valid runtime reused without another download");
                File.Delete(Path.Combine(Path.GetDirectoryName(SelfUpdate.CachedExe(root,release)),"release.json"));
                SelfUpdate.Prepare(root,release,transfer,(s,p)=>{}).GetAwaiter().GetResult();SelfUpdate.ValidateRuntime(root,SelfUpdate.CachedExe(root,release));Check(feed.downloads==1,"interrupted metadata write repaired without redownloading");
            }
            var other=SelfUpdate.Describe(runner);Prepare(root,runner);SetState(root,release);string stateFile=Path.Combine(home,"current.json"),before=Data.Hash(stateFile);
            Reject(()=>SelfUpdate.Activate(root,other,release,true,(p,a)=>{throw new IOException("launch fixture");}),"launch failure rejected");Check(Data.Hash(stateFile)==before,"launch failure keeps known-good pointer");
            Reject(()=>SelfUpdate.Activate(root,other,release,true,(p,a)=>Process.Start(new ProcessStartInfo(runner,"--child-fail"){UseShellExecute=false,CreateNoWindow=true})),"startup crash rejected");Check(Data.Hash(stateFile)==before,"startup crash keeps known-good pointer");
            using(var locked=new FileStream(stateFile,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>SelfUpdate.Activate(root,other,release,true,ReadyChild),"pointer write failure rejects transition");Check(Data.Hash(stateFile)==before,"failed commit keeps exact previous state");
            File.SetAttributes(target,FileAttributes.ReadOnly);
            try {
                using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))SelfUpdate.Activate(root,other,release,true,ReadyChild);
                Check(SelfUpdate.State(root).active.file.sha256==other.file.sha256&&SelfUpdate.State(root).previous.file.sha256==release.file.sha256,"locked and readonly game-directory EXE permits cache update");
                Check(Data.Hash(target)==hash&&(File.GetAttributes(target)&FileAttributes.ReadOnly)!=0,"launcher bytes and readonly attribute unchanged");
            }finally{File.SetAttributes(target,FileAttributes.Normal);}
            SelfUpdate.Bootstrap(root,target,(p,a)=>{if(p==SelfUpdate.CachedExe(root,other))throw new IOException("broken active fixture");return ReadyChild(p,a);});
            Check(SelfUpdate.State(root).active.file.sha256==release.file.sha256,"failed active runtime falls back to previous version");
            File.WriteAllText(Path.Combine(home,"download-interrupted.exe"),"partial");
            SelfUpdate.Bootstrap(root,target,ReadyChild);Check(SelfUpdate.State(root).active.file.sha256==release.file.sha256,"incomplete download ignored after interruption");
            File.WriteAllText(stateFile,"{");SelfUpdate.Bootstrap(root,target,ReadyChild);Check(SelfUpdate.State(root).active.file.sha256==release.file.sha256,"corrupt pointer recovers from original launcher");
            File.WriteAllText(SelfUpdate.CachedExe(root,release),"corrupt");SelfUpdate.Bootstrap(root,target,ReadyChild);Check(Data.Hash(SelfUpdate.CachedExe(root,release))==hash,"corrupt cache repaired from original launcher");
            Reject(()=>SelfUpdate.ValidateRuntime(root,candidate),"runtime cannot execute from arbitrary path");
            release.version="99.0.0.0";Reject(()=>SelfUpdate.Verify(candidate,release),"assembly version must match manifest");release=SelfUpdate.Describe(candidate);
            string badRoot=Root();feed=new ClientFeed{payload=File.ReadAllBytes(candidate),corrupt=true};using(var transfer=new Transfer(feed))Reject(()=>SelfUpdate.Prepare(badRoot,release,transfer,(s,p)=>{}).GetAwaiter().GetResult(),"bad download SHA rejected");
            Check(!File.Exists(SelfUpdate.CachedExe(badRoot,release))&&SelfUpdate.State(badRoot).active==null,"corrupt download never activated");
            feed=new ClientFeed{offline=true};using(var transfer=new Transfer(feed))Reject(()=>SelfUpdate.Prepare(badRoot,release,transfer,(s,p)=>{}).GetAwaiter().GetResult(),"interrupted network preserves previous state");
            Check(Data.Hash(target)==hash&&File.ReadAllText(Path.Combine(root,"Lethal Company.exe"))=="fixture","game files and original launcher unchanged throughout");
            string legacy=Path.Combine(root,".lcpack-updater","client-update",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(root,".lcpack-updater","update.lock"),"");SelfUpdate.FinishLegacy(root,Path.GetFileName(legacy));Check(File.Exists(Path.Combine(legacy,"started")),"legacy handoff acknowledgement retained");
            Console.WriteLine("RESULT: "+passed+" self-update checks passed; isolated fixtures and simulated startup acknowledgements.");
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
