using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;
class RuntimeFeed:HttpMessageHandler {
    public ClientRelease release;public byte[] bytes;public int downloads;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        bool feed=request.RequestUri.GetLeftPart(UriPartial.Path)==SelfUpdate.Feed;
        if(!feed)downloads++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=feed?(HttpContent)new StringContent(Data.Json(release)):new ByteArrayContent(bytes)});
    }
}
class RuntimeLiveTest {
    delegate bool EnumProc(IntPtr window,IntPtr param);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,EnumProc callback,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int max);
    static string root;static int passed;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    static Process Launch(string path,string args){return Process.Start(new ProcessStartInfo(path,args){UseShellExecute=false,WorkingDirectory=root});}
    static void WaitSuccess() {
        string active=SelfUpdate.CachedExe(root,SelfUpdate.State(root).active);bool success=false;string text="";
        for(int i=0;i<900;i++) {
            bool found=false;
            foreach(var p in Process.GetProcessesByName("client"))using(p) {
                if(p.HasExited||!String.Equals(p.MainModule.FileName,active,StringComparison.OrdinalIgnoreCase))continue;
                found=true;p.Refresh();var labels=new StringBuilder();
                EnumChildWindows(p.MainWindowHandle,(window,param)=>{var value=new StringBuilder(2048);GetWindowText(window,value,value.Capacity);labels.AppendLine(value.ToString());return true;},IntPtr.Zero);text=labels.ToString();
                if((text.Contains("已是最新版！")||text.Contains("安装成功！"))&&text.Contains("当前版本：V1.5"))success=true;
                if(text.Contains("更新失败")||text.Contains("启动失败")||text.Contains("暂未完成"))throw new Exception("Real client reported: "+text);
            }
            if(!found&&success)return;
            Thread.Sleep(50);
        }
        throw new Exception("Real runtime did not complete: "+text);
    }
    static void Main(string[] args) {
        try {
            root=Path.GetFullPath(args[0]);string target=Path.Combine(root,Channel.ClientName),hash=Data.Hash(target),next=args[1];
            var files=Data.Load<LocalState>(Path.Combine(root,".lcpack-updater","state.json")).files;
            Check(files.Count==475&&files.All(f=>Data.Hash(Path.Combine(root,f.path))==f.sha256),"fixture starts with all 475 published MOD files verified");
            SelfUpdate.Bootstrap(root,target,Launch);WaitSuccess();Check(true,"real cache runtime launched and verified MOD V1.5");
            var release=SelfUpdate.Describe(next);var feed=new RuntimeFeed{release=release,bytes=File.ReadAllBytes(next)};
            File.SetAttributes(target,FileAttributes.ReadOnly);
            try {
                using(var held=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    using(var transfer=new Transfer(feed))Check(SelfUpdate.Check(root,transfer,(s,p)=>{}).GetAwaiter().GetResult(),"real runtime update accepted while original EXE is locked and readonly");
                    WaitSuccess();Check(SelfUpdate.State(root).active.version==release.version&&feed.downloads==1,"new real process started, checked MODs, and exited normally");
                    SelfUpdate.Bootstrap(root,target,Launch);WaitSuccess();Check(SelfUpdate.State(root).active.version==release.version,"unchanged original launcher starts cached newer version on next double-click");
                    Check(Data.Hash(target)==hash&&(File.GetAttributes(target)&FileAttributes.ReadOnly)!=0,"original launcher bytes and readonly flag unchanged");
                }
            }finally{File.SetAttributes(target,FileAttributes.Normal);}
            Check(files.All(f=>Data.Hash(Path.Combine(root,f.path))==f.sha256),"all 475 MOD files remain identical after both runtime versions");
            Console.WriteLine("RESULT: "+passed+" real-process checks passed. Client payload feed was a fixture; MOD verification used public GitHub. Root: "+root+"; cache: "+SelfUpdate.Home(root));
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
