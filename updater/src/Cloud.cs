using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LcPack {
public class CloudConfig {public string endpoint;public string publicKey;}
public class SignedDocument {public int schema=1;public string kind,payload,signature;}
public static class Cloud {
    public static CloudConfig Config {get;private set;}
    static Cloud() {using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("cos-channel.json"))if(stream!=null)using(var reader=new StreamReader(stream))Configure(Data.Read<CloudConfig>(reader.ReadToEnd()));}
    public static bool Enabled {get{return Config!=null;}}
    public static void Validate(CloudConfig config) {
        if(config==null||!Regex.IsMatch(config.endpoint??"","^https://[a-z0-9][a-z0-9-]*-[0-9]+[.]cos[.]ap-(hongkong|guangzhou)[.]myqcloud[.]com$"))throw new InvalidDataException("请填写广州或中国香港 COS 的 HTTPS 默认域名，不带路径或末尾斜线。");
        using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(config.publicKey);if(!rsa.PublicOnly||rsa.KeySize<2048)throw new InvalidDataException("清单公钥无效。");}
    }
    internal static void Configure(CloudConfig config){if(config!=null)Validate(config);Config=config;}
    public static string Url(string key) {
        if(!Enabled)throw new IOException("尚未配置 COS 更新源。");
        if(!Regex.IsMatch(key??"","^[a-zA-Z0-9][a-zA-Z0-9/._-]*$")||key.Contains("..")||key.Contains("//"))throw new InvalidDataException("COS 对象路径无效。");
        return Config.endpoint+"/lcpack/"+key;
    }
    public static bool IsUrl(Uri uri){return Enabled&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Host==new Uri(Config.endpoint).Host&&uri.AbsolutePath.StartsWith("/lcpack/",StringComparison.Ordinal);}
    public static string Blob(PackFile file){if(!Regex.IsMatch(file.sha256??"","^[a-f0-9]{64}$"))throw new InvalidDataException("文件摘要无效。");return Url("objects/"+file.sha256+".bin");}
    public static byte[] SignBytes(string kind,string payload){return Encoding.UTF8.GetBytes("LethalGoofballsPack/signed-v1/"+kind+"\n"+payload);}
    public static string Verify(string text,string kind) {
        try {
            var document=Data.Read<SignedDocument>(text);
            if(document==null||document.schema!=1||document.kind!=kind||document.payload==null||document.payload.Length>7*1024*1024||document.signature==null||document.signature.Length>1024)throw new InvalidDataException("清单格式无效。");
            using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(Config.publicKey);if(!rsa.VerifyData(SignBytes(kind,document.payload),CryptoConfig.MapNameToOID("SHA256"),Convert.FromBase64String(document.signature)))throw new InvalidDataException("清单签名不正确。");}
            return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(document.payload));
        }catch(Exception e){if(!(e is ArgumentException)&&!(e is InvalidOperationException)&&!(e is FormatException)&&!(e is CryptographicException)&&!(e is IOException))throw;throw new InvalidDataException("版本清单验签失败，已停止更新。",e);}
    }
}
public class SourceUnavailableException:IOException {public readonly HttpStatusCode status;public SourceUnavailableException(HttpStatusCode status):base("下载源返回 HTTP "+(int)status+"。"){this.status=status;}}
public partial class Transfer {
    volatile int preferredSource;
    readonly SemaphoreSlim sourceGate=new SemaphoreSlim(1,1);
    async Task<bool> ProbeSource(PackFile file,string url,CancellationToken token) {
        try {
            using(var request=new HttpRequestMessage(HttpMethod.Get,url)) {
                int length=(int)Math.Min(file.size,32768);if(length>0)request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,length-1);
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)) {
                    Check(response);using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                        byte[] buffer=new byte[Math.Max(1,length)];int read=0;
                        while(read<length){int n=await ReadNetwork(input,buffer,token,length-read).ConfigureAwait(false);if(n==0)return false;read+=n;}
                        return true;
                    }
                }
            }
        }catch(Exception e){if(NetworkFailure(e)||e is IOException)return false;throw;}
    }
    async Task<bool> PreferCos(PackFile file) {
        if(preferredSource!=0)return preferredSource==1;
        await sourceGate.WaitAsync().ConfigureAwait(false);
        try {
            if(preferredSource==0)using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3))) {
                var cos=ProbeSource(file,Cloud.Blob(file),timeout.Token);var github=ProbeSource(file,file.url,timeout.Token);
                var first=await Task.WhenAny(cos,github).ConfigureAwait(false);
                if(await first.ConfigureAwait(false))preferredSource=first==cos?1:2;
                else {var other=first==cos?github:cos;preferredSource=await other.ConfigureAwait(false)?(other==cos?1:2):1;}
                timeout.Cancel();
            }
            return preferredSource==1;
        }finally{sourceGate.Release();}
    }
    public async Task PrepareDownload(PackFile file) {
        SafePath.DownloadUrl(file.url);
        if(Cloud.Enabled)await PreferCos(file).ConfigureAwait(false);
    }
    public static bool NetworkFailure(Exception error){return error is HttpRequestException||error is TaskCanceledException||error is SourceUnavailableException;}
    static void Mark(Exception error,string url,string stage){error.Data["URL"]=url;error.Data["Stage"]=stage;}
    internal static async Task<int> ReadNetwork(Stream input,byte[] buffer,CancellationToken token,int length=0) {
        using(token.Register(()=>input.Dispose()))try{return await input.ReadAsync(buffer,0,length==0?buffer.Length:length,token).ConfigureAwait(false);}
        catch(Exception e){if(token.IsCancellationRequested)throw new TaskCanceledException("下载连接超时。");if(e is IOException)throw new HttpRequestException("下载连接中断。",e);throw;}
    }
    async Task<string> TextFrom(string url,int limit,int seconds) {
        string stage="连接版本清单";
        try {
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                Check(response);stage="读取版本清单";
                using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()) {
                    byte[] buffer=new byte[32768];int count;
                    while((count=await ReadNetwork(input,buffer,timeout.Token).ConfigureAwait(false))>0){if(output.Length+count>limit)throw new InvalidDataException("版本清单过大。");output.Write(buffer,0,count);}
                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }catch(Exception e){Mark(e,url,stage);throw;}
    }
    public async Task<string> CloudDocument(string kind) {
        string name=kind=="mods"?"manifest.json":"client-update.json",text=null;Exception primary=null;
        try{text=await TextFrom(Cloud.Url(name)+"?check="+Guid.NewGuid().ToString("N"),8*1024*1024,12).ConfigureAwait(false);}
        catch(Exception e){if(!NetworkFailure(e))throw;primary=e;}
        if(primary!=null)try{text=await TextFrom("https://raw.githubusercontent.com/"+Channel.Repo+"/main/"+(kind=="mods"?"mod-update.signed.json":"client-update.signed.json")+"?check="+Guid.NewGuid().ToString("N"),8*1024*1024,8).ConfigureAwait(false);}catch(Exception fallback){fallback.Data["PrimaryURL"]=Cloud.Url(name);fallback.Data["PrimaryFailure"]=primary.GetType().Name+": "+primary.Message;throw;}
        return Cloud.Verify(text,kind);
    }
    public async Task<string> CloudVersion(string key,string kind){return Cloud.Verify(await TextFrom(Cloud.Url(key)+"?check="+Guid.NewGuid().ToString("N"),8*1024*1024,12).ConfigureAwait(false),kind);}
    public Task VerifyCloudFile(PackFile file,string destination){SafePath.DownloadUrl(file.url);return DownloadFrom(file,Cloud.Blob(file),destination,null);}
    public Task VerifyGitHubFile(PackFile file,string destination){SafePath.DownloadUrl(file.url);return DownloadFrom(file,file.url,destination,null);}
    async Task<Manifest> CloudManifest(){var manifest=Data.Read<Manifest>(await CloudDocument("mods").ConfigureAwait(false));SafePath.ValidateManifest(manifest);return manifest;}
}
}
