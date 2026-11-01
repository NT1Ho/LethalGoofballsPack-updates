using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

class AdminLiveWindow {
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect bounds);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    static AutomationElement Find(AutomationElement parent,string name,ControlType type){
        return parent.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.NameProperty,name),new PropertyCondition(AutomationElement.ControlTypeProperty,type)));
    }
    static void Capture(Process process,string output,string name){
        process.Refresh();Rect r;GetWindowRect(process.MainWindowHandle,out r);
        using(var b=new Bitmap(r.Right-r.Left,r.Bottom-r.Top)){using(var g=Graphics.FromImage(b)){var dc=g.GetHdc();try{if(!PrintWindow(process.MainWindowHandle,dc,2))throw new Exception("PrintWindow failed");}finally{g.ReleaseHdc(dc);}}b.Save(Path.Combine(output,name+".png"));}
    }
    [STAThread]static int Main(string[] args){
        string exe=Path.GetFullPath(args[0]),workspace=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
        using(var p=Process.Start(new ProcessStartInfo(exe,"--workspace \""+workspace+"\""){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})){
            try{
                for(int i=0;i<150&&p.MainWindowHandle==IntPtr.Zero;i++){Thread.Sleep(100);p.Refresh();}
                if(p.MainWindowHandle==IntPtr.Zero||p.MainWindowTitle!="Pack Updater 管理端 · 2.0")throw new Exception("Expected admin window");
                var window=AutomationElement.FromHandle(p.MainWindowHandle);
                string[] labels={"整合包内容","接收端资料","发布中心","渠道设置"},images={"catalog-live","history-live","release-live","channels-live"};
                for(int i=0;i<labels.Length;i++){
                    var tab=Find(window,labels[i],ControlType.TabItem);
                    ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();Thread.Sleep(160);
                    if(i==3){
                        var button=Find(window,"核对在线版本",ControlType.Button);
                        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        for(int j=0;j<160;j++){Thread.Sleep(100);if(window.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"当前工作区基线与正式清单一致。"))!=null)break;}
                        // Only this read-only operation may contact the public services.
                    }
                    Capture(p,output,images[i]);Console.WriteLine("Captured "+labels[i]);
                }
                if(!p.CloseMainWindow()||!p.WaitForExit(6000))throw new Exception("Manual close failed");
                Console.WriteLine("PASS normal EXE entry, all four tabs and close");return 0;
            }catch(Exception e){Console.Error.WriteLine(e.Message);if(!p.HasExited)p.CloseMainWindow();return 1;}
        }
    }
}
