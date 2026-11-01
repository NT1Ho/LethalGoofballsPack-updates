using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class WindowPolishTests {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern uint ExtractIconExW(string file,int index,IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    static int passed;
    static string output;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+(++passed)+" "+name);}
    static IEnumerable<Control> Controls(Control form){
        foreach(Control child in form.Controls){yield return child;foreach(var nested in Controls(child))yield return nested;}
    }
    static void Pump(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(15);}}
    static void Render(Form form,string filename){
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,filename));}
    }
    static void TestUpdate(string root,string expected){
        using(var home=new ClientWindow(true,root){Opacity=0,ShowInTaskbar=false}){
            home.Show();var busy=typeof(ClientWindow).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic);
            var watch=Stopwatch.StartNew();
            do{Pump(50);}while((bool)busy.GetValue(home)&&watch.Elapsed.TotalMinutes<3);
            var message=Controls(home).OfType<Label>().First().Text;
            Check(message.Contains(expected),"actual update outcome "+message);
            Pump(3300);
            Check(home.Visible&&!home.IsDisposed&&home.Text=="Pack Updater · "+SelfUpdate.Version,"correct live title and window remains after old close deadline");
            Check(new[]{"查看 MOD","历史更新记录","故障诊断"}.All(t=>Controls(home).OfType<Button>().Any(b=>b.Text==t&&b.Enabled&&b.Visible)),"all three entrances remain available");
            Render(home,"home.png");
            home.Close();Check(home.IsDisposed,"user close exits completed window");
        }
    }
    static bool Identical(Icon a,Icon b){
        using(var x=a.ToBitmap())using(var y=b.ToBitmap()){
            if(x.Size!=y.Size)return false;
            for(int i=0;i<x.Width;i++)for(int j=0;j<x.Height;j++)if(x.GetPixel(i,j)!=y.GetPixel(i,j))return false;
            return true;
        }
    }
    static void Dialogs(string root){
        using(var diagnostics=new DiagnosticsWindow(root){Opacity=0,ShowInTaskbar=false}){
            diagnostics.Show();Pump(200);
            var registry=@"HKEY_CLASSES_ROOT\CLSID\{9343812E-1C37-4A49-A12E-4B2D810D956B}\DefaultIcon";
            string source=(string)Microsoft.Win32.Registry.GetValue(registry,"",null);int comma=source.LastIndexOf(',');IntPtr h;
            ExtractIconExW(Environment.ExpandEnvironmentVariables(source.Substring(0,comma)),Int32.Parse(source.Substring(comma+1)),IntPtr.Zero,out h,1);
            try{Check(h!=IntPtr.Zero&&diagnostics.ShowIcon&&Identical(diagnostics.Icon,Icon.FromHandle(h)),"diagnostics icon pixels match registered Explorer SearchHome resource");}finally{if(h!=IntPtr.Zero)DestroyIcon(h);}
            Render(diagnostics,"diagnostics.png");
            var timer=new System.Windows.Forms.Timer{Interval=80};Exception failure=null;
            timer.Tick+=(s,e)=>{
                var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==diagnostics);
                if(dialog==null)return;timer.Stop();
                try{
                    var text=Controls(dialog).OfType<Label>().Single();var ok=Controls(dialog).OfType<Button>().Single();
                    Check(dialog.Width>=2.4*dialog.Height&&text.Text=="诊断包已导出。","export confirmation is wide and readable");
                    var label=dialog.RectangleToClient(text.RectangleToScreen(text.ClientRectangle));
                    var button=dialog.RectangleToClient(ok.RectangleToScreen(ok.ClientRectangle));
                    Check(dialog.ClientRectangle.Contains(label)&&dialog.ClientRectangle.Contains(button)&&label.Bottom<=button.Top,"text and confirmation button fit without overlap");
                    Render(dialog,"export.png");ok.PerformClick();
                }catch(Exception ex){failure=ex;dialog.Close();}
            };
            using(timer){timer.Start();var dialogs=typeof(DiagnosticsWindow).Assembly.GetType("LcPack.DiagnosticDialogs");
                dialogs.GetMethod("ExportComplete",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{diagnostics});}
            if(failure!=null)throw failure;
            Check(diagnostics.Visible,"confirm returns to diagnosis");diagnostics.Close();
        }
    }
    [STAThread]static int Main(string[] args){
        try{output=Path.GetFullPath(args[1]);UiStartup.Run(()=>{TestUpdate(args[0],"安装成功");TestUpdate(args[0],"已是最新版");Dialogs(args[0]);});return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
