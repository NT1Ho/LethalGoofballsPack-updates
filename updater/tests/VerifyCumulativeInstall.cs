using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class VerifyCumulativeInstall {
    sealed class Feed:HttpMessageHandler {
        public string folder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            string name=r.RequestUri.AbsolutePath.Split('/').Last();
            var p=Path.Combine(folder,name=="manifest.json"?"signed.json":name.Replace(".bin",""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=r,Content=new ByteArrayContent(File.ReadAllBytes(p))});
        }
    }
    static void Check(bool b,string msg){if(!b)throw new Exception(msg);Console.WriteLine("PASS "+msg);}
    static void Run(string data){
        var cfg=Data.Load<CloudConfig>(Path.Combine(data,"channel.json"));typeof(Cloud).GetMethod("Configure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{cfg});
        var old=Data.Load<Manifest>(Path.Combine(data,"before.json"));string game=Path.Combine(data,"game");Directory.CreateDirectory(Path.Combine(game,"Lethal Company_Data","Managed"));
        foreach(string p in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(game,p),"fixture");
        foreach(var f in old.files){string p=SafePath.Target(game,f.path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.Copy(Path.Combine(data,"old.bin"),p);}
        string home=Path.Combine(game,".lcpack-updater");Directory.CreateDirectory(home);string sp=Path.Combine(home,"state.json");Data.Atomic(sp,Data.Json(new LocalState{version=old.version,files=old.files}));
        string cfgPath=Path.Combine(game,@"BepInEx\config\a.cfg");Directory.CreateDirectory(Path.GetDirectoryName(cfgPath));File.WriteAllText(cfgPath,"personal");
        string save=Path.Combine(game,"save.private");File.WriteAllText(save,"keep");
        string changed=SafePath.Target(game,"BepInEx/plugins/A/feature.bin");bool fault=false;
        using(var t=new Transfer(new Feed{folder=data})){
            try{new Engine(game,()=>{if(!fault&&File.Exists(Path.Combine(home,"pending","journal.json"))&&File.ReadAllText(changed)!="older"){fault=true;throw new IOException("injected");}}).Run(t,(s,i)=>{}).GetAwaiter().GetResult();}
            catch(IOException ex){if(ex.Message!="injected")throw;}
            Check(fault&&File.ReadAllText(changed)=="older"&&Data.Load<LocalState>(sp).version==old.version&&!Directory.Exists(Path.Combine(home,"pending")),"official receiver rolls back interrupted installation");
            var result=new Engine(game,()=>{}).Run(t,(s,i)=>{}).GetAwaiter().GetResult();var final=t.Latest().GetAwaiter().GetResult();
            Check(result.version=="qa-new"&&final.files.All(f=>f.path=="BepInEx/config/a.cfg"||Data.Hash(SafePath.Target(game,f.path))==f.sha256)&&final.deletes.All(p=>!File.Exists(SafePath.Target(game,p))),"official receiver installs signed controlled release, replacements and deletions");
            Check(File.ReadAllText(cfgPath)=="personal"&&File.ReadAllText(save)=="keep","player config and private save preserved");
            Check(ModInventory.Scan(game).Any(r=>r.name=="A"&&r.version=="1.2.0")&&PageContent.ForRoot(game).History.Any(h=>h.Version==final.version),"official receiver reads published MOD version and history");
        }
    }
    static int Main(string[] args){try{Run(args[0]);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
