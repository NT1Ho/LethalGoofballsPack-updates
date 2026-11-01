using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class BottomScrollTests {
    static IEnumerable<T> Find<T>(W.DependencyObject parent)where T:W.DependencyObject{for(int i=0;i<M.VisualTreeHelper.GetChildrenCount(parent);i++){var child=M.VisualTreeHelper.GetChild(parent,i);if(child is T)yield return(T)child;foreach(var match in Find<T>(child))yield return match;}}
    static void Pump(C.ListView list){list.UpdateLayout();list.Dispatcher.Invoke(new Action(()=>{}),W.Threading.DispatcherPriority.ApplicationIdle);}
    [STAThread]static void Main(string[] args){try{UiStartup.Run(()=>Run(args));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static void Run(string[] args){using(var window=new ModWindow("unused-test-root")){
        var handle=window.Handle;var host=window.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().Single();var list=(C.ListView)host.Child;host.Child=null;
        var items=Enumerable.Range(0,32).Select(i=>new ModWindow.ModRow{Name="MOD "+i,Version="1.0",Purpose="测试用途",New=i==0?"NEW":""}).ToList();list.ItemsSource=items;
        using(var surface=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Offscreen bottom scroll"){PositionX=-30000,PositionY=-30000,Width=1200,Height=900,WindowStyle=unchecked((int)0x90000000),ExtendedWindowStyle=0x08000080})){
            surface.RootVisual=list;double largest=0;
            foreach(int count in new[]{32,45,54,84})foreach(double height in new[]{310,428,433,467,530}){
                list.ItemsSource=Enumerable.Range(0,count).Select(i=>new ModWindow.ModRow{Name="MOD "+i,Version="1.0",Purpose="测试用途"}).ToList();list.Width=700;list.Height=height;list.Measure(new W.Size(700,height));list.Arrange(new W.Rect(0,0,700,height));Pump(list);
                var scroll=Find<C.ScrollViewer>(list).First();scroll.ScrollToTop();Pump(list);var panel=Find<C.VirtualizingStackPanel>(list).First();for(int i=0;i<count;i++){panel.MouseWheelDown();Pump(list);}
                var last=(C.ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(count-1);if(last==null)throw new Exception("Last row missing");
                W.DependencyObject parent=last;while(!(parent is C.ScrollContentPresenter))parent=M.VisualTreeHelper.GetParent(parent);var viewport=(C.ScrollContentPresenter)parent;var bounds=last.TransformToAncestor(viewport).TransformBounds(new W.Rect(0,0,last.ActualWidth,last.ActualHeight));double gap=viewport.ActualHeight-bounds.Bottom;largest=Math.Max(largest,gap);
                Console.WriteLine("rows="+count+" height="+height+" bottom gap="+gap.ToString("0.###")+" DIP; virtualized="+(Find<C.ListViewItem>(list).Count()<count));
                if(list.Items.Count!=count||bounds.Bottom>viewport.ActualHeight+1)throw new Exception("Missing or clipped final row");
                if(count==32&&height==428){var bitmap=new I.RenderTargetBitmap(700,428,96,96,M.PixelFormats.Pbgra32);bitmap.Render(list);var encoder=new I.PngBitmapEncoder();encoder.Frames.Add(I.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bottom.png")))encoder.Save(file);}
            }
            if(args.Length==0&&largest>1)throw new Exception("Extra bottom space: "+largest+" DIP");
            Console.WriteLine("RESULT: max bottom gap "+largest+" DIP; hidden list only.");
        }
    }}
}
