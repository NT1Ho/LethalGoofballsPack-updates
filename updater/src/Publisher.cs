using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly:System.Reflection.AssemblyProduct("Pack Updater 管理端")]
namespace LcPack {
static class PublisherProgram {
    [STAThread] static void Main(string[] args){
        UiStartup.Run(()=>{
            try{
                bool isolated=args.Length==2&&args[0]=="--workspace";
                if(!isolated&&args.Length!=0)throw new IOException("不支持的启动参数。");
                string root=isolated?Path.GetFullPath(args[1]):Workspace.DefaultRoot;
                SafePath.NoLinks(root);Directory.CreateDirectory(root);
                using(var lease=new FileStream(Path.Combine(root,"admin-session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                using(var form=new PublisherWindow(new Workspace(root),isolated))Application.Run(form);
            }catch(Exception e){MessageBox.Show("管理端无法打开。请确认工作区可用，且没有另一个管理端正在使用。\n"+e.Message,"Pack Updater 管理端");}
        });
    }
}
public partial class PublisherWindow:Form {
    readonly Workspace workspace;
    readonly AdminWorkspace draft;
    readonly bool isolated;
    readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};
    readonly AdminGrid catalog=new AdminGrid(),changes=new AdminGrid(),history=new AdminGrid();
    readonly ComboBox view=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
    readonly TextBox search=new TextBox{Width=165},version=new TextBox{Width=118};
    readonly Label header=new Label{AutoSize=true},status=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
    readonly Label delta=AdminControls.Label(""),connections=AdminControls.Label(""),remoteInfo=AdminControls.Label("");
    readonly Button publish,replace,config,remove,details,undo,cancelJob;
    int lastHeight;
    Manifest observed;
    List<ModGroup> groups;
    List<DraftChange> diff;
    PageContent pages;
    string channelText="尚未核对在线渠道",pagesError="";
    bool busy;
    public PublisherWindow():this(new Workspace(Workspace.DefaultRoot),false){}
    public PublisherWindow(Workspace ws):this(ws,true){}
    public PublisherWindow(Workspace ws,bool isolated):this(ws,isolated,null){}
    public PublisherWindow(Workspace ws,bool isolated,Func<System.Net.Http.HttpMessageHandler> rehearsal){
        if(rehearsal!=null&&(!isolated||ws.Root==Workspace.DefaultRoot))throw new ArgumentException("演练仅限隔离目录");this.rehearsal=rehearsal;
        workspace=ws;this.isolated=isolated;draft=new AdminWorkspace(ws);
        Text="Pack Updater 管理端 · 2.2.2";Font=new Font("Microsoft YaHei UI",9);
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);StartPosition=FormStartPosition.CenterScreen;
        ClientSize=new Size(946,545);MinimumSize=new Size(760,420);
        Icon=Icon.ExtractAssociatedIcon(typeof(PublisherWindow).Assembly.Location);
        var frame=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=3};
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));frame.RowStyles.Add(new RowStyle(SizeType.Absolute,29));
        header.Margin=new Padding(4,2,4,9);frame.Controls.Add(header,0,0);frame.Controls.Add(tabs,0,1);frame.Controls.Add(status,0,2);Controls.Add(frame);

