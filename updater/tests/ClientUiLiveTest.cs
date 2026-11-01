using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class ClientUiLiveTest {
    delegate bool EnumProc(IntPtr window,IntPtr param);
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Point2 {public int X,Y;}
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,EnumProc callback,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int max);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point2 point);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr hdc,uint flags);
    class Item {public string text,kind;public int x,y,width,height;}
    [STAThread] static void Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            using(var launcher=Process.Start(new ProcessStartInfo(Path.Combine(root,Channel.ClientName)){UseShellExecute=false,WorkingDirectory=root})){}
            for(int i=0;i<1000;i++) {
                var active=SelfUpdate.State(root).active;
                if(active!=null&&(args.Length<3||active.version==args[2]))foreach(var process in Process.GetProcessesByName("client"))using(process) {
                    if(process.HasExited||!String.Equals(process.MainModule.FileName,SelfUpdate.CachedExe(root,active),StringComparison.OrdinalIgnoreCase))continue;
                    process.Refresh();IntPtr window=process.MainWindowHandle;if(window==IntPtr.Zero)continue;
                    var origin=new Point2();ClientToScreen(window,ref origin);var items=new List<Item>();
                    EnumChildWindows(window,(child,param)=>{var text=new StringBuilder(1024);var kind=new StringBuilder(256);GetWindowText(child,text,1024);GetClassName(child,kind,256);Rect bounds;GetWindowRect(child,out bounds);items.Add(new Item{text=text.ToString(),kind=kind.ToString(),x=bounds.Left-origin.X,y=bounds.Top-origin.Y,width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top});return true;},IntPtr.Zero);
                    if(!items.Any(item=>item.text=="已是最新版！"))continue;
                    Thread.Sleep(200);
                    Rect client,outer;GetClientRect(window,out client);GetWindowRect(window,out outer);uint dpi=GetDpiForWindow(window);
                    using(var bitmap=new Bitmap(outer.Right-outer.Left,outer.Bottom-outer.Top)) {using(var graphics=Graphics.FromImage(bitmap)){IntPtr dc=graphics.GetHdc();try{PrintWindow(window,dc,2);}finally{graphics.ReleaseHdc(dc);}}bitmap.Save(Path.Combine(output,"client.png"));}
                    using(var bitmap=new Bitmap(outer.Right-outer.Left,outer.Bottom-outer.Top)) {using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(outer.Left,outer.Top,0,0,bitmap.Size);bitmap.Save(Path.Combine(output,"client-screen.png"));}
                    File.WriteAllText(Path.Combine(output,"geometry.json"),Data.Json(new{version=active.version,dpi=dpi,width=client.Right,height=client.Bottom,controls=items}));
                    double scale=dpi/96.0;var bar=items.Single(item=>item.kind.Contains("msctls_progress32"));var button=items.Single(item=>item.text=="查看 MOD");var history=items.Single(item=>item.text=="历史更新记录");var versions=items.Single(item=>item.text.Contains("当前版本："));
                    var diagnostics=items.Single(item=>item.text=="故障诊断");
                    bool ok=bar.y+bar.height<=button.y && button.y==history.y && history.y==diagnostics.y && history.x>=button.x+button.width && diagnostics.x>=history.x+history.width && diagnostics.x+diagnostics.width<=client.Right && client.Bottom-button.y-button.height<24*scale && versions.y+versions.height<=bar.y;
                    Console.WriteLine((ok?"PASS":"FAIL")+" actual cached process "+active.version+" at "+dpi+" DPI; screenshot: "+output);
                    if(!ok)Environment.ExitCode=1;return;
                }
                Thread.Sleep(50);
            }
            throw new Exception("No successful real client window captured");
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
