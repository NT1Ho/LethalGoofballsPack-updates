using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using C=System.Windows.Controls;
using W=System.Windows;
using LcPack;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class PageEditorTests {
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr key,IntPtr detail);
    static int passed;
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+(++passed)+" "+label);}
    static T F<T>(object window,string field){return (T)window.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);}
    static void Pump(){for(int i=0;i<4;i++){Application.DoEvents();Thread.Sleep(10);}}
    static IEnumerable<Control> Children(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    static void Render(Form w,string path){Pump();using(var b=new Bitmap(w.Width,w.Height)){w.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path);}}
    static string Edit(PageEditor w,string field){
        var t=F<TextBox>(w,field);t.Focus();t.Text="中文首段";t.SelectionStart=t.TextLength;
        PostMessage(t.Handle,0x100,new IntPtr(13),new IntPtr(0x001C0001));PostMessage(t.Handle,0x101,new IntPtr(13),new IntPtr(unchecked((int)0xC01C0001)));Pump();
        Check(t.Text=="中文首段\r\n"&&t.Focused,"plain Enter inserts newline and keeps focus in "+field);
        t.Paste("第二段\r\n\r\n第三段");
        Check(t.Text=="中文首段\r\n第二段\r\n\r\n第三段","paste preserves paragraph break in "+field);
        return t.Text;
    }
    static PageMod M(string key,int order,bool own,bool? fresh){return new PageMod{Key=key,Name=key,Purpose="用途",Details="",Own=own,New=fresh,Order=order};}
    [STAThread]static int Main(string[] args){try{UiStartup.Run(()=>Run(args));return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static void Run(string[] args){
        string output=args.Length==0?Path.Combine(Path.GetTempPath(),"lgp-edit-"+Guid.NewGuid().ToString("N")):Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var p=new PageContent{FontSize=17,ModNamePercent=45,ModVersionPercent=20,HistoryModPercent=48};
        p.Mods.AddRange(new[]{M("Alpha",0,false,false),M("Beta",1,false,true),M("Gamma",3,true,true),M("Delta",2,true,false),M("Extra",0,false,null)});
        p.Mods[2].VersionColor="#993366";p.Mods[0].Details="旧行\n\n  保留缩进";
        p.History.Add(new HistoryRelease{Version="v-test",PublishedUtc="2026-11-01T12:34:00Z",Changes=new List<HistoryEntry>{new HistoryEntry{Mod="组件",Kind="修复",Summary="原内容"}}});
        var installed=p.Mods.Select(m=>new InstalledMod{name=m.Key,version="1.2.3",kind="MOD",status="已安装",isNew=m.Key=="Extra"}).ToList();
        var path=Path.Combine(output,"edited.json");string expected=null;
        using(var editor=new PageEditor(p,installed,doc=>Data.Atomic(path,Data.Json(doc))){Opacity=0,ShowInTaskbar=false}){
            editor.Show();Pump();
            Check(Data.Json(editor.Read())==Data.Json(p),"untouched legacy values, LF text, order, null NEW and layout metadata survive open");
            Check(F<TabControl>(editor,"tabs").TabPages.Count==2&&!Children(editor).Any(c=>c is NumericUpDown||c is DataGridView),"no layout tab or long-text grid");
            var list=F<ListBox>(editor,"mods");
            Func<string> order=()=>String.Join(",",list.Items.Cast<PageMod>().Select(m=>m.Key));
            Check(order()=="Delta,Gamma,Extra,Beta,Alpha","own before NEW before ordinary, stable order within each");
            list.SelectedItem=list.Items.Cast<PageMod>().Single(m=>m.Key=="Alpha");
            F<ComboBox>(editor,"badge").SelectedIndex=1;
            Check(order()=="Delta,Gamma,Alpha,Extra,Beta"&&((PageMod)list.SelectedItem).Key=="Alpha","NEW toggle reorders immediately and retains selection");
            F<ComboBox>(editor,"badge").SelectedIndex=2;Check(order()=="Delta,Gamma,Extra,Beta,Alpha","removing NEW restores previous place");
            var own=F<CheckBox>(editor,"own");own.Checked=true;Check(order().StartsWith("Alpha,Delta,Gamma"),"own toggle moves into top group");own.Checked=false;
            list.SelectedItem=list.Items.Cast<PageMod>().Single(m=>m.Key=="Gamma");
            expected=Edit(editor,"name");Edit(editor,"purpose");Edit(editor,"details");
            Render(editor,Path.Combine(output,"editor.png"));
            editor.SelectSection(1,"v-test");Edit(editor,"subject");Edit(editor,"summary");Render(editor,Path.Combine(output,"history-editor.png"));
            Check(!F<TextBox>(editor,"version").Multiline&&!F<TextBox>(editor,"when").Multiline&&!F<TextBox>(editor,"search").Multiline,"structured fields remain single line");
            Children(editor).OfType<Button>().Single(b=>b.Text=="保存到待发布").PerformClick();
        }
        var loaded=PageContent.Load(path);var changed=loaded.Find("Gamma");
        Check(changed.Name==expected&&changed.Purpose==expected&&changed.Details==expected&&changed.VersionColor=="#993366"&&changed.Order==3,"serialized multiline MOD fields, color and underlying order intact");
        Check(loaded.History[0].Changes[0].Summary==expected&&loaded.History[0].Changes[0].Mod==expected,"serialized multiline history intact");
        Check(loaded.Find("Alpha").Details==p.Mods[0].Details&&loaded.Find("Extra").New==null&&loaded.FontSize==17,"untouched data not normalized on save");
        using(var reopened=new PageEditor(loaded,installed,doc=>{}){Opacity=0,ShowInTaskbar=false}){reopened.Show();var list=F<ListBox>(reopened,"mods");list.SelectedItem=list.Items.Cast<PageMod>().Single(m=>m.Key=="Gamma");
            Check(F<TextBox>(reopened,"details").Text==expected,"reopened native editor retains original paragraphs");reopened.Close();}
        using(var preview=new ModWindow("",loaded,installed){Opacity=0,ShowInTaskbar=false}){
            preview.Show();Pump();var list=F<C.ListView>(preview,"list");var rows=list.ItemsSource.Cast<ModWindow.ModRow>().ToArray();
            Check(rows.Select(r=>r.Name).SequenceEqual(new[]{"Delta",expected,"Extra","Beta","Alpha"}),"actual MOD window uses same effective ordering");
            Check(rows[1].Purpose==expected&&rows[1].DetailFeatures.Length==4&&rows[1].DetailFeatures[2].Text==""&&rows[1].Version=="1.2.3","receiver purpose/detail paragraphs, blank line and actual version preserved");
            Check(rows[1].VersionBrush.ToString()=="#FF993366"&&list.FontSize!=17,"version color retained, old typography override ignored");
            rows[1].IsExpanded=true;preview.ClientSize=new Size(670,420);Pump();
            var row=(C.ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(1);Check(row.ActualHeight>3*list.FontSize,"expanded row grows for multiline text at narrow width");preview.Close();
        }
        var aliases=new PageContent();aliases.Mods.Add(M("KiwiBodies",0,true,false));Check(aliases.Find("Giant Sapsucker Bodies")==aliases.Mods[0]&&aliases.Find("unrelated")==null,"existing identity aliases preserved");
        Console.WriteLine("RESULT "+passed+" editor and presentation checks");
    }
}
