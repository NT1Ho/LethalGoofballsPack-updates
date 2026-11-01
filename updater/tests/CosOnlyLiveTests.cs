using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class CosOnlyTransport:DelegatingHandler {
    public int files,blocked;
    public CosOnlyTransport():base(new HttpClientHandler{AllowAutoRedirect=false}){}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        if(!Cloud.IsUrl(request.RequestUri)){Interlocked.Increment(ref blocked);throw new HttpRequestException("GitHub blocked by test");}
        if(request.Headers.Authorization!=null)throw new Exception("Client sent credentials");
        if(request.RequestUri.AbsolutePath.EndsWith(".bin")&&request.Headers.Range==null)Interlocked.Increment(ref files);
        return base.SendAsync(request,token);
    }
}
class CosOnlyLiveTests {
    static void Main(string[] args){Run(args).GetAwaiter().GetResult();}
    static async Task Run(string[] args){
        string root=Path.Combine(Path.GetTempPath(),"lcpack-cos-only-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(SelfUpdate.Home(root));Directory.CreateDirectory(root);
        var h=new CosOnlyTransport();using(var t=new Transfer(h)){
            var sw=Stopwatch.StartNew();var release=await t.ClientLatest();Console.WriteLine("PASS signed COS client feed "+release.version+" in "+sw.ElapsedMilliseconds+" ms");
            if(release.version!=args[1])throw new Exception("Unexpected published client");
            sw.Restart();await SelfUpdate.Prepare(root,release,t,(s,p)=>{});SelfUpdate.Verify(SelfUpdate.CachedExe(root,release),release);Console.WriteLine("PASS anonymous COS EXE download + SHA256 + assembly version in "+sw.ElapsedMilliseconds+" ms");
            int before=h.files;await SelfUpdate.Prepare(root,release,t,(s,p)=>{});if(h.files!=before)throw new Exception("Repeated EXE download");Console.WriteLine("PASS unchanged EXE reused without download");
            sw.Restart();var manifest=await t.Latest();Console.WriteLine("PASS signed COS MOD feed "+manifest.version+" in "+sw.ElapsedMilliseconds+" ms");
            var mod=manifest.files.First(f=>f.path.EndsWith("CoilHeadStare.dll",StringComparison.OrdinalIgnoreCase));
            sw.Restart();await t.Download(mod,Path.Combine(root,"mod-sample.bin"),null);Console.WriteLine("PASS actual MOD DLL downloaded from COS + SHA256 in "+sw.ElapsedMilliseconds+" ms");
            before=h.files;sw.Restart();var result=await new Engine(args[0],()=>{}).Run(t,(s,p)=>{});
            if(!result.latest||h.files!=before)throw new Exception("Fixture was not latest or redownloaded MODs");
            Console.WriteLine("PASS current V1.7.8 fixture: "+sw.ElapsedMilliseconds+" ms, zero MOD downloads; GitHub requests blocked="+h.blocked);
            Console.WriteLine("LIMIT: real COS traffic from this machine only, not a mainland ISP test.");
        }
    }
}
