using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using D=System.Windows.Data;

namespace LcPack {
static class PublisherProgram {
    [STAThread] static void Main() {UiStartup.Run(()=>Application.Run(new PublisherWindow()));}
}
public class AdminRow {
    public string Name{get;set;}public string Version{get;set;}public string Purpose{get;set;}public string Count{get;set;}public string Size{get;set;}public string Status{get;set;}
    public string[] Paths{get;set;}public ModGroup Group{get;set;}
}
public class AdminTable:C.ListView {
    readonly C.GridView columns=new C.GridView();bool files;
    public AdminTable() {
        View=columns;columns.AllowsColumnReorder=false;SelectionMode=C.SelectionMode.Extended;
        Background=M.Brushes.White;Foreground=M.Brushes.Black;BorderBrush=M.Brushes.LightGray;BorderThickness=new W.Thickness(0,1,0,1);
        FontFamily=new M.FontFamily("Microsoft YaHei UI");FontSize=12;UseLayoutRounding=true;SnapsToDevicePixels=true;
        C.VirtualizingPanel.SetIsVirtualizing(this,true);C.VirtualizingPanel.SetVirtualizationMode(this,C.VirtualizationMode.Recycling);C.ScrollViewer.SetCanContentScroll(this,true);
        M.TextOptions.SetTextFormattingMode(this,M.TextFormattingMode.Display);
        var row=new W.Style(typeof(C.ListViewItem));row.Setters.Add(new W.Setter(W.FrameworkElement.HeightProperty,25.0));row.Setters.Add(new W.Setter(C.Control.HorizontalContentAlignmentProperty,W.HorizontalAlignment.Stretch));
        var border=new W.FrameworkElementFactory(typeof(C.Border));border.SetValue(C.Border.BackgroundProperty,new W.TemplateBindingExtension(C.Control.BackgroundProperty));
        var presenter=new W.FrameworkElementFactory(typeof(C.GridViewRowPresenter));presenter.SetBinding(C.GridViewRowPresenter.ContentProperty,new D.Binding("Content"){RelativeSource=D.RelativeSource.TemplatedParent});presenter.SetBinding(C.GridViewRowPresenter.ColumnsProperty,new D.Binding("View.Columns"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListView),1)});presenter.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);border.AppendChild(presenter);
        row.Setters.Add(new W.Setter(C.Control.TemplateProperty,new C.ControlTemplate(typeof(C.ListViewItem)){VisualTree=border}));
        var removed=new W.DataTrigger{Binding=new D.Binding("Status"),Value="待删除"};removed.Setters.Add(new W.Setter(C.Control.ForegroundProperty,M.Brushes.Firebrick));row.Triggers.Add(removed);
        var selected=new W.Trigger{Property=C.ListViewItem.IsSelectedProperty,Value=true};selected.Setters.Add(new W.Setter(C.Control.BackgroundProperty,W.SystemColors.HighlightBrush));selected.Setters.Add(new W.Setter(C.Control.ForegroundProperty,W.SystemColors.HighlightTextBrush));row.Triggers.Add(selected);ItemContainerStyle=row;
        var header=new W.Style(typeof(C.GridViewColumnHeader));header.Setters.Add(new W.Setter(C.Control.BackgroundProperty,M.Brushes.White));header.Setters.Add(new W.Setter(C.Control.PaddingProperty,new W.Thickness(6,5,6,5)));header.Setters.Add(new W.Setter(C.Control.HorizontalContentAlignmentProperty,W.HorizontalAlignment.Left));columns.ColumnHeaderContainerStyle=header;
        SizeChanged+=(s,e)=>FitColumns();
    }
    public void Configure(bool fileView) {
        files=fileView;columns.Columns.Clear();
        Add(files?"相对路径":"MOD / 组件","Name",220);
        if(!files){Add("版本","Version",115);Add("用途","Purpose",250);Add("文件","Count",50);}
        Add("大小","Size",80);Add("变更","Status",80);FitColumns();
    }
    void Add(string title,string property,double width){columns.Columns.Add(new C.GridViewColumn{Header=title,DisplayMemberBinding=new D.Binding(property),Width=width});}
    void FitColumns(){
        double width=Math.Max(0,ActualWidth-W.SystemParameters.VerticalScrollBarWidth-8);
        if(columns.Columns.Count==3)columns.Columns[0].Width=Math.Max(300,width-160);
        else if(columns.Columns.Count==6){double flexible=width-325;columns.Columns[0].Width=Math.Max(205,flexible*0.40);columns.Columns[2].Width=Math.Max(230,flexible-columns.Columns[0].Width);}
    }
}
public class PublisherWindow:Form {
    readonly Workspace workspace;readonly AdminTable list=new AdminTable();readonly C.TextBox version=new C.TextBox();readonly C.TextBlock status=new C.TextBlock();readonly C.Grid toolbar=new C.Grid();readonly C.ComboBox fileView=new C.ComboBox();readonly C.Button remove=new C.Button();bool busy;
    bool Files {get{return fileView.SelectedIndex==1;}}
    public PublisherWindow():this(new Workspace(Workspace.DefaultRoot)) {}
    public PublisherWindow(Workspace source) {
        SuspendLayout();DoubleBuffered=true;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        Text="Lethal company pack Updater Admin";ClientSize=new Size(920,560);MinimumSize=new Size(860,400);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9);
        Icon=Icon.ExtractAssociatedIcon(typeof(PublisherWindow).Assembly.Location);
        workspace=source;var cloudAccount=CosSettings.Load(workspace);if(cloudAccount!=null)Cloud.Configure(cloudAccount.channel);
        var body=new C.DockPanel{Background=W.SystemColors.ControlBrush,UseLayoutRounding=true,SnapsToDevicePixels=true,AllowDrop=true};C.TextBlock.SetFontFamily(body,new M.FontFamily("Microsoft YaHei UI"));C.TextBlock.SetFontSize(body,12);
        toolbar.Margin=new W.Thickness(10,10,10,10);
        for(int i=0;i<9;i++)toolbar.ColumnDefinitions.Add(new C.ColumnDefinition{Width=i==6?new W.GridLength(1,W.GridUnitType.Star):W.GridLength.Auto});
        var add=Button("添加 ▾",null);var addMenu=new C.ContextMenu();MenuItem(addMenu,"文件…",async()=>{using(var d=new OpenFileDialog{Multiselect=true,Title="添加 MOD 文件"})if(d.ShowDialog(this)==DialogResult.OK)await Import(d.FileNames);});MenuItem(addMenu,"文件夹…",async()=>{using(var d=new FolderBrowserDialog())if(d.ShowDialog(this)==DialogResult.OK)await Import(new[]{d.SelectedPath});});add.ContextMenu=addMenu;add.Click+=(s,e)=>{addMenu.PlacementTarget=add;addMenu.IsOpen=true;};Place(add,0);
        remove.Content="移除";StyleButton(remove);remove.IsEnabled=false;remove.Click+=async(s,e)=>await Work(async()=>{var paths=SelectedPaths();if(paths.Length==0)return;if(!Files&&MessageBox.Show(this,"将所选 MOD 的 "+paths.Length+" 个文件标记为删除？发布后才会同步给朋友。","移除 MOD",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;await Task.Run(()=>workspace.Remove(paths));RefreshList();});Place(remove,1);
        Place(new C.TextBlock{Text="新版本",VerticalAlignment=W.VerticalAlignment.Center,Margin=new W.Thickness(14,0,8,0)},2);
        version.Width=116;version.MaxLength=64;version.Padding=new W.Thickness(6,4,6,4);version.VerticalContentAlignment=W.VerticalAlignment.Center;version.ToolTip="只改页面资料可保留当前版本；MOD 更新请明确填写版本号";version.Text=workspace.Current.releaseVersion??"";W.Automation.AutomationProperties.SetName(version,"发布版本");Place(version,3);
        var publish=Button("发布更新",Publish);publish.Margin=new W.Thickness(10,0,0,0);Place(publish,4);
        var editPages=Button("页面编辑",EditPages);editPages.Margin=new W.Thickness(10,0,0,0);editPages.ToolTip="编辑 MOD 清单、历史更新记录和排版";Place(editPages,5);
        fileView.Items.Add("MOD 视图");fileView.Items.Add("文件视图");fileView.SelectedIndex=0;fileView.MinWidth=92;fileView.Margin=new W.Thickness(8,0,6,0);fileView.VerticalContentAlignment=W.VerticalAlignment.Center;fileView.SelectionChanged+=(s,e)=>RefreshList();W.Automation.AutomationProperties.SetName(fileView,"清单视图");Place(fileView,7);
        var more=Button("更多 ▾",null);more.Margin=new W.Thickness(0);var moreMenu=new C.ContextMenu();MenuItem(moreMenu,"设置…",()=>{Setup();return Task.FromResult(0);});MenuItem(moreMenu,"重新载入云端",async()=>{if(MessageBox.Show(this,"用云端清单替换当前待发布清单？本地源文件不会改变。","重新载入",MessageBoxButtons.OKCancel)==DialogResult.OK)await Reload();});more.ContextMenu=moreMenu;more.Click+=(s,e)=>{moreMenu.PlacementTarget=more;moreMenu.IsOpen=true;};Place(more,8);
        C.DockPanel.SetDock(toolbar,C.Dock.Top);body.Children.Add(toolbar);
        status.Margin=new W.Thickness(12,8,12,8);status.TextTrimming=W.TextTrimming.CharacterEllipsis;C.DockPanel.SetDock(status,C.Dock.Bottom);body.Children.Add(status);body.Children.Add(list);
        var host=new ElementHost{Dock=DockStyle.Fill,Child=body};Controls.Add(host);
        list.SelectionChanged+=(s,e)=>remove.IsEnabled=!busy&&list.SelectedItems.Count>0;
        list.MouseDoubleClick+=(s,e)=>{if(!busy&&!Files)ShowDetails();};
        body.PreviewDragOver+=(s,e)=>{e.Effects=!busy&&e.Data.GetDataPresent(W.DataFormats.FileDrop)?W.DragDropEffects.Copy:W.DragDropEffects.None;e.Handled=true;};
        body.PreviewDrop+=async(s,e)=>{e.Handled=true;if(!busy&&e.Data.GetDataPresent(W.DataFormats.FileDrop))await Work(()=>Import((string[])e.Data.GetData(W.DataFormats.FileDrop)));};
        FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};RefreshList();ResumeLayout(true);
        Shown+=async(s,e)=>await Work(async()=>{
            if(CosSettings.Load(workspace)==null){status.Text="请在“更多 → 设置”中填写上传凭据。";return;}
            if(!File.Exists(Path.Combine(workspace.Root,"draft.json"))||(String.IsNullOrEmpty(workspace.Current.releaseVersion)&&workspace.Current.deletes.Count==0&&workspace.Current.files.All(f=>!String.IsNullOrEmpty(f.url))))await Reload();
            else {using(var transfer=new Transfer())await workspace.CacheCatalog(workspace.Current.files,transfer);RefreshList();}
        });
    }
    void Place(W.UIElement control,int column){C.Grid.SetColumn(control,column);toolbar.Children.Add(control);}
    static void StyleButton(C.Button button){button.Padding=new W.Thickness(12,5,12,5);button.Margin=new W.Thickness(0,0,6,0);button.MinHeight=30;}
    C.Button Button(string title,Func<Task> action){var button=new C.Button{Content=title};StyleButton(button);if(action!=null)button.Click+=async(s,e)=>await Work(action);return button;}
    void MenuItem(C.ContextMenu menu,string title,Func<Task> action){var item=new C.MenuItem{Header=title};item.Click+=async(s,e)=>await Work(action);menu.Items.Add(item);}
    async Task Work(Func<Task> action) {if(busy)return;busy=true;toolbar.IsEnabled=false;try{await action();}catch(Exception e){status.Text="未完成："+e.Message;MessageBox.Show(this,e.Message,"未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{busy=false;toolbar.IsEnabled=true;remove.IsEnabled=list.SelectedItems.Count>0;}}
    void RefreshList() {
        list.UnselectAll();list.Configure(Files);List<AdminRow> rows;
        if(!Files)rows=AdminCatalog.Build(workspace).Select(g=>new AdminRow{Name=g.name,Version=g.version=="不适用"?"":g.version,Purpose=ModNames.Description(g.name),Count=g.paths.Length.ToString(),Size=(g.size/1024.0/1024.0).ToString("0.##")+" MB",Status=g.status=="已发布"?"":g.status,Paths=g.paths,Group=g}).ToList();
        else rows=workspace.Current.files.OrderBy(f=>f.path,StringComparer.OrdinalIgnoreCase).Select(FileRow).Concat(workspace.Current.deletes.OrderBy(p=>p).Select(FileRow)).ToList();
        list.ItemsSource=rows;int pending=workspace.Current.files.Count(f=>String.IsNullOrEmpty(f.url));
        status.Text=(String.IsNullOrEmpty(workspace.Current.baseVersion)?"尚未发布":workspace.Current.baseVersion)+"  ·  "+(Files?"":rows.Count+" 个组件  ·  ")+workspace.Current.files.Count+" 个文件"+(pending>0?"  ·  "+pending+" 待发布":"")+(workspace.Current.deletes.Count>0?"  ·  "+workspace.Current.deletes.Count+" 待删除":"");
    }
    AdminRow FileRow(PackFile f){return new AdminRow{Name=f.path,Paths=new[]{f.path},Size=(f.size/1024.0).ToString("0.#")+" KB",Status=String.IsNullOrEmpty(f.url)?"待发布":""};}
    AdminRow FileRow(string path){var f=workspace.Current.files.FirstOrDefault(item=>String.Equals(item.path,path,StringComparison.OrdinalIgnoreCase));return f==null?new AdminRow{Name=path,Paths=new[]{path},Size="",Status="待删除"}:FileRow(f);}
    string[] SelectedPaths(){return list.SelectedItems.Cast<AdminRow>().SelectMany(row=>row.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
    async Task EditPages(){
        using(var transfer=new Transfer())await workspace.CacheCatalog(workspace.Current.files,transfer);
        var file=workspace.Current.files.FirstOrDefault(f=>f.path==PageContent.RelativePath);
        var pages=file==null?new PageContent{History=HistoryWindow.ReadHistory()}:PageContent.Load(workspace.ObjectPath(file.sha256));
        var groups=AdminCatalog.Build(workspace).Where(g=>g.version!="不适用").ToList();
        var installed=groups.Select(g=>new InstalledMod{name=g.name,version=g.version,status="发布清单预览",kind="MOD"}).ToList();
        foreach(var item in installed.OrderByDescending(g=>ModNames.OwnRank(g.name)))if(pages.Find(item.name)==null)pages.Mods.Add(new PageMod{Key=item.name,Name=ModNames.OwnRank(item.name)==4?"Universal Wrench":item.name=="MoreCounterplay"?"H增强版 MoreCounterplay":item.name,Purpose=ModNames.Description(item.name),Order=pages.Mods.Count,New=null,Own=ModNames.OwnRank(item.name)>0,Details=item.name=="MoreCounterplay"?String.Join("\n",ModNames.Features(item.name).Select(f=>(f.SelfDeveloped?"* ":"")+f.Text)):""});
        using(var editor=new PageEditor(pages,installed,p=>{
            p.Validate();string path=Path.Combine(workspace.Root,"display-pages.json");
            if(File.Exists(path))File.Copy(path,path+".backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Data.Atomic(path,Data.Json(p));workspace.Add(path,PageContent.RelativePath);
        }))editor.ShowDialog(this);
        RefreshList();
    }
    void ShowDetails(){
        if(list.SelectedItems.Count!=1)return;var group=((AdminRow)list.SelectedItem).Group;if(group==null)return;
        using(var dialog=new Form{Text=group.name+" — 文件",Icon=Icon,ClientSize=new Size(850,480),MinimumSize=new Size(620,300),StartPosition=FormStartPosition.CenterParent,Font=Font}){
            dialog.AutoScaleMode=AutoScaleMode.Dpi;dialog.AutoScaleDimensions=new SizeF(96,96);var details=new AdminTable();details.Configure(true);details.ItemsSource=group.paths.Select(FileRow).ToList();dialog.Controls.Add(new ElementHost{Dock=DockStyle.Fill,Child=details});dialog.ShowDialog(this);
        }
    }
    async Task Import(string[] paths) {
        status.Text="正在读取文件……";
        var items=new List<Tuple<string,string>>();
        foreach(string input in paths) {
            SafePath.NoLinks(input);string relative=Workspace.Infer(input);
            if(Directory.Exists(input)&&Directory.Exists(Path.Combine(input,"BepInEx"))) {
                foreach(string file in await Task.Run(()=>Workspace.Files(Path.Combine(input,"BepInEx")).ToArray()))items.Add(Tuple.Create(file,"BepInEx/"+file.Substring(Path.Combine(input,"BepInEx").Length+1).Replace('\\','/')));
                foreach(string n in new[]{"winhttp.dll","doorstop_config.ini",".doorstop_version"})if(File.Exists(Path.Combine(input,n)))items.Add(Tuple.Create(Path.Combine(input,n),n));
            } else {
                if(relative==null) {relative=AskPath("无法自动判断相对路径，请填写一次：", "BepInEx/plugins/"+Path.GetFileName(input));if(relative==null)continue;}
                if(Directory.Exists(input))foreach(string file in await Task.Run(()=>Workspace.Files(input).ToArray()))items.Add(Tuple.Create(file,relative.TrimEnd('/')+"/"+file.Substring(input.TrimEnd('\\').Length+1).Replace('\\','/')));
                else items.Add(Tuple.Create(input,relative));
            }
        }
        foreach(var item in items)SafePath.Validate(item.Item2);
        status.Text="正在读取文件……";
        await Task.Run(()=>{foreach(var item in items)workspace.Add(item.Item1,item.Item2);});RefreshList();
    }
    async Task Reload() {
        status.Text="正在读取云端清单……";
        var account=CosSettings.Load(workspace);if(account==null){status.Text="请在“更多 → 设置”中填写上传凭据。";return;}
        using(var api=new CosApi(account))using(var transfer=new Transfer()){var remote=await api.Remote();if(remote==null){RefreshList();status.Text="COS 尚未发布；已保留现有清单，点击发布即可迁移。";return;}await workspace.CacheCatalog(remote.files,transfer);await Task.Run(()=>workspace.SetRemote(remote));}version.Clear();RefreshList();
    }
    async Task Publish() {
        string requested=Publisher.VersionName(version.Text);workspace.Current.releaseVersion=requested;await Task.Run(()=>workspace.Save());
        if(CosSettings.Load(workspace)==null&&!Setup())return;
        status.Text="正在检查发布权限……";
        var progress=new Progress<string>(text=>status.Text=text);
        var account=CosSettings.Load(workspace);
        using(var api=new CosApi(account))using(var transfer=new Transfer()) {
            var manifest=await Task.Run(()=>CosPublisher.Publish(workspace,account,api,transfer,text=>((IProgress<string>)progress).Report(text)));
            string backupError=null;try{await Task.Run(()=>CosPublisher.Backup(workspace,account,manifest,text=>((IProgress<string>)progress).Report(text)));}catch(Exception e){backupError=e.Message;}
            version.Clear();RefreshList();status.Text="发布完成！版本："+manifest.version+(backupError==null?"":"；GitHub 备份暂未完成。");MessageBox.Show(this,status.Text+(backupError==null?"":"\nCOS 已可正常更新。下次点击发布会重试备份。\n"+backupError),"完成");
        }
    }
    bool Setup() {
        var old=CosSettings.Load(workspace);
        using(var dialog=new Form {Text="云端设置",Icon=Icon,ClientSize=new Size(650,285),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font}) {
            dialog.SuspendLayout();dialog.AutoScaleMode=AutoScaleMode.Dpi;
            var label=new Label {Text="COS：公有读、私有写。密钥只在本机加密保存。\n上传账号只需更新桶 lcpack/ 目录的读取、上传权限。\n不要将密钥发给朋友或粘贴到聊天里。",Location=new Point(15,15),Size=new Size(620,64)};
            var endpoint=new TextBox {Text=old!=null?old.channel.endpoint:Cloud.Enabled?Cloud.Config.endpoint:"https://lethalgoofballs-guangzhou-1464070488.cos.ap-guangzhou.myqcloud.com",Location=new Point(105,90),Width=525};
            var id=new TextBox {Text=old==null?"":old.secretId,UseSystemPasswordChar=true,Location=new Point(105,130),Width=525};
            var key=new TextBox {Text=old==null?"":old.secretKey,UseSystemPasswordChar=true,Location=new Point(105,170),Width=525};
            var save=new Button {Text="保存",Location=new Point(460,224),Size=new Size(80,30),DialogResult=DialogResult.OK};var cancel=new Button {Text="取消",Location=new Point(550,224),Size=new Size(80,30),DialogResult=DialogResult.Cancel};
            dialog.Controls.AddRange(new Control[]{label,new Label{Text="HTTPS 域名",Location=new Point(15,94),AutoSize=true},endpoint,new Label{Text="SecretId",Location=new Point(15,134),AutoSize=true},id,new Label{Text="SecretKey",Location=new Point(15,174),AutoSize=true},key,save,cancel});dialog.AcceptButton=save;dialog.CancelButton=cancel;
            dialog.AutoScaleDimensions=new SizeF(96,96);dialog.ResumeLayout(true);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;CosSettings.Save(workspace,endpoint.Text,id.Text,key.Text);status.Text="COS 凭据已保存，可以点击“发布更新”。";return true;
        }
    }
    string AskPath(string title,string initial) {
        using(var d=new Form {Text=title,Icon=Icon,ClientSize=new Size(600,105),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
            d.SuspendLayout();d.AutoScaleMode=AutoScaleMode.Dpi;
            var text=new TextBox {Text=initial,Location=new Point(15,15),Width=570};var ok=new Button {Text="确定",Location=new Point(410,60),DialogResult=DialogResult.OK};var cancel=new Button {Text="取消",Location=new Point(500,60),DialogResult=DialogResult.Cancel};d.Controls.AddRange(new Control[]{text,ok,cancel});d.AcceptButton=ok;d.CancelButton=cancel;d.AutoScaleDimensions=new SizeF(96,96);d.ResumeLayout(true);return d.ShowDialog(this)==DialogResult.OK?text.Text.Replace('\\','/'):null;
        }
    }
}
}
