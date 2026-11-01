using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class Routes:HttpMessageHandler {
    public byte[] payload;public int cosDelay,githubDelay,probes,cosFiles,githubFiles;public bool githubBlocked,cosFails,corruptCos,cosDisconnect;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token){
        bool cos=Cloud.IsUrl(r.RequestUri),probe=r.Headers.Range!=null;
        if(probe){Interlocked.Increment(ref probes);await Task.Delay(cos?cosDelay:githubDelay,token);}
        else if(cos)Interlocked.Increment(ref cosFiles);else Interlocked.Increment(ref githubFiles);
        if(cos&&!probe&&cosDisconnect)throw new HttpRequestException("connection failed");
        if((!cos&&githubBlocked)||(cos&&!probe&&cosFails))return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=r};
        return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=r,Content=new ByteArrayContent(cos&&corruptCos?new byte[payload.Length]:payload)};
    }
}
class RoutingTests {
    static int passed;static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static void Main(){try{Run().GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static async Task Run(){
        using(var rsa=new RSACryptoServiceProvider(2048)){rsa.PersistKeyInCsp=false;Cloud.Configure(new CloudConfig{endpoint="https://route-test-1234567890.cos.ap-hongkong.myqcloud.com",publicKey=rsa.ToXmlString(false)});}
        byte[] bytes=new byte[]{1,2,3,4};string hash;using(var sha=SHA256.Create())hash=Data.Hex(sha.ComputeHash(bytes));
        var file=new PackFile{path="BepInEx/plugins/test.dll",sha256=hash,size=bytes.Length,url=Channel.Web+"/releases/download/V1/"+hash+".bin"};string dir=Path.Combine(Path.GetTempPath(),"lcpack-routing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var china=new Routes{payload=bytes,cosDelay=5,githubDelay=1000,githubBlocked=true};using(var t=new Transfer(china)){await t.Download(file,Path.Combine(dir,"china"),null);Check(china.cosFiles==1&&china.githubFiles==0,"blocked GitHub does not delay or receive actual download");await t.Download(file,Path.Combine(dir,"china2"),null);Check(china.probes==2,"source measured once per transfer session");}
        var america=new Routes{payload=bytes,cosDelay=1000,githubDelay=5};using(var t=new Transfer(america)){await t.Download(file,Path.Combine(dir,"america"),null);Check(america.githubFiles==1&&america.cosFiles==0,"faster GitHub source wins without regional settings");}
        var fail=new Routes{payload=bytes,cosDelay=5,githubDelay=1000,cosFails=true};using(var t=new Transfer(fail)){await t.Download(file,Path.Combine(dir,"fallback"),null);Check(fail.cosFiles==1&&fail.githubFiles==1&&Data.Hash(Path.Combine(dir,"fallback"))==hash,"selected COS failure safely switches to GitHub and verifies hash");await t.Download(file,Path.Combine(dir,"fallback2"),null);Check(fail.cosFiles==1&&fail.githubFiles==2,"successful fallback is reused for remaining files");}
        var disconnect=new Routes{payload=bytes,cosDelay=5,githubDelay=1000,cosDisconnect=true};using(var t=new Transfer(disconnect)){await t.Download(file,Path.Combine(dir,"disconnect"),null);Check(disconnect.cosFiles==1&&disconnect.githubFiles==1,"connection failure switches source without retrying the failed primary");}
        var corrupt=new Routes{payload=bytes,cosDelay=5,githubDelay=1000,corruptCos=true};using(var t=new Transfer(corrupt)){bool rejected=false;try{await t.Download(file,Path.Combine(dir,"bad"),null);}catch(InvalidDataException){rejected=true;}Check(rejected&&corrupt.githubFiles==0&&!File.Exists(Path.Combine(dir,"bad")),"source selection never bypasses failed SHA256 verification");}
        Console.WriteLine("RESULT "+passed+" routing checks passed; synthetic network conditions only.");
    }
}
