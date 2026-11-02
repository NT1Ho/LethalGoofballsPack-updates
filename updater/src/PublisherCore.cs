using System;
using System.Collections.Generic;
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
public class Draft {
    public string baseVersion="";
    public string cumulative;
    public string baseManifestHash="";
    public string releaseVersion="";
    public List<PackFile> files=new List<PackFile>();
    public List<string> deletes=new List<string>();
}
public class Workspace {
    // AppData can be redirected for packaged callers; both entry points use this stable user folder.
    public static string DefaultRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".LethalGoofballsPackPublisher"); } }
    public readonly string Root;
    public Draft Current;
    string draftFileHash;
    public Workspace(string root) {
        Root=Path.GetFullPath(root);SafePath.NoLinks(Root);Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"objects"));
        string path=Path.Combine(Root,"draft.json");SafePath.NoLinks(path);
        if(!File.Exists(path)){Current=new Draft();draftFileHash="";return;}
        using(var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
            draftFileHash=Data.Hash(path);Current=Data.Load<Draft>(path);
        }
    }
    public string ObjectPath(string hash) { if(!System.Text.RegularExpressions.Regex.IsMatch(hash??"","^[a-f0-9]{64}$"))throw new InvalidDataException("本地文件索引无效。");return Path.Combine(Root,"objects",hash); }
    public void Save() { CommitDraft(Current); }
    public void CommitDraft(Draft next) {
        string path=Path.Combine(Root,"draft.json"),gate=Path.Combine(Root,"draft-write.lock");
        SafePath.NoLinks(path);SafePath.NoLinks(gate);
        using(var lease=new FileStream(gate,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
            if((File.Exists(path)?Data.Hash(path):"")!=draftFileHash)throw new IOException("另一个程序已经修改草稿，请重新打开工作区。");
            Data.Atomic(path,Data.Json(next));draftFileHash=Data.Hash(path);Current=next;
        }
    }
    public void RememberBaseline(Manifest manifest) {
        SafePath.ValidateManifest(manifest);
        string path=Path.Combine(Root,"baseline-"+Fingerprint(manifest)+".json");
        SafePath.NoLinks(path);
        if(!File.Exists(path)||Data.Hash(path)!=Fingerprint(manifest))Data.Atomic(path,Data.Json(manifest));
    }
    public Manifest Baseline() {
        if(String.IsNullOrEmpty(Current.baseVersion))return new Manifest();
        string hash=Current.baseManifestHash;
        if(!System.Text.RegularExpressions.Regex.IsMatch(hash??"",@"\A[0-9a-f]{64}\z"))return null;
        string path=Path.Combine(Root,"baseline-"+hash+".json");SafePath.NoLinks(path);
        if(File.Exists(path)){
            if(Data.Hash(path)!=hash)return null; // Keep editing locked until a verified remote snapshot repairs this cache.
            var stored=Data.Load<Manifest>(path);SafePath.ValidateManifest(stored);return stored;
        }
        var baseline=new Manifest{version=Current.baseVersion,files=Current.files,deletes=Current.deletes};
        return Fingerprint(baseline)==hash?Data.Read<Manifest>(Data.Json(baseline)):null;
    }
    public void SetRemote(Manifest manifest) {
        if(manifest!=null)SafePath.ValidateManifest(manifest);
        var next=manifest==null?new Draft():Data.Read<Draft>(Data.Json(new Draft {baseVersion=manifest.version,baseManifestHash=Fingerprint(manifest),files=manifest.files,deletes=manifest.deletes}));
        if(manifest!=null)RememberBaseline(manifest);
        CommitDraft(next);
    }
    public static string Fingerprint(Manifest manifest) {using(var sha=SHA256.Create())return Data.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Data.Json(manifest))));}
    public void CheckBase(Manifest remote) {
        if(!String.IsNullOrEmpty(Current.baseManifestHash)&&(remote==null||Current.baseManifestHash!=Fingerprint(remote)))throw new IOException("云端清单内容已变化（版本号可能未变），请先重新载入云端，避免覆盖他人的发布。");
    }
    public async Task CacheCatalog(IEnumerable<PackFile> files,Transfer transfer) {
        foreach(var file in files.Where(f=>f.path=="BepInEx/pack-info/display-pages.json"||f.path=="BepInEx/pack-info/original-files.json"||f.path=="BepInEx/pack-info/download-sources.json")) {
            if(file.size>8*1024*1024)throw new IOException("MOD 分类资料过大。");
            string dest=ObjectPath(file.sha256);SafePath.NoLinks(dest);
            if(File.Exists(dest)&&Data.Hash(dest)==file.sha256)continue;
            if(String.IsNullOrEmpty(file.url))throw new IOException("待发布的 MOD 分类资料缺失，请重新拖入该文件。");
            string tmp=dest+".tmp-"+Guid.NewGuid().ToString("N");
            try {await transfer.Download(file,tmp,null).ConfigureAwait(false);Data.Read<object>(File.ReadAllText(tmp,Encoding.UTF8));Data.Replace(tmp,dest);}
            finally {if(File.Exists(tmp))File.Delete(tmp);}
        }
    }
    public PackFile CacheSource(string source,string relative) {
        SafePath.Validate(relative);SafePath.NoLinks(source);var fi=new FileInfo(source);
        if(fi.Length>2L*1024*1024*1024)throw new IOException("GitHub 单文件上限为 2 GiB。");
        string hash=Data.Hash(source), dest=ObjectPath(hash);
        SafePath.NoLinks(dest);
        if(!File.Exists(dest)||new FileInfo(dest).Length!=fi.Length||Data.Hash(dest)!=hash) {
            string tmp=dest+".tmp-"+Guid.NewGuid().ToString("N");
            try { Data.CopyDurable(source,tmp);if(Data.Hash(tmp)!=hash)throw new IOException("拖入的文件正在变化，请关闭占用程序后重试。");Data.Replace(tmp,dest); }
            finally { if(File.Exists(tmp))File.Delete(tmp); }
        }
        return new PackFile{path=relative,sha256=hash,size=new FileInfo(dest).Length};
    }
    public void Add(string source,string relative) {
        var cached=CacheSource(source,relative);string hash=cached.sha256;
        var previous=Current.files.FirstOrDefault(f=>String.Equals(f.path,relative,StringComparison.OrdinalIgnoreCase));
        string url=previous!=null&&previous.sha256==hash?previous.url:null;
        Current.files.RemoveAll(f=>String.Equals(f.path,relative,StringComparison.OrdinalIgnoreCase));
        cached.url=url;Current.files.Add(cached);
        Current.deletes.RemoveAll(p=>String.Equals(p,relative,StringComparison.OrdinalIgnoreCase));Save();
    }
    public void Remove(IEnumerable<string> names) {
        foreach(string path in names) {SafePath.Validate(path);Current.files.RemoveAll(f=>String.Equals(f.path,path,StringComparison.OrdinalIgnoreCase));if(!Current.deletes.Contains(path,StringComparer.OrdinalIgnoreCase))Current.deletes.Add(path);}Save();
    }
    public static string Infer(string source) {
        string full=Path.GetFullPath(source).Replace('\\','/');int start=full.IndexOf("/BepInEx/",StringComparison.OrdinalIgnoreCase);
        if(start>=0)return full.Substring(start+1);
        if(full.EndsWith("/BepInEx",StringComparison.OrdinalIgnoreCase))return "BepInEx";
        string name=Path.GetFileName(source);
        return new[]{"winhttp.dll","doorstop_config.ini",".doorstop_version"}.Contains(name,StringComparer.OrdinalIgnoreCase)?name:null;
    }
    public static IEnumerable<string> Files(string directory) {
        SafePath.NoLinks(directory);
        foreach(string path in Directory.GetFiles(directory)){SafePath.NoLinks(path);yield return path;}
        foreach(string sub in Directory.GetDirectories(directory))foreach(string path in Files(sub))yield return path;
    }
    public string Token() {
        string p=Path.Combine(Root,"credential.dat");if(!File.Exists(p))return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(p),Encoding.UTF8.GetBytes(Channel.Repo),DataProtectionScope.CurrentUser));
    }
    public void SaveToken(string value) {
        if(String.IsNullOrWhiteSpace(value))throw new InvalidDataException("请填写发布凭据。");
        byte[] bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()),Encoding.UTF8.GetBytes(Channel.Repo),DataProtectionScope.CurrentUser);
        string p=Path.Combine(Root,"credential.dat"),tmp=p+".tmp-"+Guid.NewGuid().ToString("N");
        File.WriteAllBytes(tmp,bytes);Data.Replace(tmp,p);
    }
}
public class ReleaseAsset {public long id;public string name;public string browser_download_url;public long size;public string digest;}
public class Release {public long id;public bool draft;public string tag_name;public List<ReleaseAsset> assets;}
public class RepoInfo {public bool @private;public bool archived;public string full_name;public string default_branch;}
public class UserInfo {public string login;}
public class RepositoryFileInfo {public string sha;}
public class FeedFile {public string content;}
public class GitHubApi : IDisposable {
    readonly HttpClient client;
    readonly SemaphoreSlim writeGate=new SemaphoreSlim(1);DateTime nextWrite=DateTime.MinValue;
    public GitHubApi(string token) : this(token,new HttpClientHandler {AllowAutoRedirect=false}) {}
    public GitHubApi(string token,HttpMessageHandler handler) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit=8;
        client=new HttpClient(handler);client.Timeout=TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LethalGoofballsPack-Publisher/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        if(!String.IsNullOrEmpty(token))client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    }
    async Task<T> Send<T>(HttpMethod method,string path,HttpContent body,bool missingOkay) where T:class {
        if(method!=HttpMethod.Get)await PaceWrite().ConfigureAwait(false);
        using(var request=new HttpRequestMessage(method,"https://api.github.com"+path)) {
            request.Content=body;
            using(var response=await client.SendAsync(request).ConfigureAwait(false)) {
                if(response.StatusCode==HttpStatusCode.NotFound&&missingOkay)return null;
                if(!response.IsSuccessStatusCode)throw Error(response.StatusCode);
                return Data.Read<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }
    async Task PaceWrite() {
        await writeGate.WaitAsync().ConfigureAwait(false);
        try {
            var wait=nextWrite-DateTime.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait).ConfigureAwait(false);
            nextWrite=DateTime.UtcNow.AddMilliseconds(900);
        }finally{writeGate.Release();}
    }
    static Exception Error(HttpStatusCode status) {
        if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)return new IOException("GitHub 授权不足或达到限流，请检查凭据或稍后重试。");
        if(status==HttpStatusCode.NotFound)return new IOException("更新仓库不存在或当前凭据无权访问。");
        return new IOException("GitHub 返回 "+(int)status+"，本次发布未完成。");
    }
    static HttpContent JsonBody(object data) {return new StringContent(Data.Json(data),Encoding.UTF8,"application/json");}
    public async Task ValidateAccount() {
        var user=await Send<UserInfo>(HttpMethod.Get,"/user",null,false).ConfigureAwait(false);
        if(!String.Equals(user.login,Channel.Repo.Split('/')[0],StringComparison.OrdinalIgnoreCase))throw new IOException("请使用 NT1Ho 本人的发布凭据。");
        var repo=await Send<RepoInfo>(HttpMethod.Get,"/repos/"+Channel.Repo,null,false).ConfigureAwait(false);
        if(repo.@private||repo.archived||!String.Equals(repo.full_name,Channel.Repo,StringComparison.OrdinalIgnoreCase))throw new IOException("更新源必须是指定的公开、可写仓库。");
    }
    public Task<Release> LatestRelease() {return Send<Release>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases/latest?check="+Guid.NewGuid().ToString("N"),null,true);}
    public Task<Release> GetRelease(long id) {return Send<Release>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases/"+id,null,false);}
    public Task<Release> FindTag(string tag) {return Send<Release>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases/tags/"+Uri.EscapeDataString(tag),null,true);}
    public async Task<Release> FindPublishTarget(string tag) {
        var published=await FindTag(tag).ConfigureAwait(false);
        if(published!=null)return published;
        // GitHub's by-tag route returns 404 for private drafts, even to the owner.
        for(int page=1;;page++){
            var batch=await Send<List<Release>>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases?per_page=3&page="+page,null,false).ConfigureAwait(false);
            var draft=batch.SingleOrDefault(r=>r.draft&&r.tag_name==tag);
            if(draft!=null||batch.Count<3)return draft;
        }
    }
    public async Task ValidateReferences(Manifest manifest,Transfer transfer,string pendingTag=null,string allowedDraftTag=null) {
        SafePath.ValidateManifest(manifest);
        // Check every URL, not just every hash: two paths can share bytes but reference different releases.
        foreach(var group in manifest.files.GroupBy(f=>new Uri(f.url).Segments.Reverse().Skip(1).First().TrimEnd('/'))) {
            if(group.Key==pendingTag)continue;
            var release=group.Key==allowedDraftTag?await FindPublishTarget(group.Key).ConfigureAwait(false):await FindTag(group.Key).ConfigureAwait(false);
            if(release==null||(release.draft&&group.Key!=allowedDraftTag))throw new IOException("下载引用指向不存在或尚未公开的 Release："+group.Key);
            var assets=new List<ReleaseAsset>();
            for(int page=1;;page++) {
                var batch=await Send<List<ReleaseAsset>>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases/"+release.id+"/assets?per_page=100&page="+page,null,false).ConfigureAwait(false);
                assets.AddRange(batch);if(batch.Count<100)break;
            }
            foreach(var file in group.GroupBy(f=>f.url).Select(g=>g.First())) {
                var asset=assets.SingleOrDefault(a=>a.name==file.sha256+".bin");
                if(asset==null||asset.size!=file.size||(!String.IsNullOrEmpty(asset.digest)&&asset.digest!="sha256:"+file.sha256))throw new IOException("发布已停止：下载附件缺失或校验不符："+file.path+"（"+group.Key+"）");
                // Older GitHub assets may have no digest. Verify their actual bytes rather than trusting their name.
                if(String.IsNullOrEmpty(asset.digest)) {
                    string temp=Path.Combine(Path.GetTempPath(),"lcpack-reference-"+Guid.NewGuid().ToString("N"));
                    try{await transfer.VerifyGitHubFile(file,temp).ConfigureAwait(false);}finally{if(File.Exists(temp))File.Delete(temp);}
                }
            }
        }
    }
    public Task<Release> Create(string tag,string notes=null) {return Send<Release>(HttpMethod.Post,"/repos/"+Channel.Repo+"/releases",JsonBody(new {tag_name=tag,name="LethalGoofballsPack "+tag,draft=true,prerelease=false,body=notes??"由 LethalGoofballsPack 发布工具生成。客户端读取 manifest.json 并按 SHA-256 更新。"}),false);}
    public Task<Release> Commit(long id) {return Send<Release>(new HttpMethod("PATCH"),"/repos/"+Channel.Repo+"/releases/"+id,JsonBody(new {draft=false,prerelease=false,make_latest="true"}),false);}
    public async Task StoreFeed(string name,string text) {
        if(!new[]{"mod-update.signed.json","client-update.signed.json"}.Contains(name))throw new InvalidDataException("备用清单路径无效。");
        string path="/repos/"+Channel.Repo+"/contents/"+name;
        var old=await Send<RepositoryFileInfo>(HttpMethod.Get,path+"?ref=main",null,true).ConfigureAwait(false);
        await Send<object>(HttpMethod.Put,path,JsonBody(new{message="Publish signed update manifest",content=Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),sha=old==null?null:old.sha,branch="main"}),false).ConfigureAwait(false);
    }
    public async Task<string> ReadModFeed() {
        var f=await Send<FeedFile>(HttpMethod.Get,"/repos/"+Channel.Repo+"/contents/mod-update.signed.json?ref=main",null,false).ConfigureAwait(false);
        return Encoding.UTF8.GetString(Convert.FromBase64String(f.content));
    }
    public async Task<string> OptionalModFeed(){
        var f=await Send<FeedFile>(HttpMethod.Get,"/repos/"+Channel.Repo+"/contents/mod-update.signed.json?ref=main",null,true).ConfigureAwait(false);
        return f==null?null:Encoding.UTF8.GetString(Convert.FromBase64String(f.content));
    }
    public async Task<List<ReleaseAsset>> Assets(long id){
        var all=new List<ReleaseAsset>();for(int page=1;;page++){var batch=await Send<List<ReleaseAsset>>(HttpMethod.Get,"/repos/"+Channel.Repo+"/releases/"+id+"/assets?per_page=100&page="+page,null,false).ConfigureAwait(false);all.AddRange(batch);if(batch.Count<100)return all;}
    }
    public Task<object> RenameAsset(long id,string name){return Send<object>(new HttpMethod("PATCH"),"/repos/"+Channel.Repo+"/releases/assets/"+id,JsonBody(new{name=name}),false);}
    public Task<object> MakeLatest(long id){return Send<object>(new HttpMethod("PATCH"),"/repos/"+Channel.Repo+"/releases/"+id,JsonBody(new{make_latest="true"}),false);}
    public async Task Upload(long id,string name,Stream stream,string type,long size,string hash,Action<int> sent=null) {
        await PaceWrite().ConfigureAwait(false);
        using(var request=new HttpRequestMessage(HttpMethod.Post,"https://uploads.github.com/repos/"+Channel.Repo+"/releases/"+id+"/assets?name="+Uri.EscapeDataString(name))) {
            request.Content=UploadContent.For(stream,sent);request.Content.Headers.ContentType=new MediaTypeHeaderValue(type);request.Content.Headers.ContentLength=size;
            using(var response=await client.SendAsync(request).ConfigureAwait(false)) {if(!response.IsSuccessStatusCode)throw Error(response.StatusCode);var asset=Data.Read<ReleaseAsset>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));if(asset.size!=size||(!String.IsNullOrEmpty(asset.digest)&&asset.digest!="sha256:"+hash))throw new IOException("GitHub 上传校验失败，未发布。");}
        }
    }
    public void Dispose(){client.Dispose();writeGate.Dispose();}
}
public sealed class UploadContent:HttpContent {
    readonly Stream source;readonly Action<int> sent;
    UploadContent(Stream source,Action<int> sent){this.source=source;this.sent=sent;}
    public static HttpContent For(Stream s,Action<int> sent){return sent==null?(HttpContent)new StreamContent(s,65536):new UploadContent(s,sent);}
    protected override bool TryComputeLength(out long size){size=source.Length;return true;}
    protected override async Task SerializeToStreamAsync(Stream output,TransportContext context){
        byte[] buffer=new byte[65536];int n;
        while((n=await source.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))>0){
            await output.WriteAsync(buffer,0,n).ConfigureAwait(false);sent(n);
        }
    }
}
public class Publisher {
    public static string VersionName(string value) {
        string name=(value??"").Trim();
        if(name.Length>64||(name.Length>0&&(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9][A-Za-z0-9._-]*$")||name.Contains("..")||name.EndsWith(".")||name.EndsWith(".lock",StringComparison.OrdinalIgnoreCase))))throw new IOException("版本名请用 1–64 个英文字母、数字、点、横线或下划线，例如 V1.5；不能含连续点或以点、.lock 结尾。");
        return name;
    }
    public static async Task<Manifest> Remote(GitHubApi api,Transfer transfer) {
        var release=await api.LatestRelease().ConfigureAwait(false);if(release==null)return null;
        var m=await transfer.ForRelease(release.tag_name).ConfigureAwait(false);if(m.version!=release.tag_name)throw new IOException("云端版本清单与发布标签不符。");return m;
    }
    public static async Task<Manifest> Publish(Workspace workspace,GitHubApi api,Transfer transfer,Action<string> progress) {
        string requested=VersionName(workspace.Current.releaseVersion);
        await api.ValidateAccount().ConfigureAwait(false);
        Manifest remote=await Remote(api,transfer).ConfigureAwait(false);
        workspace.CheckBase(remote);
        if((remote==null?"":remote.version)!=workspace.Current.baseVersion)throw new IOException("云端已有新版本，请先重新载入云端清单，避免覆盖别人的发布。");
        if(workspace.Current.files.Count==0)throw new IOException("请先拖入要发布的 MOD 文件。");
        string tag=requested.Length>0?requested:DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
        var m=Data.Read<Manifest>(Data.Json(new Manifest {version=tag,files=workspace.Current.files,deletes=workspace.Current.deletes}));
        var uploads=new Dictionary<string,PackFile>();
        foreach(var f in m.files) {
            var old=remote==null?null:remote.files.FirstOrDefault(p=>String.Equals(p.path,f.path,StringComparison.OrdinalIgnoreCase)&&p.sha256==f.sha256&&p.size==f.size);
            if(old!=null)f.url=old.url;
            else {
                string source=workspace.ObjectPath(f.sha256);SafePath.NoLinks(source);
                if(!File.Exists(source)||new FileInfo(source).Length!=f.size||Data.Hash(source)!=f.sha256)throw new IOException("发布缓存缺失或损坏，请重新拖入："+f.path);
                f.url=Channel.Web+"/releases/download/"+tag+"/"+f.sha256+".bin";uploads[f.sha256]=f;
            }
        }
        SafePath.ValidateManifest(m);
        await api.ValidateReferences(m,transfer,tag).ConfigureAwait(false);
        if(remote!=null&&uploads.Count==0&&m.files.Count==remote.files.Count&&m.deletes.SequenceEqual(remote.deletes)&&(requested.Length==0||requested==remote.version))return remote;
        if(requested.Length>0&&await api.FindTag(tag).ConfigureAwait(false)!=null)throw new IOException("版本名已发布过，请换一个新名称，例如 V1.6。");
        progress("正在创建草稿……");var release=await api.Create(tag).ConfigureAwait(false);
        int completed=0,failed=0;
        using(var workers=new SemaphoreSlim(4)) {
            await Task.WhenAll(uploads.Select(async pair=>{
                await workers.WaitAsync().ConfigureAwait(false);
                try {
                    if(Interlocked.CompareExchange(ref failed,0,0)!=0)return;
                    using(var stream=File.OpenRead(workspace.ObjectPath(pair.Key)))await api.Upload(release.id,pair.Key+".bin",stream,"application/octet-stream",stream.Length,pair.Key).ConfigureAwait(false);
                    int done=Interlocked.Increment(ref completed);progress("已上传 "+done+"/"+uploads.Count+"（最多 4 路）");
                }catch{Interlocked.Exchange(ref failed,1);throw;}finally{workers.Release();}
            })).ConfigureAwait(false);
        }
        byte[] bytes=Encoding.UTF8.GetBytes(Data.Json(m));progress("正在上传版本清单……");
        string manifestHash;using(var sha=SHA256.Create())manifestHash=Data.Hex(sha.ComputeHash(bytes));
        using(var stream=new MemoryStream(bytes))await api.Upload(release.id,"manifest.json",stream,"application/json",bytes.Length,manifestHash).ConfigureAwait(false);
        var verify=await api.GetRelease(release.id).ConfigureAwait(false);
        if(!verify.draft||verify.assets==null||!verify.assets.Any(a=>a.name=="manifest.json"&&a.size==bytes.Length)||uploads.Any(p=>!verify.assets.Any(a=>a.name==p.Key+".bin"&&a.size==p.Value.size)))throw new IOException("云端草稿文件不完整，未发布。");
        await api.ValidateReferences(m,transfer,null,m.version).ConfigureAwait(false);
        progress("正在发布更新……");
        Exception commitError=null;
        try {await api.Commit(release.id).ConfigureAwait(false);} catch(Exception e) {commitError=e;}
        if(commitError!=null) {var outcome=await api.GetRelease(release.id).ConfigureAwait(false);if(outcome.draft)throw commitError;}
        workspace.SetRemote(m);return m;
    }
}
}
