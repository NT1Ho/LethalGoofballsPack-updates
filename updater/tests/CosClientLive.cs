using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class OnlyCos:DelegatingHandler {
    public int blobs;public bool allowGithub;public string source;
    public OnlyCos(bool allow):base(new HttpClientHandler{AllowAutoRedirect=allow,MaxAutomaticRedirections=5}){allowGithub=allow;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        if(!allowGithub&&!Cloud.IsUrl(request.RequestUri))throw new HttpRequestException("GitHub disabled by test transport");
        if(request.Headers.Authorization!=null)throw new InvalidDataException("Authenticated client request");
        if(request.RequestUri.AbsolutePath.EndsWith(".bin")&&request.Headers.Range==null){Interlocked.Increment(ref blobs);source=Cloud.IsUrl(request.RequestUri)?"Hong Kong COS":"GitHub";}
        return base.SendAsync(request,token);
    }
}
class CosClientLive {
    static void Main(string[] args){try{Run(args).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
    static async Task Run(string[] args){
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(SelfUpdate.Home(root));
        var handler=new OnlyCos(args.Length>2&&args[2]=="auto");using(var transfer=new Transfer(handler)){
            var release=await transfer.ClientLatest();if(release.version!=System.Reflection.AssemblyName.GetAssemblyName(args[1]).Version.ToString()||release.file.sha256!=Data.Hash(args[1]))throw new IOException("Published client mismatch");
            await SelfUpdate.Prepare(root,release,transfer,(s,p)=>{});
            if(handler.blobs!=1)throw new IOException("Expected one COS executable download");
            SelfUpdate.Verify(SelfUpdate.CachedExe(root,release),release);
            Console.WriteLine("PASS signed live COS client feed and anonymous EXE download; version "+release.version+"; selected source: "+handler.source);
            await SelfUpdate.Prepare(root,release,transfer,(s,p)=>{});
            if(handler.blobs!=1)throw new IOException("Unchanged executable downloaded again");
            Console.WriteLine("PASS verified cached executable reused; no upload credentials sent; GitHub allowed: "+handler.allowGithub);
        }
    }
}
