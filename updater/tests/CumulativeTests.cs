using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LcPack;

class CumulativeTests {
    static int n;
    static void Ok(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("PASS "+(++n)+" "+msg);}
    static void Refuse(Action op,string reason){try{op();}catch(Exception ex){if(!ex.Message.Contains(reason))throw;Ok(true,reason);return;}throw new Exception("unexpected success: "+reason);}
    static T Field<T>(object w,string s){return (T)w.GetType().GetField(s,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w);}
    static void Pump(Func<bool> done){var clock=System.Diagnostics.Stopwatch.StartNew();while(!done()){Application.DoEvents();Thread.Sleep(10);if(clock.Elapsed.TotalSeconds>65)throw new TimeoutException();}}
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,int cmd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder s,int n);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    static IntPtr Dialog(Form form,string title){
        var h=GetWindow(form.Handle,6);var s=new StringBuilder(100);
        if(h!=IntPtr.Zero){GetClassName(h,s,100);if(s.ToString()=="#32770"){GetWindowText(h,s,100);if(s.ToString()==title)return h;}}return IntPtr.Zero;
    }
    static void Register(Workspace ws,ModResult m,Dictionary<string,string> data){
        string dir=Path.Combine(Cumulative.Inbox(ws),m.package+"-r"+m.revision);Directory.CreateDirectory(dir);
        foreach(var x in data){string p=SafePath.Target(Path.Combine(dir,"payload"),x.Key);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,x.Value);m.files.Single(f=>f.path==x.Key).sha256=Data.Hash(p);}
        Data.Atomic(Path.Combine(dir,"result.json"),Data.Json(m));
    }
    [STAThread]static int Main(string[] args){try{UiStartup.Run(()=>{Exception failure=null;using(var loop=new ApplicationContext()){
            EventHandler start=null;start=(s,e)=>{Application.Idle-=start;try{Run(args[0]);}catch(Exception ex){failure=ex;}finally{loop.ExitThread();}};
            Application.Idle+=start;Application.Run(loop);
        }if(failure!=null)throw failure;});return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static void Run(string root){
        var ws=new Workspace(Path.Combine(root,"cumulative-test"));var draft=new AdminWorkspace(ws);
        typeof(Cloud).GetMethod("Configure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{null});
        var account=CosSettings.Save(ws,"https://fixture-123.cos.ap-guangzhou.myqcloud.com","test","test");ws.SaveToken("isolated");
        string patch="BepInEx/plugins/A/feature.bin",removed="BepInEx/plugins/A/retired.bin",cfg="BepInEx/config/a.cfg",asset="BepInEx/plugins/B/data.bin";
        string input=Path.Combine(root,"seed");File.WriteAllText(input,"older");
        draft.Import(new[]{Tuple.Create(input,patch),Tuple.Create(input,removed)},s=>{});var baseline=AdminWorkspace.Candidate(ws.Current,"qa-old");baseline.deletes.Add(asset);ws.SetRemote(baseline);draft.RefreshBase();
        var m=new ModResult{baseHash=ws.Current.baseManifestHash,package="Local-A",kind="修复",revision=1,version="1.1.0",summary="修复并清理",tests="隔离安装",own=true,dependencies=new string[0],files=new List<ResultFile>{new ResultFile{path=patch,before=Data.Hash(input)},new ResultFile{path=removed,before=Data.Hash(input)},new ResultFile{path=cfg}}};
        Register(ws,m,new Dictionary<string,string>{{patch,"v1"},{cfg,"setting"}});
        var removedInfo=new ModResult{package="Local-A",version="1.0",kind="移除",revision=1,baseHash=m.baseHash,summary="retire",tests="fixture",dependencies=new string[0],files=new List<ResultFile>{new ResultFile{path="BepInEx/pack-info/Local-A/manifest.json",before=Data.Hash(input)}}};
        Cumulative.ValidateReceipt(Data.Json(removedInfo),m.baseHash);
        removedInfo.files[0].path="BepInEx/pack-info/Other/manifest.json";Refuse(()=>Cumulative.ValidateReceipt(Data.Json(removedInfo),m.baseHash),"可登记");
        removedInfo.files[0].path="BepInEx/pack-info/display-pages.json";Refuse(()=>Cumulative.ValidateReceipt(Data.Json(removedInfo),m.baseHash),"可登记");
        m.version="1.2.0";m.revision++;Register(ws,m,new Dictionary<string,string>{{patch,"new verified"},{cfg,"setting"}});
        var other=new ModResult{baseHash=m.baseHash,package="Local-B",kind="新增",revision=1,version="1.0.0",summary="新增资源",tests="通过",dependencies=new[]{"Local-A-1.2.0"},files=new List<ResultFile>{new ResultFile{path=patch,before=Data.Hash(input)}}};
        Register(ws,other,new Dictionary<string,string>{{patch,"conflict"}});Refuse(()=>Cumulative.Build(draft),"相同文件");
        other.revision++;other.files=new List<ResultFile>{new ResultFile{path=asset}};Register(ws,other,new Dictionary<string,string>{{asset,"asset bytes"}});
        string zip=Cumulative.Build(draft);var b=Cumulative.Read(draft,zip);Ok(b.results.Count==2&&b.results.All(r=>r.revision==2),"highest confirmed revisions combined");
        using(var z=ZipFile.OpenRead(zip))Ok(z.Entries.Count==5&&!z.Entries.Any(e=>e.FullName.Contains("older")),"only three unique delta objects with index and notes");
        string corrupted=Path.Combine(root,"corrupt.zip");File.Copy(zip,corrupted);
        using(var z=ZipFile.Open(corrupted,ZipArchiveMode.Update)){var e=z.Entries.First(x=>x.FullName.StartsWith("objects/"));string k=e.FullName;e.Delete();using(var w=new StreamWriter(z.CreateEntry(k).Open()))w.Write("tampered");}
        Refuse(()=>Cumulative.Read(draft,corrupted),"哈希不符");string initial=Data.Json(ws.Current);
        other=b.results[1];other.dependencies=new[]{"Missing-Mod-1.0"};Refuse(()=>Cumulative.Apply(draft,b,"qa-new",true),"缺少依赖");Ok(initial==Data.Json(ws.Current),"rejected batch leaves draft unchanged");b=Cumulative.Read(draft,zip);
        var history=new PageContent();history.History.Add(new HistoryRelease{Version="V2.0.0",PublishedUtc="2026-11-01T12:00:00Z",Changes=new List<HistoryEntry>{new HistoryEntry{Kind="新增",Mod="Wesley全套MODS",Summary=""}}});
        Refuse(history.Validate,"第 1 条（Wesley全套MODS）缺少更新说明");history.History[0].Changes[0].Summary="说明";history.Validate();
        var svc=new AdminWorkflowTests.Service{FailBackup=false};string baseSigned=CosSettings.Sign(account,"mods",Data.Json(baseline));
        svc.Feed=baseSigned;svc.Objects["/lcpack/manifest.json"]=Encoding.UTF8.GetBytes(baseSigned);
        var reused=b.results[0].files.First(f=>f.path==cfg).sha256;svc.Objects["/lcpack/objects/"+reused+".bin"]=File.ReadAllBytes(ws.ObjectPath(reused));
        using(var w=new PublisherWindow(ws,true,()=>new AdminWorkflowTests.Handler(svc)){Opacity=0,ShowInTaskbar=false}){
            w.Show();var tabs=Field<TabControl>(w,"tabs");tabs.SelectedIndex=2;
            var data=new DataObject(DataFormats.FileDrop,new[]{zip});var drop=typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic);
            drop.Invoke(w,new object[]{new DragEventArgs(data,0,0,0,DragDropEffects.Copy,DragDropEffects.Copy)});
            var status=Field<Label>(w,"status");Pump(()=>tabs.Enabled);Ok(status.Text.Contains("检查通过"),"real form accepts dropped ZIP with zero install prompts");
            Ok(Field<DataGridView>(w,"changes").RowCount==4&&!Field<Button>(w,"undo").Enabled,"dropped archive displays additions, replacement and deletion before entering version");
            Field<TextBox>(w,"version").Text="qa-new";
            var meter=new List<string>();status.TextChanged+=(s,e)=>{meter.Add(status.Text);
                if(status.Text.StartsWith("GitHub · 文件")&&status.Text.Contains("100%")){
                    var monitor=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text.StartsWith("发布进度"));if(monitor!=null)
                        using(var img=new System.Drawing.Bitmap(monitor.Width,monitor.Height)){monitor.DrawToBitmap(img,new System.Drawing.Rectangle(0,0,img.Width,img.Height));img.Save(Path.Combine(root,"progress.png"));}
                }};
            Exception dialogError=null;bool confirmed=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=30}){
                timer.Tick+=(s,e)=>{IntPtr h=Dialog(w,"请核对操作");if(h!=IntPtr.Zero){if(!confirmed){
                    string before=Data.Json(ws.Current);Cumulative.Apply(draft,b,"qa-new",true);Ok(before==Data.Json(ws.Current),"repeat import keeps identical draft and history");
                    var late=new ModResult{package="Local-Later",version="1.0",baseHash=m.baseHash,kind="新增",revision=1,summary="留给下次",tests="通过",dependencies=new string[0],files=new List<ResultFile>{new ResultFile{path="BepInEx/plugins/Later/data.bin"}}};
                    Register(ws,late,new Dictionary<string,string>{{late.files[0].path,"later"}});
                }confirmed=true;PostMessage(h,0x111,(IntPtr)1,IntPtr.Zero);}h=Dialog(w,"操作未完成");if(h!=IntPtr.Zero){dialogError=new Exception(status.Text);PostMessage(h,0x111,(IntPtr)1,IntPtr.Zero);}};
                timer.Start();Field<Button>(w,"publish").PerformClick();Pump(()=>tabs.Enabled);timer.Stop();
            }
            if(dialogError!=null)throw dialogError;
            Ok(confirmed&&status.Text=="隔离演练成功：qa-new"&&svc.PointerWrites==1&&svc.Published,"single GUI confirmation completed real release transaction on controlled transports");
            Ok(meter.Any(s=>s.StartsWith("COS · 文件")&&s.Contains("已复用 1")&&s.Contains("(100%)"))&&meter.Any(s=>s.StartsWith("GitHub · 文件")&&s.Contains("(100%)")),"observed channel byte progress, verified reuse excluded");
            Ok(!draft.Pending&&Directory.GetDirectories(Cumulative.Inbox(ws)).Length==1&&Directory.GetDirectories(Path.Combine(Cumulative.Home(ws),"已发布","qa-new")).Length==4,"only published revisions archived after verification");
            var final=draft.Pages();Ok(final.Find("A").Own&&final.Find("B").New==true&&final.History.Single(h=>h.Version=="qa-new").Changes.Count==2,"receiver metadata and editable history");
            string published=Path.Combine(Cumulative.Home(ws),"已发布","qa-new",Cumulative.ArchiveName);Refuse(()=>Cumulative.Read(draft,published),"基线不匹配");
            draft.RefreshBase();var next=Cumulative.Read(draft,Cumulative.Build(draft));Ok(next.results.Count==1&&next.results[0].package=="Local-Later"&&next.baseVersion=="qa-new","unpublished result retained and safely compared with new baseline");
            string fixture=Path.Combine(root,"public-install");Directory.CreateDirectory(fixture);
            Data.Atomic(Path.Combine(fixture,"channel.json"),Data.Json(account.channel));Data.Atomic(Path.Combine(fixture,"signed.json"),svc.Feed);
            Data.Atomic(Path.Combine(fixture,"before.json"),Data.Json(baseline));File.WriteAllText(Path.Combine(fixture,"old.bin"),"older");
            foreach(var file in ws.Current.files){byte[] publicBytes=svc.Objects["/lcpack/objects/"+file.sha256+".bin"];File.WriteAllBytes(Path.Combine(fixture,file.sha256),publicBytes);}
            w.Close();
        }
        Console.WriteLine("RESULT "+n+" cumulative and GUI checks");
    }
}
