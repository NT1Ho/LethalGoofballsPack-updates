using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcPack;

class UiTests {
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+(++checks)+" "+name);}
    static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static void Render(Form f,string path){Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path);}}
    static void Wait(Task task){var watch=Stopwatch.StartNew();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(10);if(watch.Elapsed.TotalSeconds>30)throw new Exception("timeout");}task.GetAwaiter().GetResult();}
    [STAThread]public static void Main(string[] args){try{UiStartup.Run(()=>Run(args));}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static System.Collections.Generic.IEnumerable<Control> Children(Control c){
        foreach(Control child in c.Controls){yield return child;foreach(var d in Children(child))yield return d;}
    }
    static void Modal(Func<Task> open,Action<Form> work){
        var before=Application.OpenForms.Cast<Form>().ToArray();Exception failure=null;
        using(var timer=new System.Windows.Forms.Timer{Interval=50}){
            timer.Tick+=(s,e)=>{var f=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>!before.Contains(x));if(f==null)return;timer.Stop();try{work(f);}catch(Exception ex){failure=ex;f.DialogResult=DialogResult.Cancel;f.Close();}};
            timer.Start();Wait(open());
        }
        if(failure!=null)throw failure;
    }
    static Task Invoke(PublisherWindow w,string name,params object[] args){return (Task)typeof(PublisherWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,args);}
    static void Run(string[] args){
        string output=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(Path.GetTempPath(),"admin-render-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var workspace=new Workspace(args.Length>0?args[0]:Path.Combine(output,"sandbox"));
        if(workspace.Root==Workspace.DefaultRoot)throw new IOException("UI test requires a separate workspace.");
        var seed=new AdminWorkspace(workspace);string sample=Path.Combine(output,"ui-fixture.cfg");
        File.WriteAllText(sample,"[Review]\r\nLevel = 1\r\n");seed.SetVersion("QA-LOCAL");seed.Import(new[]{Tuple.Create(sample,"BepInEx/config/admin-ui-qa.cfg")},s=>{});
        using(var w=new PublisherWindow(workspace,true){Opacity=0,ShowInTaskbar=false}){
            w.Show();Application.DoEvents();
            var tabs=Field<TabControl>(w,"tabs");var publish=Field<Button>(w,"publish");var grid=Field<DataGridView>(w,"catalog");
            Check(tabs.TabPages.Count==4&&!publish.Enabled,"four work areas; isolated workspace cannot publish");
            Check(grid.VirtualMode,"catalog renders rows on demand");
            Render(w,Path.Combine(output,"catalog.png"));
            var selector=Field<ComboBox>(w,"view");selector.SelectedIndex=1;var search=Field<TextBox>(w,"search");search.Text="BepInEx/config";
            Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[0].Value).IndexOf("BepInEx/config",StringComparison.OrdinalIgnoreCase)>=0),"file search reflects actual target paths");
            search.Text="admin-ui-qa.cfg";grid.Rows[0].Selected=true;
            Modal(()=>Invoke(w,"EditConfig"),f=>{
                var settings=Children(f).OfType<DataGridView>().Single();settings.Rows[0].Cells[2].Value="2";
                Children(f).OfType<Button>().Single(b=>b.Text=="保存到候选").PerformClick();
            });
            var updated=workspace.Current.files.Single(f=>f.path=="BepInEx/config/admin-ui-qa.cfg");
            Check(File.ReadAllText(workspace.ObjectPath(updated.sha256)).Contains("Level = 2"),"configuration dialog commits edited value to candidate bytes");
            search.Clear();selector.SelectedIndex=0;
            if(!seed.Pages().History.Any(h=>h.Version=="QA-LOCAL")){
                Modal(()=>Invoke(w,"EditPages",1,true),f=>Children(f).OfType<Button>().Single(b=>b.Text=="保存到待发布").PerformClick());
            }
            Check(seed.Pages().History.Any(h=>h.Version=="QA-LOCAL"&&h.Changes.Count>0),"generated history saved through actual editor");
            Wait(Invoke(w,"CheckCandidate"));
            tabs.SelectedIndex=1;Application.DoEvents();Render(w,Path.Combine(output,"history.png"));
            tabs.SelectedIndex=2;Application.DoEvents();Render(w,Path.Combine(output,"release.png"));
            var draft=Field<AdminWorkspace>(w,"draft");var changes=Field<DataGridView>(w,"changes");
            Check(changes.RowCount==draft.Changes().Count,"release grid matches persisted file differences");
            if(changes.RowCount>0){
                var config=draft.Changes().FirstOrDefault(c=>c.Path=="BepInEx/config/admin-ui-qa.cfg");
                if(config!=null){
                    var row=changes.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[1].Value)==config.Path);
                    row.Selected=true;Wait((Task)typeof(PublisherWindow).GetMethod("Restore",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,null));
                    Check(!workspace.Current.files.Any(f=>f.path==config.Path)&&!workspace.Current.deletes.Contains(config.Path),"UI undo updates live draft without creating deletion");
                }
            }
            tabs.SelectedIndex=3;Application.DoEvents();Render(w,Path.Combine(output,"channels.png"));
            foreach(int width in new[]{810,1040}){
                w.ClientSize=new Size(width,470);tabs.SelectedIndex=2;Application.DoEvents();
                var rect=w.RectangleToClient(publish.RectangleToScreen(publish.ClientRectangle));Check(w.ClientRectangle.Contains(rect),"publish control fits width "+width);
            }
            w.Close();
        }
        Console.WriteLine("RESULT "+checks+" UI checks; renders "+output);
    }
}
