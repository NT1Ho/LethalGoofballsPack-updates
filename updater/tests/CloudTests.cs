using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class CloudTests {
    static int count;static CosAccount account;static string root;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
    static async Task Reject(Func<Task> work,string name){try{await work();}catch{Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Data.Hex(sha.ComputeHash(bytes));}
    static PackFile FileInfo(string name,byte[] bytes){var hash=Hash(bytes);return new PackFile{path="BepInEx/plugins/"+name,size=bytes.Length,sha256=hash,url=Channel.Web+"/releases/download/V1/"+hash+".bin"};}
    sealed class BrokenRead:MemoryStream {
        public bool hang;readonly TaskCompletionSource<int> pending=new TaskCompletionSource<int>();
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){if(!hang)pending.TrySetException(new IOException("transport disconnected"));return pending.Task;}
        protected override void Dispose(bool disposing){pending.TrySetException(new ObjectDisposedException("network"));base.Dispose(disposing);}
    }
    sealed class Public:HttpMessageHandler {
        public Manifest manifest;public ClientRelease release;public byte[] blob;public int github,downloads,auth;public bool offline,tamper,corrupt;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            bool cos=request.RequestUri.Host.EndsWith(".myqcloud.com"),probe=request.Headers.Range!=null;if(!cos&&!probe)github++;if(request.Headers.Contains("Authorization"))auth++;
            if(offline&&cos)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=request});
            byte[] bytes;if(request.RequestUri.AbsolutePath.EndsWith(".bin")){if(!probe)downloads++;bytes=corrupt?new byte[blob.Length]:blob;}
            else {string signed=release==null?CosSettings.Sign(account,"mods",Data.Json(manifest)):CosSettings.Sign(account,"client",Data.Json(release));if(tamper){var doc=Data.Read<SignedDocument>(signed);doc.payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(Data.Json(new Manifest{version="ATTACK"})));signed=Data.Json(doc);}bytes=Encoding.UTF8.GetBytes(signed);}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});
        }
    }
    sealed class Store:HttpMessageHandler {
        public Dictionary<string,byte[]> data=new Dictionary<string,byte[]>();public int puts;public bool failBlob;public bool unauthorized;public List<string> writes=new List<string>();readonly object gate=new object();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            string path=request.RequestUri.AbsolutePath;byte[] payload=request.Content==null?null:await request.Content.ReadAsByteArrayAsync();
            lock(gate) {
                if(request.Method!=HttpMethod.Get&&!request.Headers.Contains("Authorization"))unauthorized=true;
                byte[] existing;bool found=data.TryGetValue(path,out existing);
                var response=new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request};
                if(request.Method==HttpMethod.Put) {
                    if(failBlob&&path.Contains("/objects/"))return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=request};
                    if(request.Headers.Contains("x-cos-forbid-overwrite")&&found)return new HttpResponseMessage(HttpStatusCode.Conflict){RequestMessage=request};
                    using(var md5=MD5.Create()){if(!md5.ComputeHash(payload).SequenceEqual(request.Content.Headers.ContentMD5))throw new Exception("MD5 request mismatch");response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\""+Data.Hex(md5.ComputeHash(payload))+"\"");}
                    data[path]=payload;puts++;writes.Add(path);return response;
                }
                if(!found){response.StatusCode=HttpStatusCode.NotFound;return response;}
                response.Content=new ByteArrayContent(existing);response.Headers.Add("x-cos-meta-sha256",Hash(existing));return response;
            }
        }
    }
    static async Task Run(string candidate) {
        root=Path.Combine(Path.GetTempPath(),"lcpack-cos-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        bool disconnected=false;using(var broken=new BrokenRead())try{await Transfer.ReadNetwork(broken,new byte[1],CancellationToken.None);}catch(HttpRequestException){disconnected=true;}Check(disconnected,"body disconnect is eligible for network retry and fallback");
        bool timedOut=false;using(var stalled=new BrokenRead{hang=true})using(var timeout=new CancellationTokenSource(100))try{await Transfer.ReadNetwork(stalled,new byte[1],timeout.Token);}catch(TaskCanceledException){timedOut=true;}Check(timedOut,"stalled body is aborted even when stream ignores cancellation token");
        var ws=new Workspace(Path.Combine(root,"publisher"));account=CosSettings.Save(ws,"https://unit-test-1234567890.cos.ap-hongkong.myqcloud.com","TESTID","TESTKEY");
        Cloud.Validate(account.channel);Check(true,"legacy Hong Kong endpoint remains supported");
        var gz=new CloudConfig{endpoint="https://unit-test-1234567890.cos.ap-guangzhou.myqcloud.com",publicKey=account.channel.publicKey};
        Cloud.Validate(gz);Check(true,"Guangzhou endpoint accepted with existing signing key");
        await Reject(()=>Task.Run(()=>Cloud.Validate(new CloudConfig{endpoint=gz.endpoint+".evil.example",publicKey=gz.publicKey})),"lookalike COS domain rejected");
        Cloud.Configure(null);account=CosSettings.Save(ws,gz.endpoint,account.secretId,account.secretKey);
        Check(!File.ReadAllText(Path.Combine(ws.Root,"cos-credential.dat")).Contains("TESTKEY"),"COS credentials encrypted with Windows DPAPI");
        Check(CosSettings.Load(ws).secretKey=="TESTKEY","local credentials round trip");
        var again=CosSettings.Save(ws,account.channel.endpoint,"TESTID2","TESTKEY2");Check(again.signingKey==account.signingKey,"saving credentials preserves signing identity");account=again;
        string signed=CosSettings.Sign(account,"mods","{\"version\":\"V1\"}");Check(Cloud.Verify(signed,"mods").Contains("V1"),"RSA SHA256 signature verifies");
        await Reject(()=>Task.FromResult(Cloud.Verify(signed,"client")),"cross-purpose signature rejected");
        var doc=Data.Read<SignedDocument>(signed);doc.signature=Convert.ToBase64String(new byte[256]);await Reject(()=>Task.FromResult(Cloud.Verify(Data.Json(doc),"mods")),"modified signature rejected");
        await Reject(()=>Task.FromResult(Cloud.Verify("{}","mods")),"unsigned manifest rejected");
        using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;Cloud.Configure(new CloudConfig{endpoint=account.channel.endpoint,publicKey=rsa.ToXmlString(false)});await Reject(()=>Task.FromResult(Cloud.Verify(signed,"mods")),"wrong signing key rejected");}Cloud.Configure(account.channel);
        await Reject(()=>Task.FromResult(Cloud.Url("../secret")),"COS object path traversal rejected");
        string game=Path.Combine(root,"game");Directory.CreateDirectory(Path.Combine(game,"Lethal Company_Data","Managed"));File.WriteAllText(Path.Combine(game,"Lethal Company.exe"),"fixture");File.WriteAllText(Path.Combine(game,"UnityPlayer.dll"),"fixture");File.WriteAllText(Path.Combine(game,"Lethal Company_Data","Managed","Assembly-CSharp.dll"),"fixture");
        byte[] v1=Encoding.UTF8.GetBytes("first file"),v2=Encoding.UTF8.GetBytes("second file");
        var file=FileInfo("one.dll",v1);var manifest=new Manifest{version="V1",files=new List<PackFile>{file}};
        var handler=new Public{manifest=manifest,blob=v1};using(var transfer=new Transfer(handler)) {
            var engine=new Engine(game,()=>{});var initial=await engine.Run(transfer,(s,n)=>{});Check(initial.downloaded==1&&handler.github==0&&handler.auth==0,"first install uses COS only, no GitHub or credentials");
            int before=handler.downloads;var latest=await engine.Run(transfer,(s,n)=>{});Check(latest.latest&&handler.downloads==before,"latest check downloads no file");
            handler.manifest=new Manifest{version="V2",files=new List<PackFile>{FileInfo("one.dll",v2)}};handler.blob=v2;
            var next=await engine.Run(transfer,(s,n)=>{});Check(next.downloaded==1&&File.ReadAllBytes(Path.Combine(game,"BepInEx","plugins","one.dll")).SequenceEqual(v2),"COS incremental update installs changed file");
            handler.manifest=manifest;handler.blob=v1;handler.corrupt=true;
            await Reject(()=>engine.Run(transfer,(s,n)=>{}),"corrupt COS download blocked");Check(Data.Hash(Path.Combine(game,"BepInEx","plugins","one.dll"))==Hash(v2),"corrupt download preserves old file");Check(handler.github==0,"corruption never bypasses verification through fallback");handler.corrupt=false;
            handler.tamper=true;await Reject(()=>engine.Run(transfer,(s,n)=>{}),"tampered COS manifest blocked");Check(handler.github==0,"invalid signature cannot fall back to an unsigned source");handler.tamper=false;
            handler.offline=true;var fallback=await transfer.Latest();Check(fallback.version=="V1"&&handler.github==1,"network failure permits signed GitHub fallback");
        }
        var clientHandler=new Public{release=SelfUpdate.Describe(candidate),blob=File.ReadAllBytes(candidate)};
        Directory.CreateDirectory(SelfUpdate.Home(game));
        using(var transfer=new Transfer(clientHandler)) {
            var release=await transfer.ClientLatest();await SelfUpdate.Prepare(game,release,transfer,(s,n)=>{});SelfUpdate.Verify(SelfUpdate.CachedExe(game,release),release);
            Check(clientHandler.downloads==1&&clientHandler.github==0&&clientHandler.auth==0,"client self-update fetches signed feed and EXE through COS only");
            await SelfUpdate.Prepare(game,release,transfer,(s,n)=>{});Check(clientHandler.downloads==1,"verified cached client is reused");
            Cloud.Configure(null);SafePath.DownloadUrl(release.file.url);Check(release.file.url.StartsWith(Channel.Web),"cache metadata retains legacy launcher-compatible URLs");Cloud.Configure(account.channel);
            clientHandler.tamper=true;await Reject(()=>transfer.ClientLatest(),"tampered client feed rejected before executable download");
        }
        string source=Path.Combine(root,"source.dll");File.WriteAllBytes(source,v1);ws.Add(source,"BepInEx/plugins/one.dll");ws.Current.releaseVersion="V1";
        var store=new Store();using(var api=new CosApi(account,store))using(var transfer=new Transfer(store)) {
            var published=await CosPublisher.Publish(ws,account,api,transfer,s=>{});Check(published.version=="V1"&&!store.unauthorized,"publisher signs COS requests and publishes V1");
            Check(store.writes.Last()=="/lcpack/manifest.json"&&store.writes.First().Contains("/objects/"),"files uploaded before signed latest pointer");
            int before=store.puts;await CosPublisher.Publish(ws,account,api,transfer,s=>{});Check(store.puts==before,"unchanged publish writes nothing");
            byte[] originalPointer=store.data["/lcpack/manifest.json"];File.WriteAllBytes(source,v2);ws.Add(source,"BepInEx/plugins/one.dll");ws.Current.releaseVersion="V2";store.failBlob=true;
            await Reject(()=>CosPublisher.Publish(ws,account,api,transfer,s=>{}),"failed COS upload rejected");Check(store.data["/lcpack/manifest.json"].SequenceEqual(originalPointer)&&ws.Current.baseVersion=="V1","failed upload preserves published pointer and local draft");store.failBlob=false;
            await CosPublisher.Publish(ws,account,api,transfer,s=>{});Check(ws.Current.baseVersion=="V2","retry completes publication");
            int writes=store.puts;using(var stream=new MemoryStream(v2))await api.Put("objects/"+Hash(v2)+".bin",stream,"application/octet-stream",true);Check(store.puts==writes,"immutable duplicate confirmed without overwrite");
            ws.Current.releaseVersion="V1";await Reject(()=>CosPublisher.Publish(ws,account,api,transfer,s=>{}),"reusing old version for different content rejected");
        }
        Cloud.Configure(null);Console.WriteLine("RESULT: "+count+" COS checks passed; synthetic HTTP and isolated files only.");
    }
    static void Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
