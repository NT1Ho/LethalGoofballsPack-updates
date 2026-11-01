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
using C=System.Windows.Controls;
using W=System.Windows;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class ReceiverPresentationTests {
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr arg,IntPtr dpi);
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr ChangeDpi(IntPtr hwnd,uint msg,IntPtr arg,ref Rect suggested);
    static int count;
    static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
    static void Check(bool v,string text){if(!v)throw new Exception(text);Console.WriteLine("PASS "+(++count)+" "+text);}
    static void Pump(){Application.DoEvents();Thread.Sleep(20);Application.DoEvents();}
    static IEnumerable<T> Visuals<T>(W.DependencyObject p) where T:W.DependencyObject{
        for(int i=0;i<M.VisualTreeHelper.GetChildrenCount(p);i++){var c=M.VisualTreeHelper.GetChild(p,i);if(c is T)yield return (T)c;foreach(var n in Visuals<T>(c))yield return n;}
    }
    static void Render(W.FrameworkElement c,string file){
        c.UpdateLayout();var b=new I.RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(c.ActualWidth)),Math.Max(1,(int)Math.Ceiling(c.ActualHeight)),96,96,M.PixelFormats.Pbgra32);
        b.Render(c);var png=new I.PngBitmapEncoder();png.Frames.Add(I.BitmapFrame.Create(b));using(var s=File.Create(file))png.Save(s);
    }
    static void Run(string folder){
        string root=Path.Combine(Path.GetTempPath(),"lgp-reading-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data/Managed"));
        foreach(string n in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,n),"fixture");
        string info=Path.Combine(root,"BepInEx/pack-info");Directory.CreateDirectory(info);
        var pages=PageContent.Load(Path.Combine(folder,"edited.json"));
        string multi=pages.Find("Gamma").Purpose;
        var records=new List<SourceFile>();var packages=new List<PackageRecord>();var state=new LocalState();
        foreach(var m in pages.Mods){
            string relative="BepInEx/plugins/"+m.Key+"/resource.bin", full=Path.Combine(root,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));File.WriteAllText(full,m.Key);
            records.Add(new SourceFile{package="Test-"+m.Key,path=relative,sha256=Data.Hash(full)});
            packages.Add(new PackageRecord{package="Test-"+m.Key,version="1.2.3",dependencies=new string[0]});
            if(m.Key=="Extra")state.updatedPaths.Add(relative);
        }
        Data.Atomic(Path.Combine(info,"original-files.json"),Data.Json(records));Data.Atomic(Path.Combine(info,"download-sources.json"),Data.Json(packages));
        Data.Atomic(Path.Combine(root,PageContent.RelativePath),Data.Json(pages));Directory.CreateDirectory(Path.Combine(root,".lcpack-updater"));Data.Atomic(Path.Combine(root,".lcpack-updater/state.json"),Data.Json(state));
        using(var mods=new ModWindow(root){ShowInTaskbar=false,Opacity=0}){
            mods.Show();var list=Field<C.ListView>(mods,"list");var watch=Stopwatch.StartNew();while(list.Items.Count==0&&watch.ElapsedMilliseconds<6000)Pump();
            var items=list.Items.Cast<ModWindow.ModRow>().ToArray();
            Check(items.Select(i=>i.Name).SequenceEqual(new[]{"Delta",pages.Find("Gamma").Name,"Extra","Beta","Alpha"}),"normal receiver disk scan and legacy automatic NEW match editor ordering");
            items[1].IsExpanded=true;
            foreach(int dpi in new[]{96,120,144,192}){
                var rect=new Rect{Left=20,Top=20,Right=20+715*dpi/96,Bottom=20+460*dpi/96};ChangeDpi(mods.Handle,0x2e0,new IntPtr(dpi|(dpi<<16)),ref rect);Pump();list.UpdateLayout();
                var row=(C.ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(1);var texts=Visuals<C.TextBlock>(row).Where(t=>t.Text==multi).ToList();
                Check(mods.DeviceDpi==dpi&&texts.Count>=2&&texts.All(t=>t.TextWrapping==W.TextWrapping.Wrap&&t.ActualHeight>t.FontSize*3),"visible name and purpose retain four wrapped lines at "+dpi+" DPI");
            }
            Render(list,Path.Combine(folder,"receiver-mods.png"));mods.Close();
        }
        using(var history=new HistoryWindow(pages.History,pages){ShowInTaskbar=false,Opacity=0}){
            history.Show();Pump();var host=history.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().Single();
            var panel=(W.FrameworkElement)host.Child;panel.UpdateLayout();
            Check(Visuals<C.TextBlock>(panel).Count(t=>t.Text==multi&&t.ActualHeight>3*t.FontSize)>=2,"receiver history displays both multiline subject and full summary");
            Render(panel,Path.Combine(folder,"receiver-history.png"));history.Close();
        }
        using(var diag=new DiagnosticsWindow(root){ShowInTaskbar=false,Opacity=0}){
            diag.Show();Pump();
            var h=SendMessage(diag.Handle,0x7f,new IntPtr(0),new IntPtr(diag.DeviceDpi));
            if(h==IntPtr.Zero)h=SendMessage(diag.Handle,0x7f,new IntPtr(2),IntPtr.Zero);
            Check(h!=IntPtr.Zero,"native diagnostic window supplies a small title-bar HICON");
            using(var bitmap=Icon.FromHandle(h).ToBitmap()){
                Console.WriteLine("Native HICON "+bitmap.Width+"x"+bitmap.Height+" at "+diag.DeviceDpi+" DPI");bitmap.Save(Path.Combine(folder,"title-icon.png"));
            }
            using(var b=new Bitmap(diag.Width,diag.Height)){diag.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(folder,"diagnostics-title.png"));}
            diag.Close();
        }
    }
    [STAThread]static int Main(string[] args){try{UiStartup.Run(()=>Run(Path.GetFullPath(args[0])));Console.WriteLine("RESULT "+count+" receiver checks");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
