using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;using LcPack;
class PageEditorTests{
 static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);n++;}
 static T Field<T>(object o,string s){return (T)o.GetType().GetField(s,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 [STAThread]static int Main(){try{Run();Console.WriteLine(n+" checks passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(){
 var aliases=new PageContent();aliases.Mods.Add(new PageMod{Key="KiwiBodies",Name="KiwiBodies",Purpose="edited corpse",Details="* custom detail",Order=0,VersionColor="#A8652A"});aliases.Mods.Add(new PageMod{Key="IndoorHazardGuard",Name="IndoorHazardGuard",Purpose="edited guard",Order=1});aliases.Mods.Add(new PageMod{Key="UniversalWrench",Name="wrench",Order=2});aliases.Validate();
 Check(aliases.Find("Giant Sapsucker Bodies")==aliases.Mods[0]&&aliases.Find("Indoor Hazard Guard")==aliases.Mods[1],"DLL aliases match publisher keys");
 Check(aliases.Find("Universal Wrench slot 3 listening fixture")==null,"helper plugin not merged with main mod");
 var aliasInput=new List<InstalledMod>{new InstalledMod{name="Indoor Hazard Guard",kind="MOD",version="1.0.0"},new InstalledMod{name="Giant Sapsucker Bodies",kind="MOD",version="1.0.0"}};
 using(var w=new ModWindow("",aliases,aliasInput)){w.Opacity=0;w.ShowInTaskbar=false;w.Show();Application.DoEvents();var display=Field<System.Windows.Controls.ListView>(w,"list").ItemsSource.Cast<ModWindow.ModRow>().ToList();Check(display[0].Name=="KiwiBodies"&&display[1].Name=="IndoorHazardGuard","aliases retain editor pin order");Check(display[0].Purpose=="edited corpse"&&display[0].DetailFeatures[0].Text=="custom detail","aliases use edited description and details");Check(display[0].VersionBrush.ToString()=="#FFA8652A"&&display[1].VersionBrush==System.Windows.Media.Brushes.Black,"custom version color and default color");w.Close();}
 aliases.Mods[0].VersionColor="red";bool badColor=false;try{aliases.Validate();}catch(InvalidDataException){badColor=true;}Check(badColor,"invalid color rejected");aliases.Mods[0].VersionColor="";aliases.Validate();
 var temp=Path.Combine(Path.GetTempPath(),"LcPageEdit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
 var p=new PageContent{History=HistoryWindow.ReadHistory()};
 p.Mods.Add(new PageMod{Key="MoreCounterplay",Name="H增强版 MoreCounterplay",Purpose="反制",Own=true,New=null,Details="原作者功能\n* 修复八音盒问题",Order=1});
 p.Mods.Add(new PageMod{Key="BetterStamina",Name="体力优化",Purpose="优化体力",New=true,Order=0});p.Validate();
 var source=Data.Json(p);var installed=new List<InstalledMod>{new InstalledMod{name="MoreCounterplay",version="1.5.1",kind="MOD",status="已安装",isNew=true},new InstalledMod{name="BetterStamina",version="1.5.7",kind="MOD",status="已安装"}};
 var ws=new Workspace(Path.Combine(temp,"publisher"));PageContent saved=null;
 using(var ed=new PageEditor(p,installed,value=>{saved=value;string path=Path.Combine(temp,"edited.json");Data.Atomic(path,Data.Json(value));ws.Add(path,PageContent.RelativePath);})){ed.Opacity=0;ed.ShowInTaskbar=false;ed.Show();Application.DoEvents();
 var rows=(System.ComponentModel.BindingList<PageMod>)Field<DataGridView>(ed,"mods").DataSource;rows[0].Purpose="调整后的说明";
 Check(!Field<DataGridView>(ed,"mods").Columns.OfType<DataGridViewCheckBoxColumn>().Any(c=>c.ThreeState),"all checkboxes use two states");
 var result=ed.Read();Check(result.Mods[0].Key=="BetterStamina","display order editable without changing plugin identity");Check(Data.Json(p)==source,"editing does not mutate original on cancel");
 Check(result.Mods[1].New==false,"legacy automatic NEW normalized to unchecked");Check(result.Mods[1].Features()[1].SelfDeveloped,"feature-level self-developed mark");
 var bar=ed.Controls.OfType<FlowLayoutPanel>().Single();bar.Controls.OfType<Button>().Single(b=>b.Text=="保存到待发布").PerformClick();Application.DoEvents();
 Check(saved!=null&&ws.Current.files.Count==1,"save stages one metadata file only");Check(ws.Current.releaseVersion==""&&ws.Current.deletes.Count==0,"save does not publish or change pack version or delete mods");}
 var f=ws.Current.files.Single();var loaded=PageContent.Load(ws.ObjectPath(f.sha256));Check(loaded.Mods[0].Purpose=="调整后的说明","saved changes reload");
 string dest=Path.Combine(temp,PageContent.RelativePath.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(ws.ObjectPath(f.sha256),dest);Check(PageContent.ForRoot(temp).History.Count==p.History.Count,"receiver loads all published history");
 using(var window=new ModWindow(temp,loaded,installed)){window.Opacity=0;window.ShowInTaskbar=false;window.Show();Application.DoEvents();var list=Field<System.Windows.Controls.ListView>(window,"list");var display=list.ItemsSource.Cast<ModWindow.ModRow>().ToList();Check(display[0].Name=="体力优化"&&display[0].Purpose=="调整后的说明","real receiver preview uses names descriptions and order");Check(display[0].Version=="1.5.7","installed DLL version remains authoritative");Check(display[1].New==""&&display[1].IsHao&&!display[1].IsExpanded,"explicit NEW choice and collapsed details retained");window.Close();}
 using(var h=new HistoryWindow(loaded.History,loaded)){h.Opacity=0;h.ShowInTaskbar=false;h.Show();Application.DoEvents();Check(h.Text=="历史更新记录","history renders from edited data");h.Close();}
 loaded.Mods.Add(loaded.Mods[0]);bool failed=false;try{loaded.Validate();}catch(InvalidDataException){failed=true;}Check(failed,"duplicate identity rejected");loaded.Mods.RemoveAt(loaded.Mods.Count-1);loaded.FontSize=100;failed=false;try{loaded.Validate();}catch(InvalidDataException){failed=true;}Check(failed,"invalid layout rejected");
 Check(PageContent.ForRoot(Path.Combine(temp,"empty")).History.Count>0,"old installations retain embedded history fallback");
 }
}
