using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LcPack {
public partial class Transfer {
    public async Task<ClientRelease> ClientLatest() {
        if(Cloud.Enabled){var current=Data.Read<ClientRelease>(await CloudDocument("client").ConfigureAwait(false));SelfUpdate.Validate(current);return current;}
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        using(var response=await client.GetAsync(SelfUpdate.Feed+"?check="+Guid.NewGuid().ToString("N"),HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
            Check(response);
            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using(var output=new MemoryStream()) {
                byte[] buffer=new byte[4096];int count;
                while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0){if(output.Length+count>16384)throw new InvalidDataException("接收端更新清单过大。");output.Write(buffer,0,count);}
                var release=Data.Read<ClientRelease>(Encoding.UTF8.GetString(output.ToArray()));SelfUpdate.Validate(release);return release;
            }
        }
    }
}
public class ClientRelease { public int schema=1; public string version; public PackFile file; }
public class ClientState { public ClientRelease active; public ClientRelease previous; }
public static class SelfUpdate {
    public static string Version {get{return Assembly.GetExecutingAssembly().GetName().Version.ToString();}}
    // Earlier releases keep their original four-part GitHub tags.
    public static string ReleaseLabel(string version) {
        var v=new System.Version(version);
        return v>=new System.Version(1,16,0,0) && v.Build==0 && v.Revision==0 ? v.ToString(2) : version;
    }
    public const string Feed="https://raw.githubusercontent.com/"+Channel.Repo+"/main/client-update.json";
    public static void Validate(ClientRelease release) {
        System.Version parsed;
        if(release==null||release.schema!=1||!System.Version.TryParse(release.version,out parsed)||release.file==null||release.file.path!=Channel.ClientName||release.file.size<1024||release.file.size>10*1024*1024||!Regex.IsMatch(release.file.sha256??"","^[a-f0-9]{64}$"))throw new InvalidDataException("接收端更新清单无效。");
        SafePath.DownloadUrl(release.file.url);
    }
    public static bool IsNewer(ClientRelease release) {Validate(release);return new System.Version(release.version)>new System.Version(Version);}
    public static string Home(string root) {
        string full=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(),key;
        using(var hash=SHA256.Create())key=Data.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(full))).Substring(0,32);
        string home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LethalGoofballsPackUpdater",key);
        SafePath.NoLinks(home);return home;
    }
    static string Local(string root,string name) {string path=Path.Combine(Home(root),name);SafePath.NoLinks(path);return path;}
    public static string CachedExe(string root,ClientRelease release) {Validate(release);return Local(root,Path.Combine("clients",release.file.sha256,"client.exe"));}
    public static void Verify(string path,ClientRelease release) {
        Validate(release);SafePath.NoLinks(path);
        if(new FileInfo(path).Length!=release.file.size||Data.Hash(path)!=release.file.sha256||AssemblyName.GetAssemblyName(path).Version!=new System.Version(release.version))throw new IOException("接收端文件校验失败："+path);
    }
    public static ClientRelease Describe(string exe) {
        string version=AssemblyName.GetAssemblyName(exe).Version.ToString(),hash=Data.Hash(exe);
        return new ClientRelease{version=version,file=new PackFile{path=Channel.ClientName,size=new FileInfo(exe).Length,sha256=hash,url=Channel.Web+"/releases/download/updater-v"+ReleaseLabel(version)+"/"+hash+".bin"}};
    }
    static FileStream Lock(string root) {
        Directory.CreateDirectory(Home(root));return new FileStream(Local(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    public static ClientState State(string root) {
        string path=Local(root,"current.json");if(!File.Exists(path))return new ClientState();
        try {
            if(new FileInfo(path).Length>32768)throw new IOException("接收端本地记录过大。");
            var state=Data.Load<ClientState>(path);if(state==null)throw new IOException("接收端本地记录无效。");
            if(state.active!=null)Validate(state.active);if(state.previous!=null)Validate(state.previous);return state;
        }catch(Exception e){if(!(e is IOException)&&!(e is ArgumentException)&&!(e is InvalidOperationException))throw;Record(root,e);return new ClientState();}
    }
    static void Store(string root,string temporary,ClientRelease release) {
        Verify(temporary,release);string target=CachedExe(root,release);Directory.CreateDirectory(Path.GetDirectoryName(target));
        Data.Replace(temporary,target);Data.Atomic(Path.Combine(Path.GetDirectoryName(target),"release.json"),Data.Json(release));
    }
    static bool Available(string root,ClientRelease release) {
        try {
            string exe=CachedExe(root,release);Verify(exe,release);
            string metadata=Path.Combine(Path.GetDirectoryName(exe),"release.json");SafePath.NoLinks(metadata);
            string json=Data.Json(release);if(!File.Exists(metadata)||File.ReadAllText(metadata)!=json)Data.Atomic(metadata,json);
            return true;
        }catch(IOException){return false;}
    }
    public static async Task Prepare(string root,ClientRelease release,Transfer transfer,Action<string,int> progress) {
        if(Available(root,release))return;
        string temporary=Local(root,"download-"+Guid.NewGuid().ToString("N")+".exe");long received=0;
        try {
            progress("正在选择下载线路……",-1);
            await transfer.PrepareDownload(release.file).ConfigureAwait(false);
            progress("正在连接下载源……",-1);
            await transfer.Download(release.file,temporary,n=>{
                received+=n;int percent=(int)Math.Min(100,Math.Max(0,received*100/release.file.size));
                if(received<=0)progress("正在重新连接下载源……",-1);
                else progress("正在更新接收端…… "+(percent==0?"不足 1%":percent+"%"),percent);
            }).ConfigureAwait(false);
            Store(root,temporary,release);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static string Ticket(string root,string id) {
        if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("接收端启动凭据无效。");
        return Local(root,"ready-"+id);
    }
    public static void Ready(string root,string id) {using(var self=Process.GetCurrentProcess())Data.Atomic(Ticket(root,id),self.Id.ToString());}
    public static void ValidateRuntime(string root,string exe) {
        Engine.CheckGameDirectory(root);string metadata=Path.Combine(Path.GetDirectoryName(exe),"release.json");SafePath.NoLinks(metadata);
        if(!File.Exists(metadata)||new FileInfo(metadata).Length>16384)throw new IOException("接收端缓存记录无效。");
        var release=Data.Load<ClientRelease>(metadata);
        if(!String.Equals(Path.GetFullPath(exe),CachedExe(root,release),StringComparison.OrdinalIgnoreCase))throw new IOException("接收端缓存路径无效。");
        Verify(exe,release);
    }
    public static void WaitForCache(string root) {
        for(int i=0;;i++){try{using(var file=Lock(root))return;}catch(IOException){if(i==200)throw;Thread.Sleep(50);}}
    }
    public static Mutex Instance(string root) {
        var mutex=new Mutex(false,"Local\\LethalGoofballsPackClient-"+Path.GetFileName(Home(root)));
        try {try{if(!mutex.WaitOne(10000))throw new IOException("接收端已在运行，请使用已打开的窗口。");}catch(AbandonedMutexException){}return mutex;}
        catch{mutex.Dispose();throw;}
    }
    public static void Activate(string root,ClientRelease release,ClientRelease previous,bool updated,Func<string,string,Process> launch) {
        string exe=CachedExe(root,release);Verify(exe,release);
        if(new System.Version(release.version)<new System.Version("1.10.0.0"))throw new IOException("该版本不支持缓存启动。");
        string id=Guid.NewGuid().ToString("N"),ready=Ticket(root,id);Process child=null;
        try {
            child=launch(exe,"--run-client "+Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(root)))+" "+id+" "+(updated?"updated":"check"));
            if(child==null)throw new IOException("新版接收端未能启动。");
            for(int i=0;i<200;i++) {
                if(child.HasExited)throw new IOException("接收端启动失败。");
                if(File.Exists(ready)&&File.ReadAllText(ready)==child.Id.ToString()) {
                    var state=State(root);
                    if(state.active==null||state.active.file.sha256!=release.file.sha256)Data.Atomic(Local(root,"current.json"),Data.Json(new ClientState{active=release,previous=previous}));
                    return;
                }
                Thread.Sleep(50);
            }
            throw new IOException("接收端启动超时，已保留旧版。");
        }catch{if(child!=null&&!child.HasExited){child.Kill();child.WaitForExit(5000);}throw;}
        finally{if(child!=null)child.Dispose();if(File.Exists(ready))File.Delete(ready);}
    }
    public static void Bootstrap(string root,string exe,Func<string,string,Process> launch) {
        // Bootstrap writes only the verified per-user receiver cache, never game resources.
        Engine.CheckGameDirectory(root);
        using(var cacheLock=Lock(root)) {
            var state=State(root);var seed=Describe(exe);
            // Normal bootstrap changes only verified cache entries and this small pointer.
            if(state.active!=null&&new System.Version(state.active.version)>=new System.Version(seed.version)) {
                try{Activate(root,state.active,state.previous,false,launch);return;}catch(Exception e){Record(root,e);}
                if(state.previous!=null){try{Activate(root,state.previous,null,false,launch);return;}catch(Exception e){Record(root,e);}}
            }
            if(!Available(root,seed)) {
                string temporary=Local(root,"seed-"+Guid.NewGuid().ToString("N")+".exe");
                try{Data.CopyDurable(exe,temporary);Store(root,temporary,seed);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            Activate(root,seed,null,false,launch);
        }
    }
    public static async Task<bool> Check(string root,Transfer transfer,Action<string,int> progress) {
        Engine.CheckGameDirectory(root);Engine.CheckGameStopped();
        var release=await transfer.ClientLatest().ConfigureAwait(false);if(!IsNewer(release))return false;
        using(var cacheLock=Lock(root)) {
            await Prepare(root,release,transfer,progress).ConfigureAwait(false);Engine.CheckGameStopped();
            var current=Describe(Assembly.GetExecutingAssembly().Location);
            Activate(root,release,current,true,(path,args)=>Process.Start(new ProcessStartInfo(path,args){UseShellExecute=false,WorkingDirectory=root}));return true;
        }
    }
    public static async Task RefreshLegacyLauncher(string root) {
        Engine.CheckGameDirectory(root);Engine.CheckGameStopped();
        string target=Path.Combine(root,Channel.ClientName),source=Assembly.GetExecutingAssembly().Location;
        if(!File.Exists(target)||AssemblyName.GetAssemblyName(target).Version>=new System.Version("1.14.0.0"))return;
        SafePath.NoLinks(target);var release=Describe(source);Verify(source,release);
        string backup=Local(root,Path.Combine("launcher-backups",Data.Hash(target)+".exe"));Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))Data.CopyDurable(target,backup);
        string temporary=target+".new-"+Guid.NewGuid().ToString("N");
        try{
            Data.CopyDurable(source,temporary);Verify(temporary,release);
            // The old bootstrap parent exits just after acknowledging its cache child.
            for(int i=0;;i++){Engine.CheckGameStopped();try{Data.Replace(temporary,target);break;}catch(IOException){if(i==19)throw;}await Task.Delay(100);}
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static string Record(string root,Exception error) {
        string log=Local(root,"last-error.txt");
        try{Directory.CreateDirectory(Home(root));var details=new StringBuilder();foreach(System.Collections.DictionaryEntry entry in error.Data)details.AppendLine(entry.Key+": "+entry.Value);File.WriteAllText(log,"UTC: "+DateTime.UtcNow.ToString("o")+"\r\nGame: "+root+"\r\nClient: "+Version+"\r\n"+details+error);}catch(IOException){}catch(UnauthorizedAccessException){}
        return log;
    }
    // Keep the old one-time handoff so 1.5-1.9 can migrate through their existing update channel.
    public static void FinishLegacy(string root,string id) {
        if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("接收端更新路径无效。");
        string started=Path.Combine(root,".lcpack-updater","client-update",id,"started");SafePath.NoLinks(started);File.WriteAllText(started,"ok");
        string path=Path.Combine(root,".lcpack-updater","update.lock");SafePath.NoLinks(path);
        for(int i=0;;i++){try{using(var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))return;}catch(IOException){if(i==200)throw;Thread.Sleep(50);}}
    }
}
}
