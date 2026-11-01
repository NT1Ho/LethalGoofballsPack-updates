using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;
using LcPack;

class UiTests {
    static int checks;static string output;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    static T Field<T>(PublisherWindow window,string name){return (T)typeof(PublisherWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);}
    static object Call(PublisherWindow window,string name,params object[] args){return typeof(PublisherWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,args);}
    [STAThread]public static void Main(string[] args){try{UiStartup.Run(()=>Run(args));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static void Pump(){Application.DoEvents();W.Threading.Dispatcher.CurrentDispatcher.Invoke(new Action(()=>{}),W.Threading.DispatcherPriority.ApplicationIdle);}
    static void Wait(Task work){var clock=Stopwatch.StartNew();while(!work.IsCompleted&&clock.Elapsed.TotalSeconds<15){Pump();Thread.Sleep(1);}if(!work.IsCompleted)throw new Exception("UI work did not complete");work.GetAwaiter().GetResult();Pump();}
    static System.Windows.Interop.HwndSource Surface(PublisherWindow window,out C.DockPanel body){
        var h=window.Handle;window.PerformAutoScale();window.PerformLayout();var host=window.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().Single();body=(C.DockPanel)host.Child;host.Child=null;
        var surface=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Offscreen admin checks"){PositionX=-30000,PositionY=-30000,Width=1600,Height=1000,WindowStyle=unchecked((int)0x90000000),ExtendedWindowStyle=0x08000080});surface.RootVisual=body;Layout(body,920,560);SynchronizationContext.SetSynchronizationContext(new W.Threading.DispatcherSynchronizationContext(body.Dispatcher));return surface;
    }
    static void Layout(C.DockPanel body,double width,double height){body.Width=width;body.Height=height;body.Measure(new W.Size(width,height));body.Arrange(new W.Rect(0,0,width,height));body.UpdateLayout();Pump();}
    static void Save(C.DockPanel body,string name,double dpi=96){var image=new I.RenderTargetBitmap((int)(body.ActualWidth*dpi/96),(int)(body.ActualHeight*dpi/96),dpi,dpi,M.PixelFormats.Pbgra32);image.Render(body);var encoder=new I.PngBitmapEncoder();encoder.Frames.Add(I.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);}
    static int Containers(W.DependencyObject parent){int count=0;for(int i=0;i<M.VisualTreeHelper.GetChildrenCount(parent);i++){var child=M.VisualTreeHelper.GetChild(parent,i);if(child is C.ListViewItem)count++;count+=Containers(child);}return count;}
    static void ToolbarFits(PublisherWindow window){var toolbar=Field<C.Grid>(window,"toolbar");double last=0;foreach(var child in toolbar.Children.Cast<W.FrameworkElement>().OrderBy(C.Grid.GetColumn)){var origin=child.TranslatePoint(new W.Point(),toolbar);Check(origin.X>=last-1&&origin.X+child.ActualWidth<=toolbar.ActualWidth+1,"toolbar control fits: "+child.GetType().Name);last=origin.X+child.ActualWidth;}}
    static void Run(string[] args){
        output=Path.Combine(Path.GetTempPath(),"lcadmin-ui-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(output);
        var ws=new Workspace(Path.Combine(output,"synthetic"));for(int i=0;i<10000;i++)ws.Current.files.Add(new PackFile{path="BepInEx/plugins/test-"+i.ToString("D5")+".dll",size=i,url="https://example.invalid/published"});ws.Current.baseVersion="V1.7.8";ws.Save();
        using(var window=new PublisherWindow(ws)){
            C.DockPanel body;using(var surface=Surface(window,out body)){
                var list=Field<AdminTable>(window,"list");var selector=Field<C.ComboBox>(window,"fileView");var remove=Field<C.Button>(window,"remove");var version=Field<C.TextBox>(window,"version");
                Check(window.Text=="Lethal company pack Updater Admin","English application title retained");Check(list.Items.Count==10000&&((C.GridView)list.View).Columns.Count==6,"opens in component view");
                Check(body.AllowDrop,"file and folder drop target retained");Check(!remove.IsEnabled,"remove is disabled without selection");
                var toolbar=Field<C.Grid>(window,"toolbar");Check(toolbar.Children.OfType<C.Button>().Count()==4&&toolbar.Children.OfType<C.Button>().Count(b=>b.ContextMenu!=null)==2,"compact toolbar keeps four primary buttons with add and more menus");
                selector.SelectedIndex=1;Pump();Check(((C.GridView)list.View).Columns.Count==3&&list.Items.Count==10000,"file view lists 10000 paths");
                Check(C.VirtualizingPanel.GetIsVirtualizing(list)&&Containers(list)<100,"only visible rows are rendered");Check(list.Items.Cast<AdminRow>().All(r=>r.Status==""),"unchanged rows omit repetitive published status");
                var last=(AdminRow)list.Items[9999];list.SelectedItems.Add(last);list.ScrollIntoView(last);Pump();list.SelectedItems.Add(list.Items[7]);
                var paths=(string[])Call(window,"SelectedPaths");Check(paths.Length==2&&paths.Contains(last.Name)&&paths.Contains("BepInEx/plugins/test-00007.dll"),"multi-selection maps exact relative paths");Check(remove.IsEnabled,"selection enables remove");
                remove.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));var wait=Stopwatch.StartNew();while(Field<bool>(window,"busy")&&wait.Elapsed.TotalSeconds<10){Pump();Thread.Sleep(1);}
                Check(!Field<bool>(window,"busy")&&ws.Current.files.Count==9998&&ws.Current.deletes.Count==2,"remove action creates only selected deletion markers");Check(list.Items.Cast<AdminRow>().Count(r=>r.Status=="待删除")==2&&list.SelectedItems.Count==0,"deletion status replaces rows without stale selection");
                string imported=Path.Combine(output,"input","BepInEx","plugins","Added.dll");Directory.CreateDirectory(Path.GetDirectoryName(imported));File.WriteAllText(imported,"isolated import fixture");Wait((Task)Call(window,"Import",new object[]{new[]{imported}}));
                Check(ws.Current.files.Any(f=>f.path=="BepInEx/plugins/Added.dll")&&File.ReadAllText(imported)=="isolated import fixture","import keeps relative path and source file");Check(list.Items.Cast<AdminRow>().Any(r=>r.Name.EndsWith("Added.dll")&&r.Status=="待发布"),"new import is marked pending");version.Text="V1.8";Check(Publisher.VersionName(version.Text)=="V1.8","release version remains editable");
                for(int i=0;i<40;i++){list.ScrollIntoView(list.Items[i%2==0?0:list.Items.Count-1]);Pump();}Check(Containers(list)<100,"scrolling recycles visible rows");
                foreach(int dpi in new[]{96,120,144,192}){body.LayoutTransform=new M.ScaleTransform(dpi/96.0,dpi/96.0);Layout(body,840,500);ToolbarFits(window);Check(list.Items.Count==10001,"all rows retained at simulated "+dpi+" DPI");}body.LayoutTransform=M.Transform.Identity;Layout(body,920,560);Save(body,"files");
            }
        }
        if(args.Length>0)Catalog(args[0]);Console.WriteLine("RESULT: "+checks+" admin checks passed; hidden isolated UI only, no live publish or foreground control.");Console.WriteLine("RENDERS: "+output);
    }
    static void Catalog(string sourceRoot){
        var source=new Workspace(sourceRoot);var ws=new Workspace(Path.Combine(output,"catalog"));ws.Current=Data.Read<Draft>(Data.Json(source.Current));ws.Save();foreach(var f in ws.Current.files.Where(f=>f.path.EndsWith("/original-files.json")||f.path.EndsWith("/download-sources.json")))File.Copy(source.ObjectPath(f.sha256),ws.ObjectPath(f.sha256));
        using(var window=new PublisherWindow(ws)){
            C.DockPanel body;using(var surface=Surface(window,out body)){
                var list=Field<AdminTable>(window,"list");var rows=list.Items.Cast<AdminRow>().ToList();
                Check(rows.SelectMany(r=>r.Paths).OrderBy(p=>p).SequenceEqual(ws.Current.files.Select(f=>f.path).Concat(ws.Current.deletes).Distinct().OrderBy(p=>p)),"every real publication file is mapped once");Check(rows.All(r=>!String.IsNullOrWhiteSpace(r.Purpose)&&r.Purpose!="暂无用途说明"),"all current component descriptions retained");
                var fork=rows.Single(r=>r.Name=="SnatchinBrackenFORK");list.SelectedItem=fork;Check(fork.Version=="2.0.4"&&((string[])Call(window,"SelectedPaths")).SequenceEqual(fork.Paths),"real MOD selection retains its version and files");list.UnselectAll();Save(body,"admin-default");Save(body,"admin-150dpi",144);
                var timings=new List<double>();for(int i=0;i<120;i++){var clock=Stopwatch.StartNew();Layout(body,840+i%40*12,500+i%10*8);timings.Add(clock.Elapsed.TotalMilliseconds);}Check(list.Items.Count==rows.Count,"120 continuous resizes preserve every component");
                var columns=((C.GridView)list.View).Columns;Check(columns[0].ActualWidth>205&&columns[2].ActualWidth>230,"MOD and purpose columns expand with window");ToolbarFits(window);Save(body,"admin-wide");Layout(body,840,500);ToolbarFits(window);Save(body,"admin-small");
                Console.WriteLine("LAYOUT median="+timings.OrderBy(v=>v).ElementAt(60).ToString("0.00")+" ms; p95="+timings.OrderBy(v=>v).ElementAt(114).ToString("0.00")+" ms; hardware tier="+(M.RenderCapability.Tier>>16));Console.WriteLine("LIMIT: offscreen timings, not on-screen FPS or subjective drag smoothness.");
            }
        }
    }
}
