using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace LcPack {
public sealed class DiagnosticsWindow : Form {
    readonly string root;readonly ListBox sessions=new ListBox(),marks=new ListBox();readonly TextBox note=new TextBox(),group=new TextBox(),report=new TextBox();readonly PictureBox photo=new PictureBox();readonly CheckBox pin=new CheckBox();readonly Label status=new Label();readonly ComboBox key=new ComboBox();
    DiagnosticSession Current {get{return sessions.SelectedItem as DiagnosticSession;}}
    string MarkId {get{return marks.SelectedItem as string??"sessionNote";}}
    public DiagnosticsWindow(string root){
        this.root=root;Text="游戏故障诊断 · 本地记录";Size=new Size(1120,780);MinimumSize=new Size(900,650);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",9);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(8)};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));Controls.Add(layout);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true};layout.Controls.Add(toolbar,0,0);layout.SetColumnSpan(toolbar,2);
        Add(toolbar,"安装／修复插件",()=>{DiagnosticsStore.Install(root);Status();});Add(toolbar,"启用监控",()=>{DiagnosticsStore.Configure(root,true,key.Text);Status();});Add(toolbar,"停用监控",()=>{DiagnosticsStore.Configure(root,false,key.Text);Status();});
        toolbar.Controls.Add(new Label{Text="标记键",AutoSize=true,Margin=new Padding(8)});key.DropDownStyle=ComboBoxStyle.DropDownList;key.Width=60;key.Items.AddRange(Enumerable.Range(1,12).Select(i=>(object)("F"+i)).ToArray());key.SelectedItem="F8";toolbar.Controls.Add(key);Add(toolbar,"刷新",RefreshList);
        status.AutoSize=true;status.MaximumSize=new Size(1040,0);toolbar.Controls.Add(status);
        sessions.Dock=DockStyle.Fill;layout.Controls.Add(sessions,0,1);sessions.SelectedIndexChanged+=async(s,e)=>await SelectSession();photo.Dock=DockStyle.Fill;photo.SizeMode=PictureBoxSizeMode.Zoom;photo.BackColor=Color.FromArgb(35,35,35);layout.Controls.Add(photo,1,1);
        var edit=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=6,ColumnCount=1};edit.RowStyles.Add(new RowStyle(SizeType.Absolute,72));edit.RowStyles.Add(new RowStyle(SizeType.Absolute,25));edit.RowStyles.Add(new RowStyle(SizeType.Absolute,26));edit.RowStyles.Add(new RowStyle(SizeType.Absolute,26));edit.RowStyles.Add(new RowStyle(SizeType.Percent,100));edit.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.Controls.Add(edit,0,2);
        marks.Dock=DockStyle.Fill;edit.Controls.Add(marks);marks.SelectedIndexChanged+=(s,e)=>SelectMark();edit.Controls.Add(new Label{Text="共同联机编号（房主和朋友填相同文字）",AutoSize=true});group.Dock=DockStyle.Fill;edit.Controls.Add(group);edit.Controls.Add(new Label{Text="选中故障标记，再填写问题备注：",AutoSize=true});note.Multiline=true;note.ScrollBars=ScrollBars.Vertical;note.Dock=DockStyle.Fill;edit.Controls.Add(note);pin.Text="保留此会话（已标记会话自动保护）";pin.AutoSize=true;edit.Controls.Add(pin);
        report.Multiline=true;report.ReadOnly=true;report.ScrollBars=ScrollBars.Both;report.WordWrap=false;report.Dock=DockStyle.Fill;layout.Controls.Add(report,1,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(actions,0,3);layout.SetColumnSpan(actions,2);Add(actions,"保存备注",Save);Add(actions,"恢复异常记录",()=>{Need();DiagnosticsStore.Recover(Current.Path);RefreshList();});
        var export=new Button{Text="导出诊断 ZIP",AutoSize=true};actions.Controls.Add(export);export.Click+=async(s,e)=>{try{Need();Save();using(var d=new SaveFileDialog{Filter="诊断 ZIP|*.zip",FileName="LGP-diagnostics-"+Current.Id+".zip"})if(d.ShowDialog(this)==DialogResult.OK){string path=Current.Path;export.Enabled=false;await Task.Run(()=>DiagnosticsStore.Export(path,d.FileName));MessageBox.Show(this,"已导出。截图和 MOD 日志可能含个人信息，请检查后再分享。");}}catch(Exception ex){MessageBox.Show(this,ex.Message);}finally{export.Enabled=true;}};
        Add(actions,"清理普通旧记录",()=>{int n=DiagnosticsStore.Cleanup(root);RefreshList();MessageBox.Show(this,"已清理 "+n+" 次普通会话；标记、保留及异常记录不自动删除。");});
        Add(actions,"移至回收站",()=>{Need();if(DiagnosticsStore.Active(Current.Path))throw new IOException("请先退出游戏。");if(MessageBox.Show(this,"将整次会话（含截图和所有标记）移至回收站？","删除诊断",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(Current.Path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);RefreshList();});
        Shown+=(s,e)=>RefreshList();FormClosed+=(s,e)=>{if(photo.Image!=null)photo.Image.Dispose();};
    }
    void Add(Control c,string text,Action action){var b=new Button{Text=text,AutoSize=true};c.Controls.Add(b);b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message);}};}
    void Need(){if(Current==null)throw new IOException("请先选择一次游戏记录。");}
    void Status(){string plugin=Path.Combine(root,DiagnosticsStore.PluginPath);string cfg=Path.Combine(root,"BepInEx/config/nt1ho.lethaldiagnostics.cfg");string content=File.Exists(cfg)?File.ReadAllText(cfg):"";status.Text=(File.Exists(plugin)?"插件已安装":"插件未安装")+" · "+(content.Contains("Enabled = false")?"监控已停用":"监控默认启用")+" · 只保存在本机 · 改键后点启用；运行状态见会话列表";}
    void RefreshList(){string id=Current==null?null:Current.Id;DiagnosticsStore.Cleanup(root);var list=DiagnosticsStore.Sessions(root);sessions.DataSource=list;if(id!=null)for(int i=0;i<sessions.Items.Count;i++)if(((DiagnosticSession)sessions.Items[i]).Id==id)sessions.SelectedIndex=i;Status();long bytes=list.Sum(s=>s.Bytes);status.Text+=" · 共 "+(bytes/1048576.0).ToString("F0")+" MB";if(bytes>1024L*1024*1024)status.Text+=" · 超过1GB，请导出并手动整理受保护记录";}
    async Task SelectSession(){
        var selected=Current;if(selected==null)return;
        try{marks.Items.Clear();foreach(string f in Directory.GetFiles(selected.Path,"mark-*.json").OrderBy(p=>p))marks.Items.Add(Path.GetFileNameWithoutExtension(f).Substring(5));if(marks.Items.Count==0)marks.Items.Add("sessionNote");var notes=DiagnosticsStore.Read(Path.Combine(selected.Path,"notes.json"));group.Text=Convert.ToString(DiagnosticsStore.Value(notes,"group"));pin.Checked=Convert.ToBoolean(DiagnosticsStore.Value(notes,"pinned")??false);marks.SelectedIndex=0;SelectMark();report.Text="正在整理……";string text=await Task.Run(()=>DiagnosticsStore.Report(selected.Path));if(Current==selected)report.Text=text;}
        catch(Exception ex){report.Text="记录读取失败："+ex.Message;}
    }
    void SelectMark(){if(Current==null)return;try{var notes=DiagnosticsStore.Read(Path.Combine(Current.Path,"notes.json"));note.Text=Convert.ToString(DiagnosticsStore.Value(notes,MarkId));if(photo.Image!=null){photo.Image.Dispose();photo.Image=null;}string image=Path.Combine(Current.Path,"shot-"+MarkId+".png");if(File.Exists(image)){using(var original=Image.FromFile(image))photo.Image=new Bitmap(original);}}catch(Exception ex){status.Text=ex.Message;}}
    void Save(){Need();DiagnosticsStore.SaveNote(Current.Path,MarkId,note.Text,group.Text,pin.Checked);report.Text=DiagnosticsStore.Report(Current.Path);}
}
}
