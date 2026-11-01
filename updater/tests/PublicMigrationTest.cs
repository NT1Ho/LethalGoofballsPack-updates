using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LcPack;
class PublicMigrationTest {
    delegate bool EnumProc(IntPtr window,IntPtr param);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,EnumProc callback,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int max);
    static void WaitSuccess(string root,string version) {
        bool success=false;string text="";
        for(int i=0;i<1600;i++) {
            var state=SelfUpdate.State(root);bool found=false;
            if(state.active!=null&&state.active.version==version)foreach(var p in Process.GetProcessesByName("client"))using(p) {
                if(p.HasExited||!String.Equals(p.MainModule.FileName,SelfUpdate.CachedExe(root,state.active),StringComparison.OrdinalIgnoreCase))continue;
                found=true;p.Refresh();var labels=new StringBuilder();
                EnumChildWindows(p.MainWindowHandle,(window,param)=>{var value=new StringBuilder(2048);GetWindowText(window,value,value.Capacity);labels.AppendLine(value.ToString());return true;},IntPtr.Zero);text=labels.ToString();
                if(text.Contains("已是最新版！")&&text.Contains("当前版本：V1.5"))success=true;
                if(text.Contains("更新失败")||text.Contains("启动失败")||text.Contains("暂未完成"))throw new Exception(text);
            }
            if(!found&&success)return;
            Thread.Sleep(50);
        }
        throw new Exception("Public update did not complete: "+text);
    }
    static Process Launch(string root,string exe,string args){return Process.Start(new ProcessStartInfo(exe,args){UseShellExecute=false,WorkingDirectory=root});}
    static void Main(string[] args) {
        string root=Path.GetFullPath(args[0]),target=Path.Combine(root,Channel.ClientName);bool cache=args[1]=="cache";
        try {
            var before=SelfUpdate.Describe(target);var files=Data.Load<LocalState>(Path.Combine(root,".lcpack-updater","state.json")).files;
            if(files.Count!=475||!files.All(f=>Data.Hash(Path.Combine(root,f.path))==f.sha256))throw new Exception("Fixture MODs differ");
            ClientRelease release;using(var transfer=new Transfer())release=transfer.ClientLatest().GetAwaiter().GetResult();
            if(release.version!="1.10.1.0")throw new Exception("Unexpected public client feed: "+release.version);
            if(cache) {
                SelfUpdate.Verify(SelfUpdate.CachedExe(root,before),before);
                Data.Atomic(Path.Combine(SelfUpdate.Home(root),"current.json"),Data.Json(new ClientState{active=before}));
                File.SetAttributes(target,FileAttributes.ReadOnly);
                using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))using(var process=Launch(root,target,""))WaitSuccess(root,release.version);
                if(Data.Hash(target)!=before.file.sha256||(File.GetAttributes(target)&FileAttributes.ReadOnly)==0)throw new Exception("Original launcher changed");
                Console.WriteLine("PASS public GitHub cache update "+before.version+" -> "+release.version+"; original EXE locked, readonly and byte-identical.");
            } else {
                using(var process=Launch(root,target,""))WaitSuccess(root,release.version);
                if(Data.Hash(target)!=release.file.sha256)throw new Exception("Legacy handoff did not install expected launcher");
                var backups=Directory.GetFiles(Path.Combine(root,".lcpack-updater","client-update"),"previous.exe",SearchOption.AllDirectories);
                if(!backups.Any(p=>Data.Hash(p)==before.file.sha256))throw new Exception("Legacy backup missing");
                Console.WriteLine("PASS public GitHub legacy migration "+before.version+" -> "+release.version+"; backup verified; runtime automatically started.");
            }
            if(SelfUpdate.State(root).active.file.sha256!=release.file.sha256||!files.All(f=>Data.Hash(Path.Combine(root,f.path))==f.sha256))throw new Exception("Final cache or MOD bytes differ");
            Console.WriteLine("PASS real window showed current MOD V1.5 and latest status, then exited; all 475 MOD files unchanged.");
            if(cache) {
                string statePath=Path.Combine(SelfUpdate.Home(root),"current.json"),saved=File.ReadAllText(statePath),id=Guid.NewGuid().ToString("N");
                Data.Atomic(statePath,Data.Json(new ClientState{active=before}));Process pending=null;
                try {
                    using(var held=new FileStream(Path.Combine(SelfUpdate.Home(root),"update.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
                        pending=Launch(root,SelfUpdate.CachedExe(root,release),"--run-client "+Convert.ToBase64String(Encoding.UTF8.GetBytes(root))+" "+id+" updated");
                        for(int i=0;i<200&&!File.Exists(Path.Combine(SelfUpdate.Home(root),"ready-"+id));i++)Thread.Sleep(50);
                        if(!File.Exists(Path.Combine(SelfUpdate.Home(root),"ready-"+id)))throw new Exception("Interrupted handoff did not reach readiness");
                    }
                    if(!pending.WaitForExit(10000)||pending.ExitCode!=0||SelfUpdate.State(root).active.file.sha256!=before.file.sha256)throw new Exception("Uncommitted child did not exit safely");
                    Console.WriteLine("PASS real new process exits without applying MODs when parent stops before committing cache pointer.");
                }finally{if(pending!=null)pending.Dispose();Data.Atomic(statePath,saved);}
            }
            Console.WriteLine("RESULT: public "+args[1]+" success; root="+root+"; SHA256="+release.file.sha256);
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
        finally{if(cache)File.SetAttributes(target,FileAttributes.Normal);}
    }
}
