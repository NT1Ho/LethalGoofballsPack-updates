using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using LcPack;

class MultiplayerDiagnosticsTests {
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static IEnumerable<Control> Children(Control parent){
        foreach(Control control in parent.Controls){yield return control;foreach(var child in Children(control))yield return child;}
    }
    static void Until(Func<bool> ready){
        var clock=Stopwatch.StartNew();
        while(!ready()){Application.DoEvents();Thread.Sleep(20);if(clock.Elapsed.TotalSeconds>15)throw new Exception("Receiver did not finish loading");}
    }
    static void ShowAndCapture(string root,string path){
        using(var receiver=new DiagnosticsWindow(root){Opacity=0,ShowInTaskbar=false}){
            receiver.Show();Until(()=>receiver.Enabled&&Children(receiver).OfType<ListBox>().Single().Items.Count>0);
            Exception failure=null;
            var open=Children(receiver).OfType<Button>().Single(b=>b.Text=="跨端事件");
            using(var timer=new System.Windows.Forms.Timer{Interval=50}){
                timer.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="跨端事件");
                    if(dialog==null)return;
                    timer.Stop();dialog.Opacity=0;
                    try {
                        var grid=Children(dialog).OfType<DataGridView>().Single();Until(()=>grid.RowCount>0);
                        int count=grid.RowCount;
                        var filters=Children(dialog).OfType<ComboBox>().ToArray();
                        Check(filters.Length==5&&filters[0].Items.Count>2,"receiver exposes role, MOD, category, status and F8 filters");
                        filters[4].SelectedIndex=1;
                        Check(grid.RowCount>0&&grid.RowCount<=count,"F8 narrows the table to its retained cross-peer evidence");
                        using(var image=new Bitmap(dialog.Width,dialog.Height)){
                            dialog.PerformLayout();Application.DoEvents();dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path);
                        }
                    }catch(Exception ex){failure=ex;}finally{dialog.Close();}
                };
                timer.Start();open.PerformClick();timer.Stop();
            }
            if(failure!=null)throw failure;
            var save=Children(receiver).OfType<Button>().Single(b=>b.Text=="保存更改");
            save.PerformClick();
            Check(Children(receiver).OfType<Button>().Single(b=>b.Text=="导出诊断 ZIP").Enabled,"receiver save enables ZIP export");
            receiver.Close();
        }
    }
    static void Verify(string host,string guest,string output){
        Directory.CreateDirectory(output);
        var hostSession=DiagnosticsStore.Sessions(host).First();
        var guestSession=DiagnosticsStore.Sessions(guest).First();
        Check(!DiagnosticsStore.Active(hostSession.Path)&&!DiagnosticsStore.Active(guestSession.Path),"both real V81 sessions exited");
        var review=new DiagnosticReview(hostSession.Path);
        var mark=review.Marks.Last(m=>m.Manual);
        string shot="shot-"+mark.Id+".png";
        using(var picture=Image.FromFile(Path.Combine(hostSession.Path,shot)))Check(picture.Width>800&&picture.Height>400,"host F8 screenshot decodes");
        var g=new DiagnosticReview(guestSession.Path);
        Check(!g.Marks.Any(m=>m.Manual)&&!Directory.GetFiles(g.Path,"shot-*.png").Any(),"guest required no F8 and collected no screenshot");
        Check(g.Marks.Any(m=>m.Kind=="host_mark"&&File.ReadAllText(Path.Combine(g.Path,"mark-"+m.Id+".json")).Contains(mark.Id)),
            "guest recorded a context window for the actual host marker");
        var timeline=MultiplayerTimeline.Load(review);
        var nearest=timeline.Rows.Where(r=>r.Marks.Contains(mark.Id)).ToArray();
        Check(nearest.Any(r=>r.Role=="host")&&nearest.Any(r=>r.Role=="guest"&&r.Category!="inventory"),"host F8 correlates views in the same network-time window");
        Check(nearest.Where(r=>r.Role=="guest").All(r=>r.Sequence>0&&r.Utc.Length>0&&r.ReceivedTime>=0),"guest event identity, arrival and original UTC retained");
        string root=Path.Combine(output,"receiver-fixture");
        string fixture=Path.Combine(DiagnosticsStore.Home(root),hostSession.Id);
        Directory.CreateDirectory(fixture);
        foreach(string file in DiagnosticsStore.Evidence(review.Path))File.Copy(file,Path.Combine(fixture,Path.GetFileName(file)),true);
        var copy=new DiagnosticReview(fixture);
        copy.SetNote(mark.Id,"V81 实测：客机事件自动汇总，房主按 F8。");copy.Save();
        ShowAndCapture(root,Path.Combine(output,"receiver-cross-peer.png"));
        copy=new DiagnosticReview(fixture);
        string destination=Path.Combine(output,"F8-多端统一诊断.zip");
        copy.Export(destination);
        using(var zip=ZipFile.OpenRead(destination)){
            Check(zip.GetEntry(shot)!=null&&zip.Entries.Count(e=>e.Name.EndsWith(".png"))==1,"one ZIP contains the host screenshot and no guest screenshot");
            Check(zip.GetEntry("multiplayer-coverage.json")!=null&&zip.GetEntry("key-000.jsonl")!=null,"derived coverage and raw key journal included");
            var manifest=DiagnosticsStore.Value(Data.Read<Dictionary<string,object>>(Read(zip,"evidence-manifest.json")),"files") as System.Collections.IEnumerable;
            foreach(Dictionary<string,object> entry in manifest){
                string file=(string)entry["file"];
                using(var stream=zip.GetEntry(file).Open())using(var sha=SHA256.Create())
                    Check(Data.Hex(sha.ComputeHash(stream))==(string)entry["sha256"],"SHA-256 "+file);
            }
            Check(Read(zip,"multiplayer-timeline.jsonl").Contains(mark.Id)&&Read(zip,"notes.json").Contains("客机事件自动汇总"),"matched marker and note exported");
        }
        copy.SetDeleted(mark.Id,true);copy.Save();
        Check(MultiplayerTimeline.Load(copy).Rows.All(r=>!r.Marks.Contains(mark.Id)),"delete removes derived links");
        copy.SetDeleted(mark.Id,false);copy.Save();
        Check(MultiplayerTimeline.Load(copy).Rows.Any(r=>r.Marks.Contains(mark.Id)),"restore returns derived links");
    }
    static string Read(ZipArchive archive,string entry){
        using(var reader=new StreamReader(archive.GetEntry(entry).Open()))return reader.ReadToEnd();
    }
    [STAThread] static void Main(string[] args){
        try{
            if(args[0]=="install"){foreach(string root in args.Skip(1))DiagnosticsStore.Install(root);return;}
            UiStartup.Run(()=>Verify(args[0],args[1],args[2]));
        }catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}