        var contentBar=AdminControls.Bar();
        view.Items.AddRange(new object[]{"按 MOD","按文件"});view.SelectedIndex=0;
        contentBar.Controls.Add(view);contentBar.Controls.Add(AdminControls.Label("搜索"));contentBar.Controls.Add(search);
        var import=DropMenu(contentBar,"添加 ▾");
        Entry(import,"导入组件 ZIP…",PickPackage);Entry(import,"添加文件…",()=>PickFiles());Entry(import,"添加文件夹…",()=>PickFolder());
        replace=Act(contentBar,"替换文件…",ReplaceFile);config=Act(contentBar,"编辑配置…",EditConfig);remove=Act(contentBar,"移除所选",Remove);
        details=Act(contentBar,"查看文件",ShowFiles);catalog.SelectionChanged+=(s,e)=>SelectionState();
        Tab("整合包内容",AdminControls.Page(contentBar,catalog));
        view.SelectedIndexChanged+=(s,e)=>{SetHeaders();Filter();};search.TextChanged+=(s,e)=>Filter();
        catalog.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Start(ShowFiles);};
        AllowDrop=true;DragEnter+=(s,e)=>e.Effect=!busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;
        DragDrop+=(s,e)=>{if(!busy&&e.Data.GetDataPresent(DataFormats.FileDrop))Start(()=>Import((string[])e.Data.GetData(DataFormats.FileDrop)));};

        var pageBar=AdminControls.Bar();
        Act(pageBar,"编辑 MOD 资料",()=>EditPages(0,false));Act(pageBar,"编辑版本历史",()=>EditPages(1,false));
        Act(pageBar,"从变更生成说明",()=>EditPages(1,true));
        Act(pageBar,"预览玩家页面",Preview);
        history.Headers(new[]{"版本","更新日期","类型","组件","更新说明"},new[]{78,105,60,155,370});
        Tab("接收端资料",AdminControls.Page(pageBar,history));

        var releaseBar=AdminControls.Bar();releaseBar.Controls.Add(AdminControls.Label("候选版本"));releaseBar.Controls.Add(version);
        Act(releaseBar,"导入标准 ZIP…",PickStandard);Act(releaseBar,"整理累计区",BuildStandard);Act(releaseBar,"保存候选并检查",CheckCandidate);
        undo=Act(releaseBar,"撤销所选变更",Restore);changes.SelectionChanged+=(s,e)=>SelectionState();
        var more=DropMenu(releaseBar,"更多 ▾");Entry(more,"导出候选快照…",ExportCandidate);Entry(more,"打开累计区",()=>Local(()=>{Cumulative.Inbox(workspace);System.Diagnostics.Process.Start("explorer.exe","\""+Cumulative.Home(workspace)+"\"");}));Entry(more,"导出完整安装 ZIP…",ExportInstall);Entry(more,"保存草稿副本",BackupDraft);Entry(more,"恢复草稿副本…",RestoreBackup);
        changes.Headers(new[]{"操作","目标路径","原大小","候选大小"},new[]{65,480,90,90});
        var releaseFoot=AdminControls.Bar();publish=Act(releaseFoot,"正式发布…",Publish);Act(releaseFoot,"核对在线版本",CheckChannels);
        cancelJob=Act(releaseFoot,"解除未提交的发布任务…",CancelUncommitted);
        Tab("发布中心",AdminControls.Page(releaseBar,delta,changes,releaseFoot));

        var channelBar=AdminControls.Bar();Act(channelBar,"核对在线版本",CheckChannels);Act(channelBar,"采用正式清单…",Adopt);
        Act(channelBar,"配置发布凭据…",Setup).Enabled=!isolated;
        var channelPage=AdminControls.Page(channelBar,remoteInfo,connections,
            AdminControls.Label("整合包由管理端发布。接收端程序使用独立的签名通道。"),
            AdminControls.Label("诊断插件随接收端安装，玩家的截图、备注和日志保存在玩家电脑上。"));
        Tab("渠道设置",channelPage);
        SetHeaders();ReloadView();
        tabs.SelectedIndexChanged+=(s,e)=>FitTab();
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;return;}if(version.Text!=(workspace.Current.releaseVersion??"")&&!Confirm("候选版本名尚未保存，放弃这次输入？"))e.Cancel=true;};
        Shown+=(s,e)=>{if(!isolated&&draft.Base==null)Start(CheckChannels);else if(!isolated&&workspace.Current.files.Count>0&&pagesError.Length>0)Start(async()=>{await EnsureCatalog();ReloadView();});};
    }
    void FitTab(){
        if(WindowState!=FormWindowState.Normal)return;
        int rowCount=selectedBundle==null?(diff==null?0:diff.Count):selectedBundle.results.Sum(m=>m.files.Count);
        bool compact=tabs.SelectedIndex==3||tabs.SelectedIndex==2&&rowCount<6;
        if(compact){
            if(lastHeight==0)lastHeight=ClientSize.Height;
            MinimumSize=new Size(MinimumSize.Width,290*DeviceDpi/96);
            int height=tabs.SelectedIndex==3?338:284+rowCount*27;
            ClientSize=new Size(ClientSize.Width,height*DeviceDpi/96);
        }else if(lastHeight>0){
            MinimumSize=new Size(MinimumSize.Width,420*DeviceDpi/96);ClientSize=new Size(ClientSize.Width,lastHeight);lastHeight=0;
        }
    }
    void Tab(string name,Control control){var tab=new TabPage(name);tab.Controls.Add(control);tabs.TabPages.Add(tab);}
    Button Act(Control parent,string label,Func<Task> action){
        var b=AdminControls.Button(label,()=>Start(action));parent.Controls.Add(b);return b;
    }
    ContextMenuStrip DropMenu(Control parent,string label){
        var menu=new ContextMenuStrip();var button=AdminControls.Button(label,()=>{});parent.Controls.Add(button);
        button.Click+=(s,e)=>menu.Show(button,0,button.Height);button.Disposed+=(s,e)=>menu.Dispose();return menu;
    }
    void Entry(ContextMenuStrip menu,string label,Func<Task> action){menu.Items.Add(label).Click+=(s,e)=>Start(action);}
    static Task Local(Action action){action();return Task.FromResult(0);}
    async void Start(Func<Task> action){
        if(busy)return;busy=true;tabs.Enabled=false;UseWaitCursor=true;
        try{await action();}catch(Exception ex){status.Text="未完成 · "+ex.Message;MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{busy=false;UseWaitCursor=false;tabs.Enabled=true;RefreshState();}
    }
    bool Confirm(string text){return MessageBox.Show(this,text,"请核对操作",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.OK;}
    Action<string> Progress(){
        var p=new Progress<string>(s=>status.Text=s);
        // File imports may contain thousands of entries; coalesce status messages.
        var watch=System.Diagnostics.Stopwatch.StartNew();object gate=new object();
        return text=>{lock(gate){if(watch.ElapsedMilliseconds<100)return;watch.Restart();((IProgress<string>)p).Report(text);}};
    }
    void SelectionState(){
        var selected=catalog.SelectedPaths();
        bool canEdit=!draft.Pending&&draft.Base!=null;
        if(details!=null)details.Enabled=selected.Length>0;
        if(remove!=null)remove.Enabled=canEdit&&selected.Length>0;
        if(replace!=null)replace.Enabled=canEdit&&selected.Length==1;
        if(config!=null)config.Enabled=canEdit&&selected.Length==1&&selected[0].StartsWith("BepInEx/config/",StringComparison.OrdinalIgnoreCase)&&selected[0].EndsWith(".cfg",StringComparison.OrdinalIgnoreCase);
        if(undo!=null)undo.Enabled=canEdit&&selectedBundle==null&&changes.SelectedRows.Count>0;
    }
    void SetHeaders(){
        if(view.SelectedIndex==0)catalog.Headers(new[]{"MOD / 组件","版本","用途","标识","文件","候选状态"},new[]{167,106,220,78,53,81});
        else catalog.Headers(new[]{"相对路径","类型","大小","候选状态"},new[]{450,75,75,90});
    }
    void ReloadView(){
        draft.RefreshBase();groups=AdminInventory.Groups(workspace);diff=draft.Changes();pagesError="";
        try{pages=draft.Pages();}catch(Exception e){pages=new PageContent();pagesError=e.Message;}
        Filter();changes.ShowRows(diff.Select(c=>new AdminItem(new[]{c.Kind,c.Path,c.OldSize,c.NewSize},c.Path)));
        history.ShowRows(pages.History.SelectMany(h=>h.Changes.Select(c=>new AdminItem(new[]{h.Version,FormatDate(h.PublishedUtc),c.Kind,c.Mod,c.Summary}))));
        version.Text=workspace.Current.releaseVersion??"";RefreshState();FitTab();
        status.Text=pagesError!=""?pagesError:diff.Count==0?"请选择组件查看文件，或添加要更新的内容。":"候选已保存在本机，可到发布中心检查。";
    }
    static string FormatDate(string date){DateTimeOffset d;return DateTimeOffset.TryParse(date,out d)?d.ToLocalTime().ToString("yyyy/MM/dd"):date;}
    void RefreshState(){
        string baseName=String.IsNullOrEmpty(workspace.Current.baseVersion)?"尚未载入":workspace.Current.baseVersion;
        header.Text=(isolated?"本地工作区（禁止上线）":"整合包工作区")+"     正式基线 "+baseName+"     "+(String.IsNullOrEmpty(workspace.Current.releaseVersion)?"尚未填写新版本号":"候选版本 "+workspace.Current.releaseVersion)+"     未发布变更 "+diff.Count+" 项";
        delta.Text=draft.Pending?"有待恢复发布，请核对渠道后继续。":draft.Base==null?"旧草稿缺少基线，请核对在线版本。":
            "新增 "+diff.Count(c=>c.Kind=="新增")+"     替换 "+diff.Count(c=>c.Kind=="替换")+"     删除 "+diff.Count(c=>c.Kind=="删除");
        if(File.Exists(draft.JobFile)){
            try{var job=Data.Load<AdminReleaseJob>(draft.JobFile);delta.Text=job.Target.version+" · "+job.Stage;}
            catch(Exception){delta.Text="本地恢复记录无法读取，请保留工作区并核查。";}
        }
        publish.Text=draft.Pending?"继续未完成发布…":"确认正式发布…";publish.Enabled=(!isolated||rehearsal!=null)&&(draft.Pending||selectedBundle!=null||diff.Count!=0);
        if(selectedBundle!=null)delta.Text="累计包已核验 · "+selectedBundle.results.Count+" 个组件 · "+selectedBundle.results.Sum(m=>m.files.Count)+" 个目标路径。填写新版本号后可生成历史或确认发布。";
        cancelJob.Visible=File.Exists(draft.JobFile)&&!File.Exists(Path.Combine(workspace.Root,"page-publish.pending.json"));
        SelectionState();remoteInfo.Text=channelText;
        connections.Text="COS 凭据 "+(File.Exists(Path.Combine(workspace.Root,"cos-credential.dat"))?"已保存":"未配置")+"     GitHub 凭据 "+(File.Exists(Path.Combine(workspace.Root,"credential.dat"))?"已保存":"未配置");
    }
    void Filter(){
        if(groups==null)return;
        var kinds=diff.ToDictionary(c=>c.Path,c=>c.Kind,StringComparer.OrdinalIgnoreCase);
        Func<string,string> kind=p=>kinds.ContainsKey(p)?kinds[p]:"";
        IEnumerable<AdminItem> rows;
        if(view.SelectedIndex==0){
            rows=pages.DisplayOrder(groups,g=>g.name,g=>pages.Find(g.name),g=>g.paths.Any(kinds.ContainsKey)).Select(g=>{
                var meta=pages.Find(g.name);
                var states=g.paths.Select(kind).Where(k=>k!="").Distinct();
                return new AdminItem(new[]{meta==null?g.name:meta.Name,g.version=="不适用"?"":g.version,meta==null?ModNames.Description(g.name):meta.Purpose,
                    (meta==null?ModNames.OwnRank(g.name)>0:meta.Own)?"自研/改版":"",g.paths.Length.ToString(),String.Join("、",states)},g.paths);
            });
        }else{
            var files=workspace.Current.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
            rows=files.Keys.Concat(diff.Where(c=>c.Kind=="删除").Select(c=>c.Path)).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Select(p=>new AdminItem(new[]{p,p.StartsWith("BepInEx/config/")?"配置":"文件",files.ContainsKey(p)?AdminWorkspace.Size(files[p].size):"—",kind(p)},p));
        }
        string query=search.Text.Trim();catalog.ShowRows(rows.Where(r=>query.Length==0||r.Cells.Concat(r.Paths).Any(v=>(v??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)));
    }
    async Task EnsureCatalog(){using(var transfer=new Transfer())await workspace.CacheCatalog(workspace.Current.files,transfer);}
    void SaveVersion(){if(version.Text!=(workspace.Current.releaseVersion??""))draft.SetVersion(version.Text);RefreshState();status.Text="候选版本名已保存。";}
    async Task PickFiles(){using(var d=new OpenFileDialog{Multiselect=true,Title="添加整合包文件"})if(d.ShowDialog(this)==DialogResult.OK)await Import(d.FileNames);}
    async Task PickFolder(){using(var d=new FolderBrowserDialog{Description="选择组件目录或含 BepInEx 的整合包根目录"})if(d.ShowDialog(this)==DialogResult.OK)await Import(new[]{d.SelectedPath});}
    async Task PickPackage(){
        using(var picker=new OpenFileDialog{Filter="组件包|*.zip"})if(picker.ShowDialog(this)==DialogResult.OK)await ImportPackage(picker.FileName);
    }
    async Task ImportPackage(string path){
        string filename=Path.GetFileNameWithoutExtension(path);
        string author=AdminControls.Prompt(this,"组件归属","填写发布者标识（将与包名共同确定归属）",filename.Contains("-")?filename.Split('-')[0]:"");
        if(author==null)return;
        await EnsureCatalog();var progress=Progress();
        using(var plan=await Task.Run(()=>new AdminPackage(workspace,path,author.Trim(),progress)))
        using(var f=Dialog("核对组件 · "+plan.Key+" "+plan.Version,800,420)){
            var grid=new AdminGrid();grid.Headers(new[]{"操作","候选安装位置"},new[]{85,590});
            var known=new HashSet<string>(workspace.Current.files.Select(x=>x.path),StringComparer.OrdinalIgnoreCase);
            grid.ShowRows(plan.Files.Select(x=>new AdminItem(new[]{plan.Modified.Contains(x.Item2)?"覆盖修改版":known.Contains(x.Item2)?"更新":"新增",x.Item2},x.Item2))
                .Concat(plan.Removed.Select(x=>new AdminItem(new[]{"删除旧文件",x},x))));
            var bar=AdminControls.Bar();bar.Controls.Add(AdminControls.Button("加入本地候选",()=>f.DialogResult=DialogResult.OK));bar.Controls.Add(AdminControls.Button("取消",()=>f.DialogResult=DialogResult.Cancel));
            f.Controls.Add(AdminControls.Page(AdminControls.Label("请核对安装位置；已登记文件沿用原路径，公共资料随本次候选一起保存。"),grid,bar));
            if(f.ShowDialog(this)!=DialogResult.OK)return;
            await Task.Run(()=>draft.Import(plan.Files,progress,plan.Removed));ReloadView();
        }
    }
    async Task Import(string[] paths){
        if(paths.Length==1&&File.Exists(paths[0])&&paths[0].EndsWith(".zip",StringComparison.OrdinalIgnoreCase)&&(tabs.SelectedIndex==2||Cumulative.Standard(paths[0]))){await LoadStandard(paths[0]);return;}
        if(paths.Any(p=>p.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))){
            if(paths.Length!=1)throw new IOException("请一次导入一个组件 ZIP，以便核对归属与目标位置。");
            await ImportPackage(paths[0]);return;
        }
        var items=new List<Tuple<string,string>>();
        foreach(string input in paths){
            SafePath.NoLinks(input);string relative=Workspace.Infer(input);
            if(Directory.Exists(Path.Combine(input,"BepInEx"))){
                string dir=Path.Combine(input,"BepInEx");
                foreach(string file in await Task.Run(()=>Workspace.Files(dir).ToArray()))items.Add(Tuple.Create(file,"BepInEx/"+file.Substring(dir.Length+1).Replace('\\','/')));
                foreach(string n in new[]{"winhttp.dll","doorstop_config.ini",".doorstop_version"})if(File.Exists(Path.Combine(input,n)))items.Add(Tuple.Create(Path.Combine(input,n),n));
            }else{
                if(relative==null)relative=AdminControls.Prompt(this,"确定安装位置",Path.GetFileName(input)+" 在游戏目录中的目标路径","BepInEx/plugins/"+Path.GetFileName(input));
                if(relative==null)return;
                relative=relative.Trim().Replace('\\','/').TrimEnd('/');
                if(Directory.Exists(input))foreach(string file in await Task.Run(()=>Workspace.Files(input).ToArray()))items.Add(Tuple.Create(file,relative+"/"+file.Substring(input.TrimEnd('\\').Length+1).Replace('\\','/')));
                else items.Add(Tuple.Create(input,relative));
            }
        }
        var progress=Progress();await Task.Run(()=>draft.Import(items,progress));ReloadView();
    }
    string[] Selection(){var paths=catalog.SelectedPaths();if(paths.Length==0)throw new IOException("请先选择组件或文件。");return paths;}
    string One(){var paths=Selection();if(paths.Length!=1)throw new IOException("请切换到“按文件”，选择一个目标文件。");return paths[0];}
    async Task ReplaceFile(){
        string path=One();using(var f=new OpenFileDialog{Title="选择用于替换 "+Path.GetFileName(path)+" 的文件"})if(f.ShowDialog(this)==DialogResult.OK){await Task.Run(()=>draft.Import(new[]{Tuple.Create(f.FileName,path)},s=>{}));ReloadView();}
    }
    async Task Remove(){
        var paths=Selection();if(!Confirm("从候选移除所选的 "+paths.Length+" 个文件？完整移除组件时也会更新归属登记。"))return;
        await EnsureCatalog();await Task.Run(()=>draft.Remove(paths));ReloadView();
    }
    Task Restore(){return Local(()=>{var paths=changes.SelectedPaths();if(paths.Length==0)throw new IOException("请选择要撤销的变更。");draft.Restore(paths);ReloadView();});}
    Task ShowFiles(){return Local(()=>{
        var selected=Selection();var index=workspace.Current.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
        using(var f=Dialog("组件文件",810,360)){
            var grid=new AdminGrid();grid.Headers(new[]{"相对路径","大小","SHA-256"},new[]{320,65,270});
            grid.ShowRows(selected.Select(p=>new AdminItem(new[]{p,index.ContainsKey(p)?AdminWorkspace.Size(index[p].size):"待删除",index.ContainsKey(p)?index[p].sha256:""},p)));
            var bar=AdminControls.Bar();bar.Controls.Add(AdminControls.Button("在内容中定位",()=>{var p=grid.SelectedPaths();if(p.Length==1){view.SelectedIndex=1;search.Text=p[0];f.Close();}}));
            f.Controls.Add(AdminControls.Page(bar,grid));f.ShowDialog(this);
        }});}
    Form Dialog(string title,int width,int height){return new Form{Text=title,Font=Font,Icon=Icon,ClientSize=new Size(width,height),StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi,MinimizeBox=false};}
    async Task EditConfig(){
        string path=One();if(!path.StartsWith("BepInEx/config/",StringComparison.OrdinalIgnoreCase)||!path.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase))throw new IOException("请在文件视图中选择一个 .cfg 配置文件。");
        var file=workspace.Current.files.FirstOrDefault(f=>f.path==path);if(file==null)throw new IOException("该文件已标为删除。");
        var config=new AdminConfig(await draft.CachedFile(file));
        using(var dialog=Dialog("配置 · "+Path.GetFileName(path),720,440)){
            var grid=new DataGridView{Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White};
            foreach(var pair in new[]{Tuple.Create("分组","Section"),Tuple.Create("设置","Name"),Tuple.Create("候选值","Value")})grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=pair.Item1,DataPropertyName=pair.Item2,ReadOnly=pair.Item2!="Value"});
            grid.DataSource=config.Settings;var bar=AdminControls.Bar();bar.Controls.Add(AdminControls.Button("保存到候选",()=>{grid.EndEdit();dialog.BindingContext[grid.DataSource].EndCurrentEdit();dialog.DialogResult=DialogResult.OK;}));
            dialog.Controls.Add(AdminControls.Page(bar,grid));if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        }
        string tmp=Path.Combine(workspace.Root,"cfg-"+Guid.NewGuid().ToString("N")+".tmp");
        try{config.Write(tmp);await Task.Run(()=>draft.Import(new[]{Tuple.Create(tmp,path)},s=>{}));}finally{if(File.Exists(tmp))File.Delete(tmp);}ReloadView();
    }
    List<InstalledMod> PreviewMods(){var changed=new HashSet<string>(diff.Where(c=>c.After!=null).Select(c=>c.Path),StringComparer.OrdinalIgnoreCase);return groups.Where(g=>g.version!="不适用"&&g.status!="待删除").Select(g=>new InstalledMod{name=g.name,version=g.version,status="候选文件",kind="MOD",isNew=g.paths.Any(changed.Contains)}).ToList();}
    async Task Preview(){await EnsureCatalog();var data=draft.Pages();using(var win=new ModWindow("",data,PreviewMods()))win.ShowDialog(this);}
    async Task EditPages(int section,bool generate){
        if(draft.Pending)throw new IOException("请先完成发布恢复。");
        await EnsureCatalog();var data=draft.Pages();var mods=PreviewMods();
        foreach(var item in mods.OrderByDescending(m=>ModNames.OwnRank(m.name)))if(data.Find(item.name)==null)
            data.Mods.Add(new PageMod{Key=item.name,Name=ModNames.OwnRank(item.name)==4?"Universal Wrench":item.name=="MoreCounterplay"?"H增强版 MoreCounterplay":item.name,
                Purpose=ModNames.Description(item.name),Order=data.Mods.Count,Own=ModNames.OwnRank(item.name)>0,Details=item.name=="MoreCounterplay"?String.Join("\n",ModNames.Features(item.name).Select(f=>(f.SelfDeveloped?"* ":"")+f.Text)):""});
        if(generate){
            var suggestion=draft.SuggestHistory(workspace.Current.releaseVersion??"",groups);
            if(data.History.Any(h=>h.Version==suggestion.Version))throw new IOException("该版本已有更新记录，请直接编辑，避免覆盖已有说明。");
            data.History.Insert(0,suggestion);
        }
        using(var editor=new PageEditor(data,mods,draft.SavePages)){editor.SelectSection(section,workspace.Current.releaseVersion);editor.ShowDialog(this);}ReloadView();
    }
    async Task<Manifest> CheckCandidate(){if(selectedBundle!=null){var bundle=selectedBundle;string tag=version.Text;await Task.Run(()=>Cumulative.Apply(draft,bundle,tag,true));selectedBundle=null;ReloadView();}else SaveVersion();await EnsureCatalog();var progress=Progress();var target=await Task.Run(()=>draft.Prepare(progress));status.Text="本地预检通过 · "+target.version+" · "+diff.Count+" 项变更。上线时还会核对在线基线与凭据。";return target;}
    async Task ExportInstall(){
        using(var d=new SaveFileDialog{Filter="安装 ZIP|*.zip",FileName="整合包安装文件.zip"})if(d.ShowDialog(this)==DialogResult.OK){
            var progress=Progress();await Task.Run(()=>draft.ExportInstall(d.FileName,progress));status.Text="完整安装包已导出："+Path.GetFileName(d.FileName);
        }
    }
    async Task ExportCandidate(){
        SaveVersion();await EnsureCatalog();using(var d=new SaveFileDialog{Filter="候选材料|*.zip",FileName="整合包候选.zip"})if(d.ShowDialog(this)==DialogResult.OK){
            var progress=Progress();await Task.Run(()=>draft.ExportCandidate(d.FileName,progress));status.Text="候选差异包已导出："+Path.GetFileName(d.FileName);
        }
    }
    async Task CheckChannels(){
        using(var transfer=new Transfer()){
            var payload=await transfer.CloudVersion("manifest.json","mods");
            var official=Data.Read<Manifest>(payload);SafePath.ValidateManifest(official);
            observed=official;draft.ObserveSignedPayload(payload);
            if(draft.Base==null&&!String.IsNullOrEmpty(workspace.Current.baseVersion))
                draft.ObserveSignedPayload(await transfer.CloudVersion("versions/"+Publisher.VersionName(workspace.Current.baseVersion)+".json","mods"));
            var client=Data.Read<ClientRelease>(await transfer.CloudVersion("client-update.json","client"));SelfUpdate.Validate(client);
            channelText="COS 正式整合包 "+official.version+"（"+official.files.Count+" 个文件）     接收端 "+SelfUpdate.ReleaseLabel(client.version);
            using(var github=new GitHubApi(null)){
                try{var other=Data.Read<Manifest>(Cloud.Verify(await github.ReadModFeed(),"mods"));channelText+="\nGitHub 备用清单 "+(Workspace.Fingerprint(other)==Workspace.Fingerprint(official)?"与 COS 一致":"与 COS 有差异");}
                catch(Exception e){channelText+="\nGitHub 核对未完成："+e.Message;}
            }
            await EnsureCatalog();ReloadView();status.Text=Workspace.Fingerprint(official)==workspace.Current.baseManifestHash?"当前工作区基线与正式清单一致。":"已读取在线版本；保留本地候选。";
        }
    }
    async Task Adopt(){
        await CheckChannels();if(!Confirm("将已验签的 "+observed.version+" 作为工作区基线？当前草稿会先保存本地副本。"))return;
        using(var t=new Transfer())await workspace.CacheCatalog(observed.files,t);draft.Adopt(observed);ReloadView();
    }
    Task BackupDraft(){return Local(()=>{string file=Path.Combine(workspace.Root,"draft-copy-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".json");Data.Atomic(file,Data.Json(workspace.Current));status.Text="已保存副本："+Path.GetFileName(file);});}
    Task RestoreBackup(){return Local(()=>{using(var d=new OpenFileDialog{InitialDirectory=workspace.Root,Filter="草稿副本|draft-*.json"})if(d.ShowDialog(this)==DialogResult.OK&&Confirm("恢复所选草稿副本？")){draft.RestoreDraft(d.FileName);ReloadView();}});}
    async Task Publish(){
        if(isolated&&rehearsal==null)throw new IOException("本地演练工作区不能发布。");
        string target;bool recovering=draft.Pending;
        if(!recovering){target=(await CheckCandidate()).version;}
        else target=File.Exists(draft.JobFile)?Data.Load<AdminReleaseJob>(draft.JobFile).Target.version:Data.Load<PagePublishJob>(Path.Combine(workspace.Root,"page-publish.pending.json")).After.version;
        if(!Confirm((recovering?"继续已记录的发布任务 ":"正式发布整合包 ")+target+"？\n将更新全体玩家使用的 COS 清单及 GitHub 备用源。\n"+delta.Text))return;
        try{await ShowPublishProgress(target);}finally{ReloadView();}
        status.Text=isolated?"隔离演练成功："+target:"发布成功："+target+"，朋友现在可以通过接收端更新。";
    }
    async Task CancelUncommitted(){
        if(!File.Exists(draft.JobFile))throw new IOException("没有可解除的发布任务。");
        if(File.Exists(Path.Combine(workspace.Root,"page-publish.pending.json")))throw new IOException("展示资料事务已开始，请继续恢复。");
        var job=Data.Load<AdminReleaseJob>(draft.JobFile);
        using(var transfer=new Transfer()){
            var remote=Data.Read<Manifest>(await transfer.CloudVersion("manifest.json","mods"));
            if(Workspace.Fingerprint(remote)!=job.BaseHash)throw new IOException("正式指针已变化，不能解除，请继续恢复。");
        }
        if(Confirm("解除本地任务并继续编辑草稿？已上传的缓存对象仍会保留。"))File.Move(draft.JobFile,draft.JobFile+".cancelled-"+Guid.NewGuid().ToString("N"));
        ReloadView();
    }
    Task Setup(){return Local(()=>{
        if(isolated)throw new IOException("演练工作区不保存正式凭据。");
        var old=CosSettings.Load(workspace);
        using(var f=Dialog("发布凭据",570,265)){
            var endpoint=new TextBox{Dock=DockStyle.Fill,Text=Cloud.Config.endpoint,ReadOnly=true};
            var id=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true};var key=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true};var token=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true};
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,Padding=new Padding(12)};
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var fields=new[]{endpoint,id,key,token};var labels=new[]{"COS 渠道","COS SecretId","COS SecretKey","GitHub 发布令牌"};
            for(int i=0;i<fields.Length;i++){grid.Controls.Add(AdminControls.Label(labels[i]),0,i);grid.Controls.Add(fields[i],1,i);}
            var hint=AdminControls.Label("留空保留已保存的凭据。密钥仅由当前 Windows 用户解密。");grid.Controls.Add(hint,0,4);grid.SetColumnSpan(hint,2);
            grid.Controls.Add(AdminControls.Button("保存",()=>{f.DialogResult=DialogResult.OK;}),1,5);f.Controls.Add(grid);
            if(f.ShowDialog(this)!=DialogResult.OK)return;
            if(old==null&&(id.Text.Length>0||key.Text.Length>0))throw new IOException("此渠道已有签名身份，请在保存原签名身份的 Windows 用户工作区配置 COS。");
            if(id.Text.Length>0||key.Text.Length>0)CosSettings.Save(workspace,endpoint.Text,id.Text.Length==0&&old!=null?old.secretId:id.Text,key.Text.Length==0&&old!=null?old.secretKey:key.Text);
            if(token.Text.Length>0)workspace.SaveToken(token.Text);
        }RefreshState();status.Text="凭据设置已处理。";
    });}
}
}
