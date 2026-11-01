using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class FirstByteStream:MemoryStream {
    readonly Action read;bool first=true;
    public FirstByteStream(byte[] data,Action read):base(data){this.read=read;}
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
        if(first){first=false;read();count=1;}
        return base.ReadAsync(buffer,offset,count,token);
    }
}
class DelayedStart:HttpMessageHandler {
    public Manifest manifest;public byte[] bytes;public bool received;public Action beforeHeaders;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        HttpContent content;
        if(request.RequestUri.AbsolutePath.EndsWith("manifest.json"))content=new StringContent(Data.Json(manifest));
        else {beforeHeaders();await Task.Delay(100,token);content=new StreamContent(new FirstByteStream(bytes,()=>received=true));content.Headers.ContentLength=bytes.Length;}
        return new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=content};
    }
}
class DownloadStartTests {
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
    static void Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
    static async Task Run(string exe){
        string root=Path.Combine(Path.GetTempPath(),"lcpack-download-start-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data/Managed"));
        foreach(string p in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,p),"fixture");
        var release=SelfUpdate.Describe(exe);byte[] data=File.ReadAllBytes(exe);
        var mod=new PackFile{path="BepInEx/plugins/test.dll",url=release.file.url,sha256=release.file.sha256,size=data.Length};
        foreach(bool self in new[]{false,true}){
            var events=new List<Tuple<string,int>>();var handler=new DelayedStart{bytes=data,manifest=new Manifest{version="test",files=new List<PackFile>{mod}}};
            handler.beforeHeaders=()=>Check(events.Last().Item2==-1&&events.Last().Item1.Contains("连接"),"waiting for headers shows connection state, not 0%");
            Action<string,int> progress=(text,value)=>{if(value>=0&&!handler.received)throw new Exception("Percent shown before any data");events.Add(Tuple.Create(text,value));};
            using(var t=new Transfer(handler)){
                if(self){Directory.CreateDirectory(SelfUpdate.Home(root));await SelfUpdate.Prepare(root,release,t,progress);SelfUpdate.Verify(SelfUpdate.CachedExe(root,release),release);}
                else await new Engine(root,()=>{}).Run(t,progress);
            }
            Check(events.Any(e=>e.Item1.Contains("不足 1%")),"first byte displayed as under 1%, not rounded to 0%");
            Check(events.Any(e=>e.Item1.Contains("选择下载线路")),"selection phase shown separately");
            Console.WriteLine("FLOW "+(self?"client":"MOD")+": "+String.Join(" -> ",events.Take(5).Select(e=>e.Item1)));
        }
    }
}
