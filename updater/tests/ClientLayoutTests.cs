using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class ClientLayoutTests {
    [STAThread] static void Main(string[] args) {
        try {if(args.Length>0){Console.WriteLine("Early metadata access");Data.Read<ClientState>("{}");}UiStartup.Run(Run);}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Run() {
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);bool valid=true;
        foreach(bool early in new[]{false,true})using(var form=new LayoutPreview()) {
            if(early){var handle=form.Handle;}
            form.Show();Application.DoEvents();
            var version=form.Controls.OfType<Label>().Single(l=>l.Text.Contains("最新版本"));version.Text="当前版本：V1.5\n最新版本：V1.5";
            form.Controls.OfType<Label>().First().Text="已是最新版！";var bar=form.Controls.OfType<ProgressBar>().Single();bar.Style=ProgressBarStyle.Continuous;bar.Value=100;
            Application.DoEvents();string name=early?"early-handle":"normal";
            Console.WriteLine(name+" DPI="+form.DeviceDpi+" size="+form.ClientSize);
            foreach(Control control in form.Controls)Console.WriteLine(control.GetType().Name+" "+control.Bounds+" "+control.Text.Replace("\n"," / "));
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,name+".png"));}
            double scale=form.DeviceDpi/96.0;var button=form.Controls.OfType<Button>().Single();var hint=form.Controls.OfType<Label>().Last();
            bool ok=Math.Abs(bar.Left-18*scale)<=2&&Math.Abs(bar.Right-(form.ClientSize.Width-18*scale))<=2&&Math.Abs(button.Top-146*scale)<=2&&Math.Abs(hint.Top-button.Top-5*scale)<=2&&version.Bottom<bar.Top;
            Console.WriteLine((ok?"PASS ":"FAIL ")+name+" initial layout without manual autoscale");valid&=ok;
        }
        Console.WriteLine("RENDERS: "+output);if(!valid)throw new Exception("Initial client layout differs from native DPI geometry");
    }
}
class LayoutPreview:ClientWindow {protected override void OnShown(EventArgs e){}}
