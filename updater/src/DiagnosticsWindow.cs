using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LcPack {
public sealed class DiagnosticsWindow : Form {
    readonly string root;
    readonly ListBox sessions=new ListBox();
    readonly DiagnosticsTimeline timeline=new DiagnosticsTimeline();
    readonly ComboBox filter=new ComboBox();
    readonly Button save=new Button(), export=new Button();
    readonly Label pluginStatus=new Label(), monitorStatus=new Label(), keyStatus=new Label(), liveStatus=new Label(), editStatus=new Label();
    readonly ContextMenuStrip settings=new ContextMenuStrip(), tools=new ContextMenuStrip();
    DiagnosticReview review;
    GameRecord record;
    bool syncing,busy,resized,monitorEnabled;
    string markKey="F8";

    public DiagnosticsWindow(string root) {
        this.root=root;
        SuspendLayout();
        Text="故障诊断";
        var searchIcon=DiagnosticDialogs.ExplorerSearchIcon();
        if(searchIcon!=null){ Icon=searchIcon;Disposed+=(s,e)=>searchIcon.Dispose(); }
        else ShowIcon=false;
        StartPosition=FormStartPosition.CenterParent;
        AutoScaleMode=AutoScaleMode.Dpi;
        AutoScaleDimensions=new SizeF(96,96);
        Font=new Font("Microsoft YaHei UI",9);
        ClientSize=new Size(840,282);
        MinimumSize=new Size(680,260);
        var page=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=3 };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(page);

        var top=Strip();top.Margin=new Padding(0,0,0,7);
        page.Controls.Add(top,0,0);
        foreach(var label in new[]{pluginStatus,monitorStatus,keyStatus,new Label{Text="仅本地保存"},liveStatus}){
            label.AutoSize=true;label.Margin=new Padding(4,8,10,4);top.Controls.Add(label);
        }
        var config=Button("监控设置 ▾");
        config.Click+=(s,e)=>Guard(()=>OpenSettings(config));
        top.Controls.Add(config);
        var refresh=Button("刷新");refresh.Click+=async(s,e)=>await RefreshList();top.Controls.Add(refresh);

        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,216));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.Controls.Add(body,0,1);
        var history=Section();
        history.Controls.Add(Title("游戏记录"),0,0);
        sessions.Dock=DockStyle.Fill;
        sessions.IntegralHeight=false;
        sessions.DrawMode=DrawMode.OwnerDrawFixed;
        sessions.FontChanged+=(s,e)=>SizeRows();
        sessions.HandleCreated+=(s,e)=>SizeRows();
        DpiChanged+=(s,e)=>BeginInvoke(new Action(()=>{SizeRows();timeline.Rebuild();}));
        sessions.DrawItem+=DrawRecord;
        sessions.SelectedIndexChanged+=(s,e)=>Guard(SelectRecord);
        history.Controls.Add(sessions,0,1);body.Controls.Add(history,0,0);

        var panel=Section();panel.Margin=new Padding(8,0,0,0);panel.RowCount=3;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var ribbon=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true,Margin=Padding.Empty};
        ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ribbon.Controls.Add(Title("故障时间轴"),0,0);
        filter.DropDownStyle=ComboBoxStyle.DropDownList;filter.Width=122;
        filter.Items.AddRange(new object[]{"手动标记","自动异常","最近删除"});filter.SelectedIndex=0;
        filter.SelectedIndexChanged+=(s,e)=>BindTimeline();
        ribbon.Controls.Add(filter,1,0);panel.Controls.Add(ribbon,0,0);
        timeline.Dock=DockStyle.Fill;timeline.Margin=new Padding(0,3,0,0);
        timeline.Selected+=id=>Guard(()=>OpenMark(id));
        panel.Controls.Add(timeline,0,1);
        var hint=new Label { Text="悬停预览截图，点击圆点或时间查看现场",AutoSize=true,ForeColor=SystemColors.GrayText,Margin=new Padding(2,4,0,0) };
        panel.Controls.Add(hint,0,2);body.Controls.Add(panel,1,0);

        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true,Margin=new Padding(0,8,0,0)};
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editStatus.AutoSize=true;editStatus.Anchor=AnchorStyles.Left;bottom.Controls.Add(editStatus,0,0);
        var actions=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Anchor=AnchorStyles.Right,Margin=Padding.Empty };
        var more=Button("记录管理 ▾");more.Click+=(s,e)=>Guard(()=>OpenTools(more));actions.Controls.Add(more);
        StyleButton(save,"保存更改");save.Click+=(s,e)=>Guard(Save);actions.Controls.Add(save);
        StyleButton(export,"导出诊断 ZIP");export.Click+=async(s,e)=>await Export();actions.Controls.Add(export);
        bottom.Controls.Add(actions,1,0);page.Controls.Add(bottom,0,2);
        ResizeEnd+=(s,e)=>resized=true;
        Shown+=async(s,e)=>await RefreshList();
        FormClosing+=(s,e)=>{if(busy||!Discard())e.Cancel=true;};
        FormClosed+=(s,e)=>{settings.Dispose();tools.Dispose();};
        ResumeLayout(true);
        Changed();
    }
    static TableLayoutPanel Section() {
        var p=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.RowStyles.Add(new RowStyle(SizeType.AutoSize));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));return p;
    }
    static FlowLayoutPanel Strip(){return new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=true};}
    static Label Title(string text){return new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(1,4,1,6)};}
    static void StyleButton(Button b,string text){b.Text=text;b.AutoSize=true;b.Padding=new Padding(4,1,4,1);b.Margin=new Padding(3,1,0,1);}
    static Button Button(string text){var b=new Button();StyleButton(b,text);return b;}
    void Guard(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"故障诊断");}}
    bool Discard(){
        return review==null||!review.Dirty||MessageBox.Show(this,"放弃本局尚未保存的更改？","未保存更改",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    }
    void SizeRows(){sessions.ItemHeight=2*(TextRenderer.MeasureText("游戏",sessions.Font).Height+3);}
    void DrawRecord(object sender,DrawItemEventArgs e){
        e.DrawBackground();
        if(e.Index<0||e.Index>=sessions.Items.Count)return;
        var item=(GameRecord)sessions.Items[e.Index];
        int pad=6*DeviceDpi/96;
        var r=Rectangle.Inflate(e.Bounds,-pad,0);
        r.Height=e.Bounds.Height/2;r.Y+=1;
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics,item.Title,Font,r,e.ForeColor,flags);
        r.Y+=r.Height-1;
        TextRenderer.DrawText(e.Graphics,item.Subtitle,Font,r,(e.State&DrawItemState.Selected)!=0?e.ForeColor:Color.DimGray,flags);
        e.DrawFocusRectangle();
    }
    async Task RefreshList() {
        if(busy||!Discard())return;
        string id=record==null?null:record.Session.Id;
        busy=true;Enabled=false;
        try{
            var rows=await Task.Run(()=>DiagnosticsStore.Sessions(root).Select(GameRecord.From).ToList());
            syncing=true;sessions.DataSource=rows;
            sessions.SelectedItem=rows.FirstOrDefault(r=>r.Session.Id==id)??rows.FirstOrDefault();
            record=null;review=null;syncing=false;
            SelectRecord();
            ReadSettings();
        }catch(Exception ex){MessageBox.Show(this,"读取游戏记录失败。"+ex.Message);}
        finally{syncing=false;busy=false;Enabled=true;Changed();}
    }
    void SelectRecord(){
        if(syncing)return;
        var next=sessions.SelectedItem as GameRecord;
        if(next!=record&&!Discard()){syncing=true;sessions.SelectedItem=record;syncing=false;return;}
        record=next;review=null;timeline.Bind(null,filter.SelectedIndex);
        if(record!=null)review=new DiagnosticReview(record.Session.Path);
        BindTimeline();Changed();
    }
    void BindTimeline(){
        timeline.Bind(review,filter.SelectedIndex);
        if(!resized&&WindowState==FormWindowState.Normal&&IsHandleCreated){
            int dpi=DeviceDpi;
            int chrome=ClientSize.Height-timeline.Height;
            int target=Math.Max(250*dpi/96,Math.Min(490*dpi/96,chrome+timeline.ContentHeight+8*dpi/96));
            int available=Screen.FromControl(this).WorkingArea.Height-SystemInformation.CaptionHeight-24;
            ClientSize=new Size(ClientSize.Width,Math.Min(target,available));
        }
    }
    void OpenMark(string id) {
        if(review==null)return;
        var mark=review.Marks.FirstOrDefault(m=>m.Id==id);
        if(mark==null)return;
        DiagnosticDialogs.EditMark(this,review,mark);
        timeline.Rebuild();Changed();
    }
    void Changed(){
        bool live=review!=null&&DiagnosticsStore.Active(review.Path);
        bool editable=review!=null&&!live&&!busy;
        save.Enabled=editable;export.Enabled=editable&&review.Confirmed&&!review.Dirty;
        if(live)editStatus.Text="本局正在记录 · 只读预览";
        else if(review==null)editStatus.Text="暂无游戏记录";
        else if(review.Dirty)editStatus.Text="有未保存的更改";
        else editStatus.Text=review.Confirmed?"已保存，可以导出":"确认时间轴后请先保存";
        editStatus.ForeColor=review!=null&&review.Dirty?Color.SaddleBrown:SystemColors.GrayText;
    }
    void Save(){
        if(review==null)return;
        review.Save();record.Update(review);sessions.Invalidate();BindTimeline();Changed();
    }
    async Task Export(){
        if(review==null||!export.Enabled)return;
        using(var dialog=new SaveFileDialog{Filter="诊断 ZIP|*.zip",FileName="故障诊断-"+record.Session.Id+".zip"}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            busy=true;Enabled=false;
            try{await Task.Run(()=>review.Export(dialog.FileName));DiagnosticDialogs.ExportComplete(this);}
            catch(Exception ex){MessageBox.Show(this,ex.Message);}
            finally{busy=false;Enabled=true;Changed();}
        }
    }
    void ReadSettings(){
        bool installed=File.Exists(Path.Combine(root,DiagnosticsStore.PluginPath));
        string cfg=Path.Combine(root,"BepInEx/config/nt1ho.lethaldiagnostics.cfg");
        markKey="F8";monitorEnabled=true;
        if(File.Exists(cfg))foreach(string line in File.ReadLines(cfg)){
            string[] pair=line.Split(new[]{'='},2);
            if(pair.Length!=2)continue;
            if(pair[0].Trim()=="MarkKey")markKey=pair[1].Trim();
            if(pair[0].Trim()=="Enabled")Boolean.TryParse(pair[1].Trim(),out monitorEnabled);
        }
        pluginStatus.Text=installed?"插件已安装":"插件未安装";
        monitorStatus.Text=installed?(monitorEnabled?"监控已启用":"监控已停用"):"等待安装";
        monitorStatus.ForeColor=installed&&monitorEnabled?Color.DarkGreen:Color.DimGray;
        keyStatus.Text="标记键 "+markKey;liveStatus.Text=Engine.GameRunning()?"游戏运行中":"";
        liveStatus.Visible=liveStatus.Text.Length>0;
    }
    void Add(ToolStripItemCollection items,string text,Action action,bool enabled){
        var item=items.Add(text);item.Enabled=enabled;item.Click+=(s,e)=>Guard(action);
    }
    static void Reset(ContextMenuStrip menu){
        while(menu.Items.Count>0)menu.Items[0].Dispose();
    }
    void OpenSettings(Control anchor){
        ReadSettings();Reset(settings);
        bool stopped=!Engine.GameRunning();
        Add(settings.Items,"安装或修复插件",()=>{DiagnosticsStore.Install(root);ReadSettings();},stopped);
        Add(settings.Items,monitorEnabled?"停用监控":"启用监控",()=>{DiagnosticsStore.Configure(root,!monitorEnabled,markKey);ReadSettings();},stopped);
        var keys=new ToolStripMenuItem("标记快捷键"){Enabled=stopped};settings.Items.Add(keys);
        for(int i=1;i<=12;i++){
            string key="F"+i;
            var item=new ToolStripMenuItem(key){Checked=key==markKey};
            item.Click+=(s,e)=>Guard(()=>{DiagnosticsStore.Configure(root,monitorEnabled,key);ReadSettings();});
            keys.DropDownItems.Add(item);
        }
        if(!stopped)settings.Items.Add("退出游戏后可修改设置").Enabled=false;
        settings.Show(anchor,0,anchor.Height);
    }
    void OpenTools(Control anchor){
        Reset(tools);
        bool has=review!=null, closed=has&&!DiagnosticsStore.Active(review.Path), clean=has&&!review.Dirty;
        Add(tools.Items,"整局备注与联机编号",()=>{DiagnosticDialogs.SessionOptions(this,review);Changed();},has);
        Add(tools.Items,"查看已保存摘要",SavedSummary,has);
        tools.Items.Add(new ToolStripSeparator());
        Add(tools.Items,"恢复异常记录",()=>{int n=DiagnosticsStore.Recover(review.Path);MessageBox.Show(this,"记录已整理。未完整写入的行数为 "+n+"。");},closed&&clean);
        var cleanup=tools.Items.Add("清理普通旧记录");
        cleanup.Enabled=clean||!has;
        cleanup.Click+=async(s,e)=>{try{int n=DiagnosticsStore.Cleanup(root);await RefreshList();MessageBox.Show(this,"已清理 "+n+" 局普通旧记录。");}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
        var recycle=tools.Items.Add("整局移至回收站");recycle.Enabled=closed&&clean;
        recycle.Click+=async(s,e)=>{
            if(MessageBox.Show(this,"将整局记录连同截图移至回收站？","删除游戏记录",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            try{
                if(DiagnosticsStore.Active(review.Path))throw new IOException("本局正在记录。");
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(review.Path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                await RefreshList();
            }catch(Exception ex){MessageBox.Show(this,ex.Message);}
        };
        tools.Show(anchor,0,anchor.Height);
    }
    void SavedSummary(){
        var saved=new DiagnosticReview(review.Path);
        var text=new StringBuilder(record.Title).AppendLine().AppendLine(record.Subtitle);
        if(saved.Note("group").Length>0)text.AppendLine("联机编号　"+saved.Note("group"));
        if(saved.Note("sessionNote").Length>0)text.AppendLine("整局备注　"+saved.Note("sessionNote"));
        foreach(var mark in saved.Marks.Where(m=>!saved.Deleted(m.Id))){
            text.AppendLine().AppendLine(PlayerText.Time(mark)+"   "+PlayerText.Kind(mark));
            text.AppendLine(PlayerText.Summary(mark));
            if(saved.Note(mark.Id).Length>0)text.AppendLine("备注　"+saved.Note(mark.Id));
        }
        using(var viewer=new Form{Text="已保存的游戏记录",StartPosition=FormStartPosition.CenterParent,Size=new Size(520,360),Font=Font}){
            viewer.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=text.ToString()});viewer.ShowDialog(this);
        }
    }
}
}
