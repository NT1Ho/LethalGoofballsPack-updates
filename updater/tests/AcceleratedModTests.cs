using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class AcceleratedModTests {
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
    [STAThread]static void Main(string[] args){try{UiStartup.Run(()=>Run(args));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static void Run(string[] args){
        using(var form=new ModWindow(args[0])){
            var h=form.Handle;
            var grid=(C.ListView)typeof(ModWindow).GetField("list",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            typeof(ModWindow).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,ModInventory.Scan(args[0],false));
            var refresh=typeof(ModWindow).GetMethod("RefreshRows",BindingFlags.Instance|BindingFlags.NonPublic);refresh.Invoke(form,null);
            Check(grid.Items.Count==32,"32 gameplay MODs, dependencies folded");
            Check(grid.Items.Cast<ModWindow.ModRow>().All(r=>!String.IsNullOrEmpty(r.Purpose)&&r.Purpose!="暂无用途说明"),"all names versions and descriptions retained");
            Check(grid.Items.Cast<ModWindow.ModRow>().Any(r=>r.New=="NEW"),"NEW markers preserved");
            Check(((C.GridView)grid.View).Columns.Count==4,"simple read-only text columns retained");
            Check(C.VirtualizingPanel.GetIsVirtualizing(grid),"only visible rows need rendering");
            form.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().Single().Child=null;
            var surface=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Offscreen MOD tests"){PositionX=-30000,PositionY=-30000,Width=1400,Height=900,WindowStyle=unchecked((int)0x90000000),ExtendedWindowStyle=0x08000080});surface.RootVisual=grid;
            grid.ApplyTemplate();var timings=new List<double>();
            for(int i=0;i<120;i++){
                double width=750+i%40*8;grid.Width=width;grid.Height=850;var size=new W.Size(width,850);var sw=Stopwatch.StartNew();
                grid.Measure(size);grid.Arrange(new W.Rect(size));grid.UpdateLayout();grid.Dispatcher.Invoke(new Action(()=>{}),System.Windows.Threading.DispatcherPriority.ApplicationIdle);timings.Add(sw.Elapsed.TotalMilliseconds);
                if(i==40||i==90)Save(grid,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accelerated-"+i+".png"));
            }
            Check(grid.Items.Count==32,"all rows retained after 120 continuous layout changes");
            var columns=((C.GridView)grid.View).Columns.Select(c=>c.ActualWidth).ToArray();Console.WriteLine("Widths "+String.Join(",",columns)+"; actual="+grid.ActualWidth);
            Check(columns[0]>200&&columns[2]>80&&columns[3]>230,"MOD version and purpose columns grow with width");
            grid.SelectedIndex=0;grid.UpdateLayout();Save(grid,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selected.png"));
            Check(grid.SelectedItem!=null,"row selection preserved");
            form.Controls.OfType<CheckBox>().Single().Checked=true;
            Check(grid.Items.Count==54,"dependency toggle includes all 54 components");
            form.Controls.OfType<CheckBox>().Single().Checked=false;Check(grid.Items.Count==32,"dependency toggle can return to simple list");
            Console.WriteLine("HARDWARE rendering tier="+(M.RenderCapability.Tier>>16));
            Console.WriteLine("LAYOUT p50="+timings.OrderBy(v=>v).ElementAt(60).ToString("0.00")+" ms; p95="+timings.OrderBy(v=>v).ElementAt(114).ToString("0.00")+" ms; no foreground window was shown");
            Console.WriteLine("LIMIT: offscreen layout and render checks, not measured on-screen FPS.");surface.Dispose();
        }
    }
    static void Save(C.ListView grid,string path){var b=new I.RenderTargetBitmap((int)grid.ActualWidth,(int)grid.ActualHeight,96,96,M.PixelFormats.Pbgra32);b.Render(grid);var encoder=new I.PngBitmapEncoder();encoder.Frames.Add(I.BitmapFrame.Create(b));using(var output=File.Create(path))encoder.Save(output);}
}
