using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LcPack {
public sealed class PageEditor:Form {
    readonly PageContent content;
    readonly List<InstalledMod> installed;
    readonly HashSet<PageMod> detectedNew;
    readonly Action<PageContent> save;
    readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};
    readonly ListBox mods=List(),entries=List();
    readonly ComboBox releases=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,FormattingEnabled=true};
    readonly TextBox search=Box(false),name=Box(true),purpose=Box(true),details=Box(true);
    readonly TextBox version=Box(false),when=Box(false),kind=Box(false),subject=Box(true),summary=Box(true);
    readonly ComboBox badge=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=138};
    readonly CheckBox own=new CheckBox{Text="自研／改版",AutoSize=true};
    readonly Label identity=new Label{AutoSize=true,ForeColor=SystemColors.GrayText};
    readonly Button color=new Button{AutoSize=true};
    Control modFields,historyFields;
    bool binding,saved;
    string candidateVersion,original;
    PageMod Mod {get{return mods.SelectedItem as PageMod;}}
    HistoryRelease Release {get{return releases.SelectedItem as HistoryRelease;}}
    HistoryEntry Entry {get{return entries.SelectedItem as HistoryEntry;}}

    public PageEditor(PageContent data,List<InstalledMod> items,Action<PageContent> write){
        content=Data.Read<PageContent>(Data.Json(data));original=Data.Json(content);installed=items;save=write;
        detectedNew=new HashSet<PageMod>(items.Where(r=>r.isNew).Select(r=>content.Find(r.name)).Where(m=>m!=null));
        Text="编辑接收端页面";Font=new Font("Microsoft YaHei UI",9);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        ClientSize=new Size(884,545);MinimumSize=new Size(705,445);StartPosition=FormStartPosition.CenterParent;
        using(var icon=typeof(PageEditor).Assembly.GetManifestResourceStream("settings.ico"))if(icon!=null)Icon=new Icon(icon);
        var layout=Panel(1);layout.Padding=new Padding(8);layout.RowStyles.Add(Stretch());layout.RowStyles.Add(Auto());
        tabs.TabPages.Add(Tab("MOD 资料",ModPage()));tabs.TabPages.Add(Tab("历史更新记录",HistoryPage()));layout.Controls.Add(tabs,0,0);
        var actions=Bar();actions.FlowDirection=FlowDirection.RightToLeft;
        Button(actions,"保存到待发布",()=>{save(Read());saved=true;DialogResult=DialogResult.OK;Close();});
        Button(actions,"预览",()=>{var p=Read();using(var f=tabs.SelectedIndex==1?(Form)new HistoryWindow(p.History,p):new ModWindow("",p,installed))f.ShowDialog(this);});
        layout.Controls.Add(actions,0,1);Controls.Add(layout);
        FillMods(null);FillReleases(content.History.FirstOrDefault());
        FormClosing+=(s,e)=>{if(!saved&&Data.Json(content)!=original&&MessageBox.Show(this,"放弃尚未保存的修改？","关闭编辑器",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)e.Cancel=true;};
    }
    static ListBox List(){return new ListBox{Dock=DockStyle.Fill,IntegralHeight=false,FormattingEnabled=true,HorizontalScrollbar=true};}
    static TextBox Box(bool multi){
        return new TextBox{Dock=DockStyle.Fill,Multiline=multi,AcceptsReturn=multi,AcceptsTab=false,WordWrap=multi,ScrollBars=multi?ScrollBars.Vertical:ScrollBars.None};
    }
    static string Lines(string s){return (s??"").Replace("\r\n","\n").Replace('\r','\n').Replace("\n","\r\n");}
    static string OneLine(string s){return Lines(s).Replace("\r\n","  ");}
    static RowStyle Auto(){return new RowStyle(SizeType.AutoSize);}
    static RowStyle Stretch(){return new RowStyle(SizeType.Percent,100);}
    static TableLayoutPanel Panel(int columns){
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=columns,Margin=Padding.Empty};
        if(columns==1)p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        return p;
    }
    static FlowLayoutPanel Bar(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty};}
    static Label Label(string s){return new Label{Text=s,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(3,5,3,2)};}
    Button Button(Control parent,string text,Action action){
        var button=new Button{Text=text,AutoSize=true};button.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"页面编辑");}};parent.Controls.Add(button);return button;
    }
    static TabPage Tab(string title,Control body){var tab=new TabPage(title){Padding=new Padding(8)};tab.Controls.Add(body);return tab;}
    static TableLayoutPanel Split(Control left,Control right){
        var p=Panel(2);p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68));p.RowStyles.Add(Stretch());
        right.Margin=new Padding(12,0,0,0);p.Controls.Add(left,0,0);p.Controls.Add(right,1,0);return p;
    }
    static void Row(TableLayoutPanel panel,Control c,RowStyle size){
        int i=panel.RowStyles.Count;panel.RowStyles.Add(size);panel.RowCount=i+1;panel.Controls.Add(c,0,i);
    }
    static void Field(TableLayoutPanel panel,string title,Control c,RowStyle size){Row(panel,Label(title),Auto());Row(panel,c,size);}
    Control ModPage(){
        var left=Panel(1);Field(left,"查找 MOD",search,Auto());Row(left,mods,Stretch());
        var right=Panel(1);modFields=right;Row(right,identity,Auto());
        Field(right,"显示名称",name,new RowStyle(SizeType.Percent,21));Field(right,"用途",purpose,new RowStyle(SizeType.Percent,29));
        Field(right,"展开介绍（* 开头的行标为自研）",details,new RowStyle(SizeType.Percent,50));purpose.MaxLength=4000;details.MaxLength=20000;
        var flags=Bar();flags.Controls.Add(own);flags.Controls.Add(Label("NEW"));badge.Items.AddRange(new object[]{"跟随安装记录","显示 NEW","不显示"});flags.Controls.Add(badge);Row(right,flags,Auto());
        var paint=Bar();color.Click+=(s,e)=>{if(Mod==null)return;using(var dialog=new ColorDialog{FullOpen=true}){
            if(!String.IsNullOrEmpty(Mod.VersionColor))dialog.Color=ColorTranslator.FromHtml(Mod.VersionColor);
            if(dialog.ShowDialog(this)==DialogResult.OK){Mod.VersionColor=String.Format("#{0:X2}{1:X2}{2:X2}",dialog.Color.R,dialog.Color.G,dialog.Color.B);ShowColor();}
        }};paint.Controls.Add(color);Button(paint,"恢复默认颜色",()=>{if(Mod!=null){Mod.VersionColor="";ShowColor();}});Row(right,paint,Auto());
        mods.Format+=(s,e)=>{var m=(PageMod)e.ListItem;e.Value=(m.Own?"自研 · ":Fresh(m)?"NEW · ":"")+OneLine(m.Name);};
        mods.SelectedIndexChanged+=(s,e)=>{if(!binding)ShowMod();};search.TextChanged+=(s,e)=>FillMods(Mod);
        name.TextChanged+=(s,e)=>{if(!binding&&Mod!=null)Mod.Name=name.Text;};
        name.Leave+=(s,e)=>{if(!binding)FillMods(Mod);};
        purpose.TextChanged+=(s,e)=>{if(!binding&&Mod!=null)Mod.Purpose=purpose.Text;};
        details.TextChanged+=(s,e)=>{if(!binding&&Mod!=null)Mod.Details=details.Text;};
        own.CheckedChanged+=(s,e)=>{if(!binding&&Mod!=null){var m=Mod;m.Own=own.Checked;FillMods(m);}};
        badge.SelectedIndexChanged+=(s,e)=>{if(!binding&&Mod!=null){var m=Mod;m.New=badge.SelectedIndex==0?(bool?)null:badge.SelectedIndex==1;FillMods(m);}};
        return Split(left,right);
    }
    bool Detected(PageMod m){return detectedNew.Contains(m);}
    bool Fresh(PageMod m){return m.New??Detected(m);}
    void ShowColor(){color.Text=Mod==null||String.IsNullOrEmpty(Mod.VersionColor)?"版本颜色 · 默认":"版本颜色 · "+Mod.VersionColor;}
    void FillMods(PageMod keep){
        var query=search.Text.Trim();
        var visible=content.Mods.Where(m=>query.Length==0||((m.Name??"")+" "+m.Key).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
        binding=true;mods.BeginUpdate();
        try{mods.Items.Clear();mods.Items.AddRange(content.DisplayOrder(visible,m=>m.Key,m=>m,Detected).Cast<object>().ToArray());
            if(keep!=null)mods.SelectedItem=keep;if(mods.SelectedIndex<0&&mods.Items.Count>0)mods.SelectedIndex=0;}
        finally{mods.EndUpdate();binding=false;}ShowMod();
    }
    void ShowMod(){
        binding=true;try{var m=Mod;modFields.Enabled=m!=null;identity.Text=m==null?"没有匹配的 MOD":"标识   "+m.Key;
            name.Text=Lines(m==null?null:m.Name);purpose.Text=Lines(m==null?null:m.Purpose);details.Text=Lines(m==null?null:m.Details);
            own.Checked=m!=null&&m.Own;badge.SelectedIndex=m==null||!m.New.HasValue?0:m.New.Value?1:2;ShowColor();}
        finally{binding=false;}
    }
    Control HistoryPage(){
        var left=Panel(1);Field(left,"版本记录",releases,Auto());
        var groups=Bar();Button(groups,"新建版本",()=>{
            string tag=candidateVersion??"";if(content.History.Any(existing=>existing.Version==tag))tag="";
            var h=new HistoryRelease{Version=tag,PublishedUtc=DateTimeOffset.Now.ToString("o"),Changes=new List<HistoryEntry>()};content.History.Insert(0,h);FillReleases(h);version.Focus();
        });
        Button(groups,"删除版本",()=>{if(Release!=null&&Confirm("删除整个版本及其中的更新记录？")){content.History.Remove(Release);FillReleases(content.History.FirstOrDefault());}});Row(left,groups,Auto());
        Row(left,entries,Stretch());var actions=Bar();
        Button(actions,"添加记录",()=>{if(Release==null)return;var entry=new HistoryEntry{Kind="更新",Mod="",Summary=""};Release.Changes.Add(entry);FillEntries(entry);subject.Focus();});
        Button(actions,"删除记录",()=>{if(Release!=null&&Entry!=null&&Confirm("删除选中的更新记录？")){Release.Changes.Remove(Entry);FillEntries(null);}});Row(left,actions,Auto());
        var right=Panel(1);
        var groupFields=Panel(2);groupFields.AutoSize=true;groupFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));groupFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));
        groupFields.Controls.Add(Label("版本"),0,0);groupFields.Controls.Add(Label("更新时间（含时区）"),1,0);groupFields.Controls.Add(version,0,1);groupFields.Controls.Add(when,1,1);Row(right,groupFields,Auto());
        var fields=Panel(1);historyFields=fields;Field(fields,"类型",kind,Auto());Field(fields,"MOD 名称",subject,new RowStyle(SizeType.Percent,25));Field(fields,"更新说明",summary,new RowStyle(SizeType.Percent,75));summary.MaxLength=20000;Row(right,fields,Stretch());
        releases.Format+=(s,e)=>{var h=(HistoryRelease)e.ListItem;e.Value=String.IsNullOrEmpty(h.Version)?"（填写版本名）":h.Version;};
        entries.Format+=(s,e)=>{var item=(HistoryEntry)e.ListItem;e.Value=item.Kind+" · "+(String.IsNullOrEmpty(item.Mod)?"新记录":OneLine(item.Mod));};
        releases.SelectedIndexChanged+=(s,e)=>{if(!binding)FillEntries(null);};entries.SelectedIndexChanged+=(s,e)=>{if(!binding)ShowEntry();};
        version.TextChanged+=(s,e)=>{if(!binding&&Release!=null)Release.Version=version.Text;};
        version.Leave+=(s,e)=>{if(!binding&&Release!=null)FillReleases(Release,Entry);};
        when.TextChanged+=(s,e)=>{if(!binding&&Release!=null)Release.PublishedUtc=when.Text;};
        kind.TextChanged+=(s,e)=>{if(!binding&&Entry!=null)Entry.Kind=kind.Text;};
        subject.TextChanged+=(s,e)=>{if(!binding&&Entry!=null)Entry.Mod=subject.Text;};
        kind.Leave+=(s,e)=>{if(!binding)FillEntries(Entry);};subject.Leave+=(s,e)=>{if(!binding)FillEntries(Entry);};
        summary.TextChanged+=(s,e)=>{if(!binding&&Entry!=null)Entry.Summary=summary.Text;};
        return Split(left,right);
    }
    bool Confirm(string text){return MessageBox.Show(this,text,"历史记录",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
    void FillReleases(HistoryRelease keep,HistoryEntry selected=null){
        binding=true;releases.Items.Clear();releases.Items.AddRange(content.History.Cast<object>().ToArray());
        if(keep!=null)releases.SelectedItem=keep;binding=false;FillEntries(selected);
    }
    void FillEntries(HistoryEntry keep){
        binding=true;try{
            var h=Release;version.Enabled=when.Enabled=h!=null;version.Text=h==null?"":h.Version;when.Text=h==null?"":h.PublishedUtc;
            entries.BeginUpdate();entries.Items.Clear();if(h!=null)entries.Items.AddRange(h.Changes.Cast<object>().ToArray());
            if(keep!=null)entries.SelectedItem=keep;if(entries.SelectedIndex<0&&entries.Items.Count>0)entries.SelectedIndex=0;
        }finally{entries.EndUpdate();binding=false;}ShowEntry();
    }
    void ShowEntry(){binding=true;try{var e=Entry;historyFields.Enabled=e!=null;kind.Text=e==null?"":e.Kind;subject.Text=Lines(e==null?null:e.Mod);summary.Text=Lines(e==null?null:e.Summary);}finally{binding=false;}}
    public void SelectSection(int section,string versionName){candidateVersion=versionName;tabs.SelectedIndex=section;}
    public PageContent Read(){content.Validate();return Data.Read<PageContent>(Data.Json(content));}
}

}
