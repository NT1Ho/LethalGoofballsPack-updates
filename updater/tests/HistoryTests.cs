using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Media.Imaging;
using LcPack;
class HistoryTests {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
 [STAThread] static void Main(string[] args){try{Run(args[0]);}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
 static void Render(C.Grid panel,int width,int height,string file){
  panel.Measure(new W.Size(width,height));panel.Arrange(new W.Rect(0,0,width,height));panel.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>{})); panel.UpdateLayout();
  System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>{})); panel.UpdateLayout();
  var image=new I.RenderTargetBitmap(width,height,96,96,M.PixelFormats.Pbgra32);image.Render(panel);
  var encoder=new I.PngBitmapEncoder();encoder.Frames.Add(I.BitmapFrame.Create(image));using(var output=File.Create(file))encoder.Save(output);
 }
 static void Run(string output){
  Directory.CreateDirectory(output);Application.EnableVisualStyles();
  var data=HistoryWindow.ReadHistory();Check(data.Count==6&&data[0].Version=="V1.9","six verified releases, newest first");
  Check(!data.Any(r=>r.Version=="V1.8.9.1"||r.Version=="V1.8.1"),"withdrawn and local-only versions not listed as releases");
  Check(data.SelectMany(r=>r.Changes).All(r=>!String.IsNullOrWhiteSpace(r.Summary)),"all changes have complete summaries");
  Check(data[0].Changes.Any(r=>r.Mod.StartsWith("Natural Selection（")&&r.Summary.Contains("关闭 → 开启")),"same-version giant change included");
  Check(data.Single(r=>r.Version=="V1.5").Changes.Any(r=>r.Kind=="移除"&&r.Mod.StartsWith("SnatchinBracken（")),"removed original Bracken recorded");
  Check(data.All(v=>v.Changes.Select(r=>r.Mod).Distinct().Count()==v.Changes.Count),"one entry per MOD per release");
  Check(data.SelectMany(v=>v.Changes).All(r=>r.Mod.Contains("（")&&r.Mod.EndsWith("）")),"all MOD names have Chinese names");
  Check(data[0].Changes.Count==7&&data[0].Changes.Single(r=>r.Mod.StartsWith("MoreMoneyStart（")).Summary.Contains("1000"),"V1.9 has distinct changes and visible initial funds");
  Check(data[0].Changes.Single(r=>r.Mod.Contains("MoreCounterplay（")).Summary.Contains("客户端放置提示消失"),"Jester fixes included in existing entry");
  Check(data[0].Changes.Single(r=>r.Mod.StartsWith("IndoorHazardGuard（")).Summary.Contains("室外导弹"),"outdoor missile fix recorded");
  Check(data.SelectMany(v=>v.Changes).All(r=>!System.Text.RegularExpressions.Regex.IsMatch(r.Summary,@"(?<!\d)\d+\.\d+\.\d+(?!\d)")),"MOD version numbers removed without removing configuration numbers");
  using(var client=new ClientWindow()){
   var buttons=client.Controls.OfType<Button>().ToList();Check(buttons.Count==2&&buttons.Any(b=>b.Text=="历史更新记录"),"two compact buttons");
   Check(!client.Controls.OfType<Label>().Any(l=>l.Text=="查看后保留窗口"),"obsolete hint removed");
   Check(!buttons[0].Bounds.IntersectsWith(buttons[1].Bounds)&&buttons.All(b=>client.ClientRectangle.Contains(b.Bounds)),"buttons do not overlap or clip");
   var history=buttons.Single(b=>b.Text=="历史更新记录");typeof(Button).GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(history,new object[]{EventArgs.Empty});
   Check((bool)typeof(ClientWindow).GetField("keepOpen",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(client),"history request prevents delayed auto close");
   Check((bool)typeof(ClientWindow).GetField("showHistory",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(client)&&!history.Enabled,"busy update queues history without interrupting installation");
   history.Text="历史更新记录";history.Enabled=true;foreach(var l in client.Controls.OfType<Label>()){if(l.Text.Contains("检查更新"))l.Text="已是最新版！";if(l.Text.Contains("最新版本"))l.Text="当前版本：V1.9\n最新版本：V1.9";}
   var bar=client.Controls.OfType<ProgressBar>().Single();bar.Style=ProgressBarStyle.Continuous;bar.Value=100;
   using(var bitmap=new Bitmap(client.Width,client.Height)){client.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"client.png"));}
  }
  using(var window=new HistoryWindow()){
   var panel=(C.Grid)window.Controls.OfType<ElementHost>().Single().Child;
   var combo=((C.StackPanel)panel.Children[0]).Children.OfType<C.ComboBox>().Single();var grid=panel.Children.OfType<C.DataGrid>().Single();Check(!panel.Children.OfType<C.TextBox>().Any()&&panel.RowDefinitions.Count==2,"no duplicate details area");
   foreach(var release in data){combo.SelectedIndex=data.IndexOf(release);Check(grid.Items.Count==release.Changes.Count,"version filter "+release.Version);grid.SelectedIndex=grid.Items.Count-1;}
   combo.SelectedIndex=0;Render(panel,880,280,Path.Combine(output,"history.png"));
   double large=grid.Columns.Last().ActualWidth;Console.WriteLine("width large="+large+" table="+grid.ActualWidth);Render(panel,700,240,Path.Combine(output,"history-small.png"));
   Console.WriteLine("width small="+grid.Columns.Last().ActualWidth+" table="+grid.ActualWidth);Check(grid.Columns.Last().ActualWidth<large&&grid.Columns.Last().ActualWidth>=250,"content column follows window width");
   for(int i=0;i<12;i++)Render(panel,700+i*20,500,Path.Combine(output,"resize.png"));
   Check(grid.Items.Count==data[0].Changes.Count,"resizing preserves all records");
   combo.SelectedIndex=2;grid.SelectedIndex=grid.Items.Count-1;Render(panel,880,580,Path.Combine(output,"history-config.png"));
   var top=(C.StackPanel)panel.Children[0];
   var measure=typeof(HistoryWindow).GetMethod("ContentHeight",BindingFlags.NonPublic|BindingFlags.Instance);
   var fit=typeof(HistoryWindow).GetMethod("FitHeight",BindingFlags.NonPublic|BindingFlags.Instance);
   Func<double> contentHeight=()=>Convert.ToDouble(measure.Invoke(window,new object[]{grid,top}));
   combo.SelectedIndex=0;Render(panel,880,500,Path.Combine(output,"auto-preflight.png"));
   double shortHeight=contentHeight();double wideName=grid.Columns[1].ActualWidth;
   Render(panel,880,(int)Math.Ceiling(shortHeight),Path.Combine(output,"auto-short.png"));
   Check(shortHeight<400,"few records use compact content height");
   combo.SelectedIndex=1;Render(panel,880,600,Path.Combine(output,"auto-long.png"));double longHeight=contentHeight();
   Check(longHeight>shortHeight+150,"more records produce a taller window");
   Render(panel,700,600,Path.Combine(output,"auto-narrow.png"));
   Check(contentHeight()>=longHeight&&grid.Columns[1].ActualWidth<wideName,"narrowing resizes MOD column and accounts for wrapped text");
   fit.Invoke(window,new object[]{grid,top});
   Check(window.Height<=Screen.FromControl(window).WorkingArea.Height,"auto height stays inside usable screen");
   combo.SelectedIndex=0;Render(panel,880,600,Path.Combine(output,"auto-shrink-preflight.png"));fit.Invoke(window,new object[]{grid,top});
   Check(Math.Abs(window.ClientSize.Height-shortHeight*window.DeviceDpi/96.0)<=2,"switching back to few records shrinks window");
   var area=Screen.FromControl(window).WorkingArea;
   Check(Math.Abs(window.Left+window.Width/2.0-(area.Left+area.Width/2.0))<=1&&Math.Abs(window.Top+window.Height/2.0-(area.Top+area.Height/2.0))<=1,"long-to-short version change recenters on current screen");
   window.Location=new Point(area.Left+30,area.Top+30);var manual=window.Location;
   fit.Invoke(window,new object[]{grid,top});Check(window.Location==manual,"ordinary layout does not override manual window position");
   combo.SelectedIndex=1;Render(panel,880,600,Path.Combine(output,"center-long.png"));fit.Invoke(window,new object[]{grid,top});
   area=Screen.FromControl(window).WorkingArea;
   Check(Math.Abs(window.Top+window.Height/2.0-(area.Top+area.Height/2.0))<=1,"short-to-long version change also recenters");
   grid.ItemsSource=Enumerable.Repeat(data[1].Changes[0],100).ToList();Render(panel,880,600,Path.Combine(output,"auto-cap.png"));fit.Invoke(window,new object[]{grid,top});
   Check(window.Height<=Screen.FromControl(window).WorkingArea.Height&&window.ClientSize.Height<contentHeight()*window.DeviceDpi/96.0,"excess records cap window height for scrolling");
   foreach(int width in new[]{700,880,1100})foreach(var release in data){
    combo.SelectedIndex=data.IndexOf(release);grid.ItemsSource=release.Changes;
    Render(panel,width,1600,Path.Combine(output,"measure-rows.png"));
    int fitted=(int)Math.Ceiling(contentHeight());Render(panel,width,fitted,Path.Combine(output,"tight-"+release.Version+"-"+width+".png"));
    var last=(C.DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(grid.Items.Count-1);
    double bottom=last.TransformToAncestor(panel).Transform(new W.Point(0,last.ActualHeight)).Y;
    Check(fitted-bottom>=0&&fitted-bottom<=3,"last row fully visible with no blank band: "+release.Version+" / "+width);
   }
  }
  using(var preview=new HistoryWindow()){
   var panel=(C.Grid)preview.Controls.OfType<ElementHost>().Single().Child;
   var top=(C.StackPanel)panel.Children[0];var combo=top.Children.OfType<C.ComboBox>().Single();var table=panel.Children.OfType<C.DataGrid>().Single();combo.SelectedIndex=1;
   Render(panel,700,1400,Path.Combine(output,"fresh-preflight.png"));
   double height=(double)typeof(HistoryWindow).GetMethod("ContentHeight",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(preview,new object[]{table,top});
   Render(panel,700,(int)Math.Ceiling(height),Path.Combine(output,"fresh-tight.png"));
   using(var bitmap=new Bitmap(Path.Combine(output,"fresh-tight.png")))Check(bitmap.GetPixel(5,5).A==255,"preview is visibly rendered");
  }
  Console.WriteLine("PASS: offline UI checks; no visible windows, game or network started.");
 }
}
