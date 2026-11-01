using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LcPack;
class PublishedHandoffTest {
    delegate bool EnumProc(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,EnumProc cb,IntPtr arg);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
    static bool LatestMessage(Process p) {
        bool found=false;
        EnumChildWindows(p.MainWindowHandle,(h,arg)=>{var s=new StringBuilder(256);GetWindowText(h,s,s.Capacity);if(s.ToString()=="已是最新版！")found=true;return !found;},IntPtr.Zero);
        return found;
    }
    static int Main(string[] args){
        string root=Path.GetFullPath(args[0]),old=Path.Combine(root,Channel.ClientName);
        var previous=SelfUpdate.State(root).active??SelfUpdate.Describe(old);var expected=SelfUpdate.Describe(typeof(SelfUpdate).Assembly.Location);
        if(new Version(previous.version)>=new Version(expected.version))throw new Exception("Use an isolated previous-version root with an older active version.");
        using(var launch=Process.Start(new ProcessStartInfo(old){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root})){
            if(!launch.WaitForExit(20000)||launch.ExitCode!=0)throw new Exception("Bootstrap failed");
        }
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed.TotalMinutes<3){
            var state=SelfUpdate.State(root);
            if(state.active!=null&&state.active.file.sha256==expected.file.sha256&&state.previous!=null&&state.previous.file.sha256==previous.file.sha256){
                string path=SelfUpdate.CachedExe(root,state.active);
                foreach(var p in Process.GetProcessesByName("client"))using(p){
                    if(p.HasExited||!String.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase)||p.MainWindowHandle==IntPtr.Zero||!LatestMessage(p))continue;
                    Thread.Sleep(3400);p.Refresh();
                    if(p.HasExited||p.MainWindowTitle!="Pack Updater · "+SelfUpdate.ReleaseLabel(expected.version))throw new Exception("Completed window did not persist");
                    if(!p.CloseMainWindow()||!p.WaitForExit(5000))throw new Exception("Manual window close failed");
                    Console.WriteLine("PASS official "+previous.version+" launched normally, fetched and handed off to "+expected.version+"; previous hash retained. New window stayed open after the complete pack check and closed on request.");
                    return 0;
                }
            }
            Thread.Sleep(200);
        }
        throw new Exception("Timed out waiting for production receiver handoff");
    }
}
