using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using LcPack;
class RepositoryFile {public string sha;}
class ClientPublish {
    static HttpClient client;
    static async Task<T> Send<T>(HttpMethod method,string path,object data=null,bool optional=false) where T:class {
        using(var request=new HttpRequestMessage(method,"https://api.github.com/repos/"+Channel.Repo+path)) {
            if(data!=null)request.Content=new StringContent(Data.Json(data),Encoding.UTF8,"application/json");
            using(var response=await client.SendAsync(request)) {
                if(optional&&response.StatusCode==HttpStatusCode.NotFound)return null;
                if(!response.IsSuccessStatusCode)throw new IOException("GitHub HTTP "+(int)response.StatusCode);
                return Data.Read<T>(await response.Content.ReadAsStringAsync());
            }
        }
    }
    static void Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
    static async Task Run(string exe) {
        string version=System.Reflection.AssemblyName.GetAssemblyName(exe).Version.ToString(),tag="updater-v"+version;
        string hash=Data.Hash(exe);long size=new FileInfo(exe).Length;
        var releaseInfo=new ClientRelease {version=version,file=new PackFile{path=Channel.ClientName,sha256=hash,size=size,url=Channel.Web+"/releases/download/"+tag+"/"+hash+".bin"}};SelfUpdate.Validate(releaseInfo);
        var ws=new Workspace(Workspace.DefaultRoot);
        var account=CosSettings.Load(ws);if(account==null)throw new IOException("请先在管理端保存 COS 上传凭据。");Cloud.Configure(account.channel);
        using(var assemblyStream=System.Reflection.Assembly.LoadFile(Path.GetFullPath(exe)).GetManifestResourceStream("cos-channel.json")) {
            if(assemblyStream==null)throw new IOException("接收端未内置 COS 更新源，拒绝发布。");
            using(var reader=new StreamReader(assemblyStream)){var bound=Data.Read<CloudConfig>(reader.ReadToEnd());if(bound.endpoint!=account.channel.endpoint||bound.publicKey!=account.channel.publicKey)throw new IOException("接收端更新源或验签公钥不匹配。");}
        }
        string signed=CosSettings.Sign(account,"client",Data.Json(releaseInfo));
        using(var cos=new CosApi(account)) {
            string existing=await cos.Read("client-update.json");
            if(existing!=null){var previous=Data.Read<ClientRelease>(Cloud.Verify(existing,"client"));SelfUpdate.Validate(previous);if(new Version(previous.version)>new Version(version)||(previous.version==version&&previous.file.sha256!=hash))throw new IOException("不能发布较旧或同版本不同内容的接收端。");}
            await cos.Blob(exe,releaseInfo.file);
            await cos.Document("clients/"+version+".json",signed,true);
            string verifyTemp=Path.GetTempFileName();try{using(var transfer=new Transfer()){await transfer.VerifyCloudFile(releaseInfo.file,verifyTemp);if(await transfer.CloudVersion("clients/"+version+".json","client")!=Data.Json(releaseInfo))throw new IOException("COS 接收端公开清单不匹配。");}}finally{if(File.Exists(verifyTemp))File.Delete(verifyTemp);}
            await cos.Document("client-update.json",signed,false);
            using(var download=File.OpenRead(exe))await cos.Put("Lethal.company.pack.Updater.exe",download,"application/octet-stream",false);
        }
        Console.WriteLine("COS client published: "+version+"; SHA256 "+hash);
        string token=ws.Token();if(String.IsNullOrEmpty(token))throw new IOException("发布凭据未保存。");
        using(var api=new GitHubApi(token))using(client=new HttpClient()) {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LethalGoofballsPack-ClientPublisher/1.0");client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
            await api.ValidateAccount();var latest=await api.LatestRelease();var repo=await Send<RepoInfo>(HttpMethod.Get,"");if(repo.default_branch!="main")throw new IOException("Expected main branch; channel unchanged.");
            var release=await api.FindTag(tag);
            if(release==null)release=await Send<Release>(HttpMethod.Post,"/releases",new {tag_name=tag,name="Lethal company pack Updater "+version,draft=true,prerelease=false,make_latest="false",body="接收端程序更新；不改变 MOD 整合包版本。"});
            release=await api.GetRelease(release.id);
            if(!release.assets.Any(a=>a.name==hash+".bin")) {
                if(!release.draft)throw new IOException("Published client version cannot be replaced; increase ClientVersion.cs.");
                using(var file=File.OpenRead(exe))await api.Upload(release.id,hash+".bin",file,"application/octet-stream",size,hash);
                using(var file=File.OpenRead(exe))await api.Upload(release.id,"Lethal.company.pack.Updater.exe",file,"application/octet-stream",size,hash);
            }
            if(release.draft)release=await Send<Release>(new HttpMethod("PATCH"),"/releases/"+release.id,new {draft=false,make_latest="false"});
            // Verify public download before atomically moving the client-only channel pointer.
            string temp=Path.GetTempFileName();try{Cloud.Configure(null);using(var transfer=new Transfer())await transfer.Download(releaseInfo.file,temp,null);}finally{Cloud.Configure(account.channel);if(File.Exists(temp))File.Delete(temp);}
            var current=await Send<RepositoryFile>(HttpMethod.Get,"/contents/client-update.json?ref=main",null,true);
            await Send<object>(HttpMethod.Put,"/contents/client-update.json",new {message="Publish client "+version,content=Convert.ToBase64String(Encoding.UTF8.GetBytes(Data.Json(releaseInfo))),sha=current==null?null:current.sha,branch="main"});
            await api.StoreFeed("client-update.signed.json",signed);
            var after=await api.LatestRelease();if(latest.id!=after.id)throw new IOException("MOD latest release unexpectedly changed.");
            Console.WriteLine("Published client "+version+"; SHA256 "+hash+"; MOD latest unchanged: "+after.tag_name);
        }
    }
}
