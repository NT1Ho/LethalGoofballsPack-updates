using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LcPack {
public class CosAccount {public CloudConfig channel;public string secretId,secretKey,signingKey;}
public static class CosSettings {
    public static CosAccount Load(Workspace workspace) {
        string path=Path.Combine(workspace.Root,"cos-credential.dat");SafePath.NoLinks(path);if(!File.Exists(path))return null;
        var account=Data.Read<CosAccount>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),Encoding.UTF8.GetBytes("LcPack/COS/v1"),DataProtectionScope.CurrentUser)));
        Validate(account);return account;
    }
    public static void Validate(CosAccount account) {
        Cloud.Validate(account.channel);
        if(String.IsNullOrWhiteSpace(account.secretId)||String.IsNullOrWhiteSpace(account.secretKey))throw new IOException("请填写 COS SecretId 和 SecretKey。");
        using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(account.signingKey);if(rsa.PublicOnly||rsa.ToXmlString(false)!=account.channel.publicKey)throw new InvalidDataException("本机清单签名密钥不匹配。");}
    }
    public static CosAccount Save(Workspace workspace,string endpoint,string id,string key) {
        var old=Load(workspace);string privateKey;
        if(old!=null)privateKey=old.signingKey;
        else using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;privateKey=rsa.ToXmlString(true);}
        string publicKey;using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(privateKey);publicKey=rsa.ToXmlString(false);}
        var account=new CosAccount{channel=new CloudConfig{endpoint=endpoint.Trim().TrimEnd('/'),publicKey=publicKey},secretId=id.Trim(),secretKey=key.Trim(),signingKey=privateKey};Validate(account);
        if(Cloud.Enabled&&(Cloud.Config.endpoint!=account.channel.endpoint||Cloud.Config.publicKey!=publicKey))throw new IOException("此程序绑定了另一更新源，请使用对应渠道的程序。");
        string path=Path.Combine(workspace.Root,"cos-credential.dat"),temp=path+".tmp-"+Guid.NewGuid().ToString("N");SafePath.NoLinks(path);
        try{File.WriteAllBytes(temp,ProtectedData.Protect(Encoding.UTF8.GetBytes(Data.Json(account)),Encoding.UTF8.GetBytes("LcPack/COS/v1"),DataProtectionScope.CurrentUser));Data.Replace(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        Data.Atomic(Path.Combine(workspace.Root,"cos-channel.json"),Data.Json(account.channel));Cloud.Configure(account.channel);return account;
    }
    public static string Sign(CosAccount account,string kind,string json) {
        string payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        if(payload.Length>7*1024*1024)throw new InvalidDataException("版本清单过大。");
        using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(account.signingKey);return Data.Json(new SignedDocument{kind=kind,payload=payload,signature=Convert.ToBase64String(rsa.SignData(Cloud.SignBytes(kind,payload),CryptoConfig.MapNameToOID("SHA256")))});}
    }
}
public sealed class CosApi:IDisposable {
    readonly CosAccount account;readonly HttpClient client;
    public CosApi(CosAccount account):this(account,new HttpClientHandler{AllowAutoRedirect=false}){}
    public CosApi(CosAccount account,HttpMessageHandler handler) {
        CosSettings.Validate(account);this.account=account;Cloud.Configure(account.channel);
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;ServicePointManager.DefaultConnectionLimit=8;
        client=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(15)};client.DefaultRequestHeaders.UserAgent.ParseAdd("LethalGoofballsPack-Publisher/1.0");
    }
    static string Sha1(string text){using(var hash=SHA1.Create())return Data.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    static string Hmac(string key,string text){using(var hash=new HMACSHA1(Encoding.UTF8.GetBytes(key)))return Data.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    public static string Authorization(string id,string secret,string method,Uri uri,string md5,long start) {
        string time=start+";"+(start+1800),headers=(md5==null?"":"content-md5="+Uri.EscapeDataString(md5)+"&")+"host="+uri.Host;
        string http=method.ToLowerInvariant()+"\n"+uri.AbsolutePath+"\n\n"+headers+"\n";
        string signature=Hmac(Hmac(secret,time),"sha1\n"+time+"\n"+Sha1(http)+"\n");
        return "q-sign-algorithm=sha1&q-ak="+id+"&q-sign-time="+time+"&q-key-time="+time+"&q-header-list="+(md5==null?"":"content-md5;")+"host&q-url-param-list=&q-signature="+signature;
    }
    async Task<HttpResponseMessage> Send(HttpMethod method,string key,HttpContent content,string md5,string hash,bool immutable) {
        var uri=new Uri(Cloud.Url(key));
        using(var request=new HttpRequestMessage(method,uri)) {
            request.Content=content;request.Headers.Host=uri.Host;
            request.Headers.TryAddWithoutValidation("Authorization",Authorization(account.secretId,account.secretKey,method.Method,uri,md5,(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds-30));
            if(hash!=null)request.Headers.Add("x-cos-meta-sha256",hash);
            if(immutable)request.Headers.Add("x-cos-forbid-overwrite","true");
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode&&response.StatusCode!=HttpStatusCode.NotFound&&response.StatusCode!=HttpStatusCode.Conflict) {
                int status=(int)response.StatusCode;string requestId=response.Headers.Contains("x-cos-request-id")?response.Headers.GetValues("x-cos-request-id").FirstOrDefault():"";response.Dispose();
                throw new IOException("COS HTTP "+status+(status==403?"：请检查存储桶权限和上传密钥。":"，上传尚未完成。")+" 请求编号："+requestId);
            }
            return response;
        }
    }
    public async Task<string> Read(string key) {
        using(var response=await Send(HttpMethod.Get,key,null,null,null,false).ConfigureAwait(false)) {
            if(response.StatusCode==HttpStatusCode.NotFound)return null;
            if(!response.IsSuccessStatusCode)throw new IOException("COS 清单读取失败。");
            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream())using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                byte[] buffer=new byte[32768];int count;
                while((count=await Transfer.ReadNetwork(input,buffer,timeout.Token).ConfigureAwait(false))>0){if(output.Length+count>8*1024*1024)throw new InvalidDataException("COS 清单过大。");output.Write(buffer,0,count);}
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
    public async Task<Manifest> Remote() {string text=await Read("manifest.json").ConfigureAwait(false);if(text==null)return null;var m=Data.Read<Manifest>(Cloud.Verify(text,"mods"));SafePath.ValidateManifest(m);return m;}
    public async Task<bool> Exists(string key,string hash,long size) {
        using(var response=await Send(HttpMethod.Head,key,null,null,null,false).ConfigureAwait(false)) {
            if(response.StatusCode==HttpStatusCode.NotFound)return false;
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength!=size||!response.Headers.Contains("x-cos-meta-sha256")||response.Headers.GetValues("x-cos-meta-sha256").SingleOrDefault()!=hash)throw new IOException("COS 已有同名对象但校验信息不符，已停止发布。");
            return true;
        }
    }
    public async Task Put(string key,Stream stream,string type,bool immutable,Action<int> sent=null) {
        long size=stream.Length;
        byte[] md5;using(var digest=MD5.Create())md5=digest.ComputeHash(stream);stream.Position=0;
        string hash;using(var digest=SHA256.Create())hash=Data.Hex(digest.ComputeHash(stream));stream.Position=0;
        using(var content=UploadContent.For(stream,sent)) {
            content.Headers.ContentType=MediaTypeHeaderValue.Parse(type);content.Headers.ContentLength=stream.Length;content.Headers.ContentMD5=md5;
            content.Headers.TryAddWithoutValidation("Cache-Control",immutable?"public, max-age=31536000, immutable":"no-store, max-age=0");
            using(var response=await Send(HttpMethod.Put,key,content,Convert.ToBase64String(md5),hash,immutable).ConfigureAwait(false)) {
                if(response.StatusCode==HttpStatusCode.Conflict&&immutable){if(await Exists(key,hash,size).ConfigureAwait(false))return;}
                if(!response.IsSuccessStatusCode||response.Headers.ETag==null||!String.Equals(response.Headers.ETag.Tag.Trim('"'),Data.Hex(md5),StringComparison.OrdinalIgnoreCase))throw new IOException("COS 上传校验失败，尚未发布更新。");
            }
        }
    }
    public async Task Blob(string path,PackFile file) {
        if(!File.Exists(path)||new FileInfo(path).Length!=file.size||Data.Hash(path)!=file.sha256)throw new IOException("发布缓存缺失或损坏："+file.path);
        string key="objects/"+file.sha256+".bin";if(await Exists(key,file.sha256,file.size).ConfigureAwait(false))return;
        using(var stream=File.OpenRead(path))await Put(key,stream,"application/octet-stream",true).ConfigureAwait(false);
    }
    public async Task Document(string key,string signed,bool immutable) {using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(signed)))await Put(key,stream,"application/json; charset=utf-8",immutable).ConfigureAwait(false);}
    public void Dispose(){client.Dispose();}
}
public class PagePublishJob { public Manifest Before;public Manifest ReleaseBefore;public Manifest After; }
public static class PagePublisher {
    public const string PathName="BepInEx/pack-info/display-pages.json";
    public static bool OnlyPages(Manifest before,Manifest after){
        if(before==null||after==null)return false;
        var a=Data.Read<Manifest>(Data.Json(before));var b=Data.Read<Manifest>(Data.Json(after));
        a.files.RemoveAll(f=>f.path==PathName);b.files.RemoveAll(f=>f.path==PathName);
        return CosPublisher.Equivalent(a,b)&&a.files.All(f=>b.files.Any(g=>g.path==f.path&&g.url==f.url))&&after.files.Count(f=>f.path==PathName)==1;
    }
    static bool Match(Manifest a,Manifest b){return Workspace.Fingerprint(a)==Workspace.Fingerprint(b);}
    static void Guard(Manifest current,Manifest old,Manifest next){if(!Match(current,old)&&!Match(current,next))throw new IOException("发布期间云端被其他操作修改；已停止，保留恢复记录。");}
    public static async Task<Manifest> Run(Workspace ws,CosAccount account,CosApi cos,Transfer transfer,PagePublishJob job,Action<string> progress,GitHubApi githubOverride=null){
        if(!OnlyPages(job.Before,job.After)||!OnlyPages(job.ReleaseBefore,job.After))throw new IOException("同版本发布只允许修改接收端展示资料，MOD、配置和删除列表必须保持不变。");
        SafePath.ValidateManifest(job.After);
        string journal=System.IO.Path.Combine(ws.Root,"page-publish.pending.json");
        string token=ws.Token();if(String.IsNullOrEmpty(token))throw new IOException("展示资料发布需要 GitHub 和 COS 两端凭据。");
        var file=job.After.files.Single(f=>f.path==PathName);string local=ws.ObjectPath(file.sha256);
        if(!File.Exists(local)||Data.Hash(local)!=file.sha256||new FileInfo(local).Length!=file.size||file.size>2*1024*1024)throw new IOException("展示资料缓存缺失、损坏或超过 2 MB。");
        var json=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(local,Encoding.UTF8)) as System.Collections.Generic.Dictionary<string,object>;
        if(json==null||!json.ContainsKey("Schema")||Convert.ToInt32(json["Schema"])!=1||!json.ContainsKey("Mods")||!json.ContainsKey("History"))throw new IOException("展示资料格式无效。");
        using(var github=githubOverride??new GitHubApi(token)){
            await github.ValidateAccount().ConfigureAwait(false);
            Guard(await cos.Remote().ConfigureAwait(false),job.Before,job.After);
            var feed=Data.Read<Manifest>(Cloud.Verify(await github.ReadModFeed().ConfigureAwait(false),"mods"));
            if(!Match(feed,job.ReleaseBefore))Guard(feed,job.Before,job.After);
            var release=await github.FindTag(job.After.version).ConfigureAwait(false);if(release==null||release.draft)throw new IOException("原版本 GitHub Release 不存在或未公开。");
            Data.Atomic(journal,Data.Json(job));
            progress("正在上传展示资料并核对两个下载源……");
            await cos.Blob(local,file).ConfigureAwait(false);
            var assets=await github.Assets(release.id).ConfigureAwait(false);
            if(file.url==Channel.Web+"/releases/download/"+job.After.version+"/"+file.sha256+".bin"&&!assets.Any(a=>a.name==file.sha256+".bin"))using(var stream=File.OpenRead(local))await github.Upload(release.id,file.sha256+".bin",stream,"application/octet-stream",file.size,file.sha256).ConfigureAwait(false);
            string temp=System.IO.Path.Combine(ws.Root,"page-verify-"+Guid.NewGuid().ToString("N"));
            try{await transfer.VerifyCloudFile(file,temp).ConfigureAwait(false);File.Delete(temp);await transfer.VerifyGitHubFile(file,temp).ConfigureAwait(false);}finally{if(File.Exists(temp))File.Delete(temp);}
            await github.ValidateReferences(job.After,transfer).ConfigureAwait(false);
            string payload=Data.Json(job.After),signed=CosSettings.Sign(account,"mods",payload),hash=Workspace.Fingerprint(job.After),oldHash=Workspace.Fingerprint(job.ReleaseBefore);
            await cos.Document("page-revisions/"+hash+".json",signed,true).ConfigureAwait(false);
            await cos.Document("page-revisions/before-"+Workspace.Fingerprint(job.Before)+".json",CosSettings.Sign(account,"mods",Data.Json(job.Before)),true).ConfigureAwait(false);
            var manifest=assets.FirstOrDefault(a=>a.name=="manifest.json");
            if(manifest!=null&&manifest.digest!="sha256:"+hash){
                if(manifest.digest!="sha256:"+oldHash){var observed=await transfer.ForRelease(job.After.version).ConfigureAwait(false);if(!Match(observed,job.ReleaseBefore))throw new IOException("GitHub Release 清单发生变化，停止覆盖。");}
                await github.RenameAsset(manifest.id,"manifest.before-pages-"+oldHash+"-"+Guid.NewGuid().ToString("N")+".json").ConfigureAwait(false);manifest=null;
            }
            if(manifest==null)using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(payload)))await github.Upload(release.id,"manifest.json",stream,"application/json",stream.Length,hash).ConfigureAwait(false);
            Guard(await cos.Remote().ConfigureAwait(false),job.Before,job.After);
            feed=Data.Read<Manifest>(Cloud.Verify(await github.ReadModFeed().ConfigureAwait(false),"mods"));
            if(!Match(feed,job.ReleaseBefore))Guard(feed,job.Before,job.After);
            await github.StoreFeed("mod-update.signed.json",signed).ConfigureAwait(false);
            await cos.Document("versions/"+job.After.version+".json",signed,false).ConfigureAwait(false);
            await cos.Document("manifest.json",signed,false).ConfigureAwait(false);
            if(job.Before.version!=job.After.version)await github.MakeLatest(release.id).ConfigureAwait(false);
            if(Cloud.Verify(await github.ReadModFeed().ConfigureAwait(false),"mods")!=payload||await transfer.CloudVersion("manifest.json","mods").ConfigureAwait(false)!=payload||await transfer.CloudVersion("versions/"+job.After.version+".json","mods").ConfigureAwait(false)!=payload||!Match(await transfer.ForRelease(job.After.version).ConfigureAwait(false),job.After))throw new IOException("两端清单尚未全部一致，请再次点击发布继续完成，恢复记录已保留。");
            ws.SetRemote(job.After);File.Move(journal,journal+".completed-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));return job.After;
        }
    }
}
internal sealed class UploadMeter {
    readonly string channel;readonly Action<string> report;readonly long total;readonly int files,reused;
    readonly System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();long bytes,last=-1000;int done;
    public UploadMeter(string channel,PackFile[] missing,int reused,Action<string> report){
        this.channel=channel;this.report=report;this.reused=reused;files=missing.Length;total=missing.Sum(f=>f.size);Tick("",0,false);
    }
    public void Tick(string path,int n,bool finished){lock(this){
        bytes+=n;if(finished)done++;long ms=watch.ElapsedMilliseconds;if(n>0&&ms-last<180)return;last=ms;
        double rate=bytes/Math.Max(0.001,watch.Elapsed.TotalSeconds);
        report(channel+" · 文件 "+done+"/"+files+" · 已复用 "+reused+" · 已传 "+bytes.ToString("N0")+" / "+total.ToString("N0")+" B"+
            (total>0?" ("+((double)bytes*100/total).ToString("0.#")+"%)":"")+(ms>=1000&&rate>0?" · "+(rate/1024).ToString("0.#")+" KiB/s · 约 "+Math.Ceiling((total-bytes)/rate)+" 秒":"")+" · "+path);
    }}
}
public static class CosPublisher {
    public static PackFile[] BackupUploads(Manifest manifest) {
        return manifest.files.Where(f=>f.url==Channel.Web+"/releases/download/"+manifest.version+"/"+f.sha256+".bin").GroupBy(f=>f.sha256).Select(g=>g.First()).ToArray();
    }
    public static async Task Backup(Workspace workspace,CosAccount account,Manifest manifest,Action<string> progress,Func<HttpMessageHandler> transport=null,string expectedBaseHash=null,string releaseNotes=null) {
        string token=workspace.Token();if(String.IsNullOrEmpty(token))throw new IOException("未保存 GitHub 备用发布凭据。");
        using(var api=transport==null?new GitHubApi(token):new GitHubApi(token,transport()))using(var transfer=transport==null?new Transfer():new Transfer(transport())) {
            await api.ValidateAccount().ConfigureAwait(false);var release=await api.FindTag(manifest.version).ConfigureAwait(false);
            if(release==null)release=await api.Create(manifest.version,releaseNotes).ConfigureAwait(false);
            if(release.draft) {
                release=await api.GetRelease(release.id).ConfigureAwait(false);
                var all=BackupUploads(manifest);var indexed=(await api.Assets(release.id).ConfigureAwait(false)).ToDictionary(a=>a.name);
                var missing=all.Where(f=>{ReleaseAsset a;if(!indexed.TryGetValue(f.sha256+".bin",out a))return true;
                    if(a.size!=f.size||a.digest!="sha256:"+f.sha256)throw new IOException("GitHub 已有附件校验失败："+f.path);return false;}).ToArray();
                var meter=new UploadMeter("GitHub",missing,all.Length-missing.Length,progress);
                using(var workers=new SemaphoreSlim(4))await Task.WhenAll(missing.Select(async file=>{
                    await workers.WaitAsync().ConfigureAwait(false);try {

                    using(var stream=File.OpenRead(workspace.ObjectPath(file.sha256)))await api.Upload(release.id,file.sha256+".bin",stream,"application/octet-stream",file.size,file.sha256,n=>meter.Tick(file.path,n,false)).ConfigureAwait(false);
                    meter.Tick(file.path,0,true);
                    }finally{workers.Release();}
                })).ConfigureAwait(false);
                byte[] bytes=Encoding.UTF8.GetBytes(Data.Json(manifest));string hash;using(var sha=SHA256.Create())hash=Data.Hex(sha.ComputeHash(bytes));
                ReleaseAsset old;indexed.TryGetValue("manifest.json",out old);
                if(old==null)using(var stream=new MemoryStream(bytes))await api.Upload(release.id,"manifest.json",stream,"application/json",bytes.Length,hash).ConfigureAwait(false);
                else if(old.size!=bytes.Length||old.digest!="sha256:"+hash)throw new IOException("GitHub 备用草稿清单不一致。");
                await api.ValidateReferences(manifest,transfer,null,manifest.version).ConfigureAwait(false);
                await api.Commit(release.id).ConfigureAwait(false);
            }
            var published=await transfer.ForRelease(manifest.version).ConfigureAwait(false);
            if(!Equivalent(published,manifest)||published.files.Any(f=>manifest.files.First(p=>p.path==f.path).url!=f.url))throw new IOException("GitHub 同版本备份内容不一致。");
            await api.ValidateReferences(manifest,transfer).ConfigureAwait(false);
            string payload=Data.Json(manifest);
            if(expectedBaseHash!=null){
                string current=await api.OptionalModFeed().ConfigureAwait(false);
                if(!String.IsNullOrEmpty(current)){
                    string old=Cloud.Verify(current,"mods");if(old==payload)return;
                    if(Workspace.Fingerprint(Data.Read<Manifest>(old))!=expectedBaseHash)throw new IOException("GitHub 备用清单已被其他发布修改，已停止覆盖。");
                }
            }
            await api.StoreFeed("mod-update.signed.json",CosSettings.Sign(account,"mods",payload)).ConfigureAwait(false);
        }
    }
    public static async Task<Manifest> Publish(Workspace workspace,CosAccount account,CosApi api,Transfer transfer,Action<string> progress,Func<HttpMessageHandler> transport=null) {
        using(var publishLock=new FileStream(Path.Combine(workspace.Root,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            string pending=System.IO.Path.Combine(workspace.Root,"page-publish.pending.json");
            if(File.Exists(pending)){
                var job=Data.Load<PagePublishJob>(pending);
                if(!Equivalent(job.After,new Manifest{files=workspace.Current.files,deletes=workspace.Current.deletes}))throw new IOException("上次展示资料发布未完成，请先恢复该草稿后重试，勿覆盖待恢复记录。");
                return await PagePublisher.Run(workspace,account,api,transfer,job,progress,transport==null?null:new GitHubApi(workspace.Token(),transport())).ConfigureAwait(false);
            }
            var remote=await api.Remote().ConfigureAwait(false);
            workspace.CheckBase(remote);
            if(remote!=null&&remote.version!=workspace.Current.baseVersion)throw new IOException("云端已有新版本，请先重新载入云端清单。");
            if(workspace.Current.files.Count==0)throw new IOException("请先拖入要发布的 MOD 文件。");
            string requested=Publisher.VersionName(workspace.Current.releaseVersion);
            bool same=remote!=null&&Equivalent(remote,new Manifest{files=workspace.Current.files,deletes=workspace.Current.deletes});
            if(same&&(requested.Length==0||requested==remote.version))return remote;
            string tag=requested.Length>0?requested:remote==null&&!String.IsNullOrEmpty(workspace.Current.baseVersion)?Publisher.VersionName(workspace.Current.baseVersion):DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            if(remote!=null&&(tag==remote.version||requested.Length==0)&&PagePublisher.OnlyPages(remote,new Manifest{files=workspace.Current.files,deletes=workspace.Current.deletes})){
                var next=Data.Read<Manifest>(Data.Json(new Manifest{version=remote.version,files=workspace.Current.files,deletes=workspace.Current.deletes}));
                foreach(var f in next.files)if(String.IsNullOrEmpty(f.url))f.url=Channel.Web+"/releases/download/"+next.version+"/"+f.sha256+".bin";
                return await PagePublisher.Run(workspace,account,api,transfer,new PagePublishJob{Before=remote,ReleaseBefore=remote,After=next},progress,transport==null?null:new GitHubApi(workspace.Token(),transport())).ConfigureAwait(false);
            }
            if(remote!=null&&tag==remote.version)throw new IOException("MOD 或配置已变化，请填写新的发布版本名；只有展示资料允许同版本发布。");
            if(requested.Length==0)throw new IOException("请填写明确的整合包版本号，不再自动生成时间戳版本。");
            var manifest=Data.Read<Manifest>(Data.Json(new Manifest{version=tag,files=workspace.Current.files,deletes=workspace.Current.deletes}));
            foreach(var file in manifest.files)if(String.IsNullOrEmpty(file.url))file.url=Channel.Web+"/releases/download/"+tag+"/"+file.sha256+".bin";
            SafePath.ValidateManifest(manifest);
            if(!String.IsNullOrEmpty(workspace.Token()))using(var github=transport==null?new GitHubApi(workspace.Token()):new GitHubApi(workspace.Token(),transport()))await github.ValidateReferences(manifest,transfer,tag).ConfigureAwait(false);
            progress("正在生成并签名正式版本清单");
            string signed=CosSettings.Sign(account,"mods",Data.Json(manifest)),versionKey="versions/"+tag+".json";
            string existing=await api.Read(versionKey).ConfigureAwait(false);if(existing!=null&&existing!=signed)throw new IOException("该版本名已有不同内容，请填写新的版本名。");
            var files=manifest.files.GroupBy(f=>f.sha256).Select(g=>g.First()).ToArray();
            progress("COS · 核对已有对象");
            bool[] existingObjects;
            using(var gate=new SemaphoreSlim(4))existingObjects=await Task.WhenAll(files.Select(async f=>{await gate.WaitAsync().ConfigureAwait(false);
                try{return await api.Exists("objects/"+f.sha256+".bin",f.sha256,f.size).ConfigureAwait(false);}finally{gate.Release();}
            })).ConfigureAwait(false);
            var missing=files.Where((f,i)=>!existingObjects[i]).ToArray();var meter=new UploadMeter("COS",missing,files.Length-missing.Length,progress);
            using(var workers=new SemaphoreSlim(4))await Task.WhenAll(missing.Select(async file=>{
                await workers.WaitAsync().ConfigureAwait(false);
                try {
                    string path=workspace.ObjectPath(file.sha256);SafePath.NoLinks(path);
                    if(!File.Exists(path)||new FileInfo(path).Length!=file.size||Data.Hash(path)!=file.sha256){string temp=path+".tmp-"+Guid.NewGuid().ToString("N");try{await transfer.Download(file,temp,null).ConfigureAwait(false);Data.Replace(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
                    using(var stream=File.OpenRead(path))await api.Put("objects/"+file.sha256+".bin",stream,"application/octet-stream",true,n=>meter.Tick(file.path,n,false)).ConfigureAwait(false);
                    meter.Tick(file.path,0,true);
                }finally{workers.Release();}
            })).ConfigureAwait(false);
            await api.Document(versionKey,signed,true).ConfigureAwait(false);
            if(await transfer.CloudVersion(versionKey,"mods").ConfigureAwait(false)!=Data.Json(manifest))throw new IOException("COS 公开下载验证失败，未替换最新清单。");
            var current=await api.Remote().ConfigureAwait(false);if(Data.Json(current)!=Data.Json(remote))throw new IOException("发布期间云端发生变化，未替换最新清单。");
            progress("正在发布签名清单……");
            Exception commitError=null;try{await api.Document("manifest.json",signed,false).ConfigureAwait(false);}catch(Exception e){commitError=e;}
            if(commitError!=null&&await api.Read("manifest.json").ConfigureAwait(false)!=signed)throw commitError;
            workspace.SetRemote(manifest);return manifest;
        }
    }
    public static bool Equivalent(Manifest left,Manifest right) {return left.files.Count==right.files.Count&&left.files.All(a=>right.files.Any(b=>a.path==b.path&&a.sha256==b.sha256&&a.size==b.size))&&left.deletes.OrderBy(x=>x).SequenceEqual(right.deletes.OrderBy(x=>x));}
}
}
