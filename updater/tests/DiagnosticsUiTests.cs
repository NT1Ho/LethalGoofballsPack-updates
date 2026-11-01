using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LcPack;
using System.Runtime.InteropServices;
using System.Text;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

class DiagnosticsUiTests {
    class PreviewHome : ClientWindow { protected override void OnShown(EventArgs e){} }
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,ref NativeRect r);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)]struct NativeRect {public int Left,Top,Right,Bottom;}
    static void RenderTip(DiagnosticsTimeline line,string output){
        var tip=(ToolTip)Field(line,"tip");
        var prop=typeof(ToolTip).GetProperty("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
        IntPtr handle=(IntPtr)prop.GetValue(tip,null);NativeRect r;GetWindowRect(handle,out r);
        Check(r.Right-r.Left>150&&r.Bottom-r.Top>150,"native screenshot tooltip has a visible preview surface");
        using(var b=new Bitmap(r.Right-r.Left,r.Bottom-r.Top)){
            using(var g=Graphics.FromImage(b)){var dc=g.GetHdc();try{Check(PrintWindow(handle,dc,2),"native tooltip renders through its owner-draw handler");}finally{g.ReleaseHdc(dc);}}
            b.Save(Path.Combine(output,"hover.png"));
        }
    }
    static int passed;
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+(++passed)+" "+message); }
    static IEnumerable<Control> Children(Control c) {
        foreach(Control child in c.Controls) { yield return child;foreach(var nested in Children(child))yield return nested; }
    }
    static object Field(object o,string name) {return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static void Call(object o,string method,params object[] args) {o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);}
    static Button Button(Form form,string label){return Children(form).OfType<Button>().Single(b=>b.Text==label);}
    static void Render(Form form,string path) {
        form.PerformLayout();Application.DoEvents();
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path);}
    }
    static void Until(Func<bool> ready){
        var watch=Stopwatch.StartNew();
        while(!ready()){Application.DoEvents();Thread.Sleep(15);if(watch.ElapsedMilliseconds>10000)throw new Exception("Timeout waiting for UI");}
    }
    static void Modal(Action open,Action<Form> work) {
        Exception failure=null;var before=Application.OpenForms.Cast<Form>().ToArray();
        using(var timer=new System.Windows.Forms.Timer{Interval=50}){
            timer.Tick+=(s,e)=>{
                var child=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>!before.Contains(f));
                if(child==null)return;
                timer.Stop();child.ShowInTaskbar=false;child.Opacity=0;
                try{work(child);}catch(Exception ex){failure=ex;child.DialogResult=DialogResult.Cancel;child.Close();}
            };
            timer.Start();open();
        }
        if(failure!=null)throw failure;
    }
    static void Click(DiagnosticsTimeline line,string id){
        var rect=line.NodeBounds(id);
        if(rect.IsEmpty)throw new Exception("Expected a selectable dot");
        Call(line,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,rect.Left+rect.Width/2,rect.Top+rect.Height/2,0));
    }
    static void MainChecks(string output){
        using(var form=new PreviewHome {ShowInTaskbar=false,Opacity=0}){
            form.Show();var labels=Children(form).OfType<Label>().ToArray();
            labels[0].Text="已是最新版！";labels[1].Text="当前版本：V2.0.0\n最新版本：V2.0.0";
            var bar=Children(form).OfType<ProgressBar>().Single();bar.Style=ProgressBarStyle.Continuous;bar.Value=100;
            Render(form,Path.Combine(output,"home.png"));
            var buttons=new[]{Button(form,"查看 MOD"),Button(form,"历史更新记录"),Button(form,"故障诊断")};
            var bounds=buttons.Select(b=>form.RectangleToClient(b.RectangleToScreen(b.ClientRectangle))).ToArray();
            Check(bounds.All(r=>r.Top==bounds[0].Top&&Math.Abs(r.Width-bounds[0].Width)<=5),"home entrances share row and near-equal widths");
            Check(form.ClientSize.Height-bounds[0].Bottom<=24*form.DeviceDpi/96,"hidden retry action reserves no blank row");
            Check(form.ClientSize.Width>=bounds[2].Right,"home navigation inside window");
        }
    }
    static void Run(string[] args){
        string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var sourceSession=DiagnosticsStore.Sessions(source).First(s=>!DiagnosticsStore.Active(s.Path)&&new DiagnosticReview(s.Path).Marks.Count(m=>m.Manual)>=1);
        string root=Path.Combine(Path.GetTempPath(),"lgp-ui-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(DiagnosticsStore.Home(root),"fixture");Directory.CreateDirectory(path);
        foreach(var file in DiagnosticsStore.Evidence(sourceSession.Path))File.Copy(file,Path.Combine(path,Path.GetFileName(file)));
        MainChecks(output);
        using(var w=new DiagnosticsWindow(root){Opacity=0,ShowInTaskbar=false}){
            w.Show();Until(()=>w.Enabled&&Field(w,"review")!=null);
            var review=(DiagnosticReview)Field(w,"review");var line=(DiagnosticsTimeline)Field(w,"timeline");
            Console.WriteLine("DPI native "+GetDpiForWindow(w.Handle)+" framework "+w.DeviceDpi);
            var mark=review.Marks.First(m=>m.Manual&&!review.Deleted(m.Id)&&File.Exists(Path.Combine(path,"shot-"+m.Id+".png")));
            var filter=(ComboBox)Field(w,"filter");
            Check(Children(w).OfType<ListBox>().Count()==1&&!Children(w).Any(c=>c is TextBox||c is PictureBox),"only session list and timeline on main page");
            Check(!((ListBox)Field(w,"sessions")).Items[0].ToString().Contains("fixture"),"session title hides internal ID");
            Render(w,Path.Combine(output,"timeline.png"));
            Rectangle dot=line.NodeBounds(mark.Id);
            Call(line,"OnMouseMove",new MouseEventArgs(MouseButtons.None,0,dot.X+dot.Width/2,dot.Y+dot.Height/2,0));
            Check(Field(line,"thumbnail")!=null&&Convert.ToString(Field(line,"caption")).Contains(review.Note(mark.Id)),"hover preview decodes real image, time and note");
            Application.DoEvents();RenderTip(line,output);
            Modal(()=>Click(line,mark.Id),dialog=>{
                Check(Children(dialog).OfType<PictureBox>().Single().Image!=null,"real node opens screenshot dialog");
                Check(Children(dialog).All(c=>!c.Text.Contains("\"netId\"")&&!c.Text.Contains("UnityEngine.")&&!c.Text.Contains("System.Collections")&&!c.Text.Contains("\"schema\"")),"detail contains only player presentation");
                Render(dialog,Path.Combine(output,"detail.png"));
                Modal(()=>Button(dialog,"查看大图").PerformClick(),photo=>{Check(Children(photo).OfType<PictureBox>().Single().Image.Width>0,"large screenshot available");photo.Close();});
                Children(dialog).OfType<TextBox>().Single(c=>c.Name=="note").Text="仅供测试的现场备注";
                Button(dialog,"应用到时间轴").PerformClick();
            });
            Check(review.Dirty&&!Button(w,"导出诊断 ZIP").Enabled,"editing remains pending and blocks export");
            Modal(()=>Click(line,mark.Id),dialog=>Button(dialog,"标为待删除").PerformClick());
            Check(review.Deleted(mark.Id)&&!line.NodeBounds(mark.Id).IsEmpty,"pending deletion stays selectable on timeline");
            Modal(()=>Click(line,mark.Id),dialog=>Button(dialog,"撤销删除").PerformClick());
            Check(!review.Deleted(mark.Id),"undo on node before save");
            Modal(()=>Click(line,mark.Id),dialog=>Button(dialog,"标为待删除").PerformClick());
            Button(w,"保存更改").PerformClick();
            Check(line.NodeBounds(mark.Id).IsEmpty&&Button(w,"导出诊断 ZIP").Enabled,"confirmed deletion removed from manual timeline");
            filter.SelectedIndex=2;
            Modal(()=>Click(line,mark.Id),dialog=>Button(dialog,"恢复标记").PerformClick());
            Button(w,"保存更改").PerformClick();filter.SelectedIndex=0;
            Check(!line.NodeBounds(mark.Id).IsEmpty&&review.Note(mark.Id)=="仅供测试的现场备注","restored original node and note");
            filter.SelectedIndex=1;Check(line.NodeCount==review.Marks.Count(m=>!m.Manual),"automatic events use same timeline");filter.SelectedIndex=0;
            string shot=Path.Combine(path,"shot-"+mark.Id+".png");File.Move(shot,shot+".qa");
            Modal(()=>Click(line,mark.Id),dialog=>{Check(Children(dialog).Any(c=>c.Text=="现场截图未取得"),"missing screenshot handled on node");Button(dialog,"取消").PerformClick();});
            Modal(()=>Call(w,"SavedSummary"),dialog=>{Check(!Children(dialog).OfType<TextBox>().Single().Text.Contains("\"schema\""),"saved summary is player text");dialog.Close();});
            foreach(int dpi in new[]{96,120,144,192}){
                using(var scaled=new DiagnosticsWindow(root){Opacity=0,ShowInTaskbar=false}){
                    scaled.Show();Until(()=>scaled.Enabled&&Field(scaled,"review")!=null);
                    var native=scaled.Bounds;var suggested=new NativeRect{Left=native.Left,Top=native.Top,Right=native.Left+830*dpi/96,Bottom=native.Top+300*dpi/96};
                    SendMessage(scaled.Handle,0x02E0,new IntPtr((dpi<<16)|dpi),ref suggested);
                    scaled.ClientSize=new Size(Math.Max(scaled.MinimumSize.Width,700*dpi/96),Math.Max(scaled.MinimumSize.Height,280*dpi/96));
                    scaled.PerformLayout();Application.DoEvents();
                    var export=Button(scaled,"导出诊断 ZIP");var rect=scaled.RectangleToClient(export.RectangleToScreen(export.ClientRectangle));
                    var history=(ListBox)Field(scaled,"sessions");Check(history.ItemHeight>=TextRenderer.MeasureText("游戏",history.Font).Height*2,"both history lines fit at "+dpi);
                    Check(scaled.DeviceDpi==dpi&&scaled.ClientRectangle.Contains(rect)&&((DiagnosticsTimeline)Field(scaled,"timeline")).Width>220*dpi/96,"narrow layout after WinForms DPI change to "+dpi);
                    Render(scaled,Path.Combine(output,"dpi-"+dpi+".png"));scaled.Close();
                }
            }
            var original=review.Marks.ToArray();review.Marks.Clear();line.Bind(review,0);
            Check(line.NodeCount==0,"no marks shows empty timeline");Render(w,Path.Combine(output,"empty.png"));
            review.Marks.Add(mark);line.Bind(review,0);Check(line.NodeCount==1,"one mark uses one timeline lane");
            for(int i=0;i<127;i++)review.Marks.Add(new DiagnosticMark{Id="test-"+i,Kind="manual",Seconds=mark.Seconds+0.0001*i,Utc=mark.Utc});
            line.Bind(review,0);line.AutoScrollPosition=new Point(0,line.ContentHeight);Application.DoEvents();
            var last=line.NodeBounds("test-126");var center=new Point(last.Left+last.Width/2,last.Top+last.Height/2);
            Check(line.NodeCount==128&&line.ClientRectangle.Contains(center)&&line.Hit(center)=="test-126","last of 128 dense nodes individually hit after scroll");
            Render(w,Path.Combine(output,"dense.png"));review.Marks.Clear();review.Marks.AddRange(original);
            w.Close();
        }
        Console.WriteLine("RESULT "+passed+" checks; controls rendered on native Windows, additional sizes/scales simulated.");
    }
    [STAThread] static void Main(string[] args){try{UiStartup.Run(()=>Run(args));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
