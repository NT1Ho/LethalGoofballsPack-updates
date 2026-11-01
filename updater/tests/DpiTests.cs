using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LcPack;

class DpiTests {
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    static int passed;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    [STAThread] static void Main(){
        string config=null;
        try {UiStartup.Run(()=>{config=(string)AppDomain.CurrentDomain.GetData("APP_CONFIG_FILE");Test();});Check(!File.Exists(config),"temporary framework configuration removed on exit");Console.WriteLine("RESULT: "+passed+" DPI checks passed.");}
        catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Test(){
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dpi-runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var ws=new Workspace(root);ws.Current.baseVersion="V1.5";for(int i=0;i<40;i++)ws.Current.files.Add(new PackFile {path="BepInEx/plugins/Example-"+i+".dll",size=1024});ws.Save();
        using(var admin=new PreviewAdmin(ws))CheckForm(admin,920,560,"admin",root);
        using(var client=new PreviewClient())CheckForm(client,440,192,"client",root);
        Console.WriteLine("RENDERS: "+root);
    }
    static void CheckForm(Form f,int width,int height,string name,string root){
        // Preview subclasses suppress Shown; no update or cloud operation runs.
        f.Show();Application.DoEvents();var handle=f.Handle;uint dpi=GetDpiForWindow(handle);
        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle),new IntPtr(-4)),name+" native window is PerMonitorV2");
        Check(f.DeviceDpi==dpi,name+" framework DPI matches Windows: "+dpi);
        Check(Math.Abs(f.ClientSize.Width-width*dpi/96.0)<=2&&Math.Abs(f.ClientSize.Height-height*dpi/96.0)<=2,name+" layout matches native scaling: "+f.ClientSize);
        foreach(var box in f.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<TextBox>()))Check(box.Height>=TextRenderer.MeasureText("V1.5",box.Font).Height,name+" version text fits");
        foreach(var label in f.Controls.OfType<Label>())Check(label.Bottom<=f.ClientSize.Height&&label.Right<=f.ClientSize.Width,name+" text stays within client area");
        if(name=="client"){
            var version=f.Controls.OfType<Label>().Single(l=>l.Text.Contains("最新版本"));version.Text="当前版本：V1.5\n最新版本：V1.5";
            Check(TextRenderer.MeasureText(version.Text,version.Font,new Size(version.Width,Int32.MaxValue),TextFormatFlags.WordBreak).Height<=version.Height,"two version lines fit at native DPI");
            Check(version.Bottom<f.Controls.OfType<ProgressBar>().Single().Top,"version lines do not overlap progress bar");
        }
        using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,name+".png"));}
        foreach(int target in new[]{96,120,144,192}){
            using(var layout=name=="client"?(Form)new PreviewClient():new PreviewAdmin(new Workspace(root))){
                var h=layout.Handle;layout.PerformAutoScale();layout.Scale(new SizeF(target/(float)layout.DeviceDpi,target/(float)layout.DeviceDpi));layout.PerformLayout();
                Check(layout.Controls.Cast<Control>().All(c=>c.Left>=0&&c.Top>=0&&c.Right<=layout.ClientSize.Width+2&&c.Bottom<=layout.ClientSize.Height+2),name+" simulated "+target+" DPI bounds fit");
            }
        }
    }
}

class PreviewClient:ClientWindow {protected override void OnShown(EventArgs e){}}
class PreviewAdmin:PublisherWindow {public PreviewAdmin(Workspace ws):base(ws){} protected override void OnShown(EventArgs e){}}
