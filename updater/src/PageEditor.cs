using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
namespace LcPack {
public class HistoryEditRow {
    public string Version{get;set;} public string Time{get;set;} public string Kind{get;set;} public string Mod{get;set;} public string Summary{get;set;}
}
public sealed class EditorGrid:DataGridView {
    public EditorGrid(){DoubleBuffered=true;ResizeRedraw=true;}
}
public class PageEditor:Form {
    readonly PageContent content; readonly List<InstalledMod> installed; readonly Action<PageContent> save;
    readonly DataGridView mods=Grid(), history=Grid(); readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};
    readonly NumericUpDown font=Number(10,20),names=Number(20,50),versions=Number(10,25),historyNames=Number(20,50);
    bool saved;string candidateVersion;
    public void SelectSection(int section,string version){candidateVersion=version;tabs.SelectedIndex=section;}
    public PageEditor(PageContent data,List<InstalledMod> items,Action<PageContent> write){
        content=Data.Read<PageContent>(Data.Json(data));installed=items;save=write; foreach(var mod in content.Mods)if(!mod.New.HasValue)mod.New=false; using(var icon=typeof(PageEditor).Assembly.GetManifestResourceStream("settings.ico"))if(icon!=null)Icon=new Icon(icon);
        DoubleBuffered=true;Text="编辑接收端页面";Font=new Font("Microsoft YaHei UI",9);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1080,580);MinimumSize=new Size(850,450);StartPosition=FormStartPosition.CenterParent;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(6),WrapContents=true};
        AddButton(bar,"保存到待发布",()=>{try{var p=Read();save(p);saved=true;DialogResult=DialogResult.OK;Close();}catch(Exception e){MessageBox.Show(this,e.Message,"未保存");}});
        AddButton(bar,"预览",()=>{try{var p=Read();using(var f=tabs.SelectedIndex==1?(Form)new HistoryWindow(p.History,p):new ModWindow("",p,installed))f.ShowDialog(this);}catch(Exception e){MessageBox.Show(this,e.Message,"无法预览");}});
        AddButton(bar,"版本颜色",()=>ChooseVersionColor());AddButton(bar,"上移",()=>MoveRow(-1));AddButton(bar,"下移",()=>MoveRow(1));
        AddButton(bar,"添加记录",()=>{Commit();((BindingList<HistoryEditRow>)history.DataSource).Add(new HistoryEditRow{Version=DefaultHistoryVersion(),Time=DefaultHistoryTime(),Kind="更新",Mod="",Summary=""});tabs.SelectedIndex=1;});
        AddButton(bar,"删除记录",()=>{if(tabs.SelectedIndex!=1||history.CurrentRow==null)return;if(MessageBox.Show(this,"删除所选历史记录？保存后才生效。","确认",MessageBoxButtons.OKCancel)==DialogResult.OK)((BindingList<HistoryEditRow>)history.DataSource).RemoveAt(history.CurrentRow.Index);});
        Column(mods,"标识（只读）","Key",150,true);Column(mods,"显示名称","Name",180);Column(mods,"用途","Purpose",300);Check(mods,"NEW","New");Check(mods,"自研","Own");Column(mods,"展开介绍（每行一项，* 开头为自研）","Details",350);
        Column(mods,"版本颜色","VersionColor",95);
        mods.Columns[mods.Columns.Count-1].ToolTipText="填写 #RRGGBB 或点击版本颜色选色；留空恢复默认。";
        mods.DataSource=new BindingList<PageMod>(content.Mods.OrderBy(m=>m.Order).ToList());
        Column(history,"版本分组","Version",90);Column(history,"更新时间（含时区）","Time",200);Column(history,"类型","Kind",65);Column(history,"MOD（中文名称）","Mod",220);Column(history,"更新内容","Summary",420);
        history.DataSource=new BindingList<HistoryEditRow>(content.History.SelectMany(h=>h.Changes.Select(e=>new HistoryEditRow{Version=h.Version,Time=h.PublishedUtc,Kind=e.Kind,Mod=e.Mod,Summary=e.Summary})).ToList());
        Tab("本地 MOD 清单",mods);Tab("历史更新记录",history);
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(12)};
        Field(layout,"字号",font,content.FontSize);Field(layout,"MOD 名称列占比 %",names,content.ModNamePercent);Field(layout,"MOD 版本列占比 %",versions,content.ModVersionPercent);Field(layout,"历史 MOD 列占比 %",historyNames,content.HistoryModPercent);
        layout.Controls.Add(new Label{AutoSize=true,Text="其余宽度用于用途/更新内容。长介绍放入展开区域；历史内容自动换行。\n保存只加入待发布清单，点击管理端“发布更新”后才同步给朋友。"});Tab("排版",layout);
        Controls.Add(tabs);Controls.Add(bar);
        Size normalSize=ClientSize;
        tabs.SelectedIndexChanged+=(s,e)=>{
            bool formatting=tabs.SelectedIndex==2;
            foreach(Button b in bar.Controls.OfType<Button>()){
                if(b.Text=="添加记录"||b.Text=="删除记录")b.Visible=tabs.SelectedIndex==1;
                if(b.Text=="版本颜色")b.Visible=tabs.SelectedIndex==0;
                if(b.Text=="上移"||b.Text=="下移")b.Visible=!formatting;
            }
            if(formatting){normalSize=ClientSize;MinimumSize=new Size(850,300);ClientSize=new Size(ClientSize.Width,300);}
            else {MinimumSize=new Size(850,450);if(ClientSize.Height<normalSize.Height)ClientSize=normalSize;}
        };
        foreach(Button b in bar.Controls.OfType<Button>())if(b.Text=="添加记录"||b.Text=="删除记录")b.Visible=false;

        FormClosing+=(s,e)=>{if(!saved&&MessageBox.Show(this,"关闭编辑器并放弃未保存的编辑？","关闭",MessageBoxButtons.OKCancel)!=DialogResult.OK)e.Cancel=true;};
    }
    string DefaultHistoryVersion(){return !String.IsNullOrEmpty(candidateVersion)?candidateVersion:content.History.Count>0?content.History[0].Version:"";}
    string DefaultHistoryTime(){
        var row=((BindingList<HistoryEditRow>)history.DataSource).FirstOrDefault(r=>r.Version==DefaultHistoryVersion());
        return row==null?DateTimeOffset.Now.ToString("o"):row.Time;
    }
    static DataGridView Grid(){return new EditorGrid{Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,GridColor=Color.FromArgb(232,232,232),ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize,AllowUserToResizeRows=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,DefaultCellStyle=new DataGridViewCellStyle{WrapMode=DataGridViewTriState.True,Padding=new Padding(5,4,5,4),Alignment=DataGridViewContentAlignment.TopLeft},EditMode=DataGridViewEditMode.EditOnKeystrokeOrF2};}
    static void Column(DataGridView grid,string title,string key,int width,bool readOnly=false){grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=title,DataPropertyName=key,FillWeight=width,MinimumWidth=key=="Kind"?50:70,ReadOnly=readOnly,SortMode=DataGridViewColumnSortMode.NotSortable});}
    static void Check(DataGridView grid,string title,string key){grid.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText=title,DataPropertyName=key,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,Width=58,ThreeState=false,ToolTipText=key=="New"?"勾选：显示 NEW；不勾选：不显示 NEW（不影响 MOD 启用状态）":"自研标记仅用于实际自研或修改的功能"});}
    static NumericUpDown Number(int min,int max){return new NumericUpDown{Minimum=min,Maximum=max,Width=100};}
    static void Field(FlowLayoutPanel p,string title,NumericUpDown input,int value){var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,6)};row.Controls.Add(new Label{Text=title,Width=200,Height=28,TextAlign=ContentAlignment.MiddleLeft});input.Value=value;row.Controls.Add(input);p.Controls.Add(row);}
    static void AddButton(FlowLayoutPanel p,string title,Action action){var b=new Button{Text=title,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(9,4,9,4),Margin=new Padding(3),MinimumSize=new Size(56,28)};b.Click+=(s,e)=>action();p.Controls.Add(b);}
    void Tab(string title,Control control){var t=new TabPage(title);t.Controls.Add(control);tabs.TabPages.Add(t);}
    void ChooseVersionColor(){
        Commit();if(mods.CurrentRow==null)return;var row=(PageMod)mods.CurrentRow.DataBoundItem;
        using(var picker=new ColorDialog{FullOpen=true}){
            if(!String.IsNullOrEmpty(row.VersionColor)&&System.Text.RegularExpressions.Regex.IsMatch(row.VersionColor,@"\A#[0-9a-fA-F]{6}\z"))picker.Color=ColorTranslator.FromHtml(row.VersionColor);
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            row.VersionColor="#"+picker.Color.R.ToString("X2")+picker.Color.G.ToString("X2")+picker.Color.B.ToString("X2");mods.Refresh();
        }
    }
    void Commit(){mods.EndEdit();history.EndEdit();BindingContext[mods.DataSource].EndCurrentEdit();BindingContext[history.DataSource].EndCurrentEdit();}
    void MoveRow(int delta){Commit();var grid=tabs.SelectedIndex==0?mods:history;if(tabs.SelectedIndex>1||grid.CurrentRow==null)return;int i=grid.CurrentRow.Index,j=i+delta;if(j<0||j>=grid.Rows.Count)return;var list=(System.Collections.IList)grid.DataSource;var row=list[i];list.RemoveAt(i);list.Insert(j,row);grid.CurrentCell=grid.Rows[j].Cells[0];}
    public PageContent Read(){Commit();content.Mods=((BindingList<PageMod>)mods.DataSource).ToList();for(int i=0;i<content.Mods.Count;i++)content.Mods[i].Order=i;
        var rows=((BindingList<HistoryEditRow>)history.DataSource).ToList();
        if(rows.GroupBy(r=>r.Version).Any(g=>g.Select(r=>r.Time).Distinct().Count()>1))throw new InvalidOperationException("同一版本的更新时间必须一致。");
        content.History=rows.GroupBy(r=>r.Version).Select(g=>new HistoryRelease{Version=g.Key,PublishedUtc=g.First().Time,Changes=g.Select(r=>new HistoryEntry{Kind=r.Kind,Mod=r.Mod,Summary=r.Summary}).ToList()}).ToList();
        content.FontSize=(int)font.Value;content.ModNamePercent=(int)names.Value;content.ModVersionPercent=(int)versions.Value;content.HistoryModPercent=(int)historyNames.Value;content.Validate();return content;}
}
}
