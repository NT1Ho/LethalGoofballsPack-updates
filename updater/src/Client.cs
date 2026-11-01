using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LcPack {
static class ClientProgram {
    [STAThread] static void Main(string[] args) {UiStartup.Run(()=>RunClient(args));}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void RunClient(string[] args) {
        string root=AppDomain.CurrentDomain.BaseDirectory;bool runtime=false,ready=false;
        try {
            if(args.Length>=1&&args[0]=="--diagnostics"&&args.Length<=2){if(args.Length==2)root=Path.GetFullPath(args[1]);Engine.CheckGameDirectory(root);Application.Run(new DiagnosticsWindow(root));return;}
            if(args.Length==4&&args[0]=="--run-client"&&(args[3]=="updated"||args[3]=="check")) {
                runtime=true;
                root=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));SelfUpdate.ValidateRuntime(root,Application.ExecutablePath);
                    using(var window=new ClientWindow(args[3]=="updated",root)) {
                        var handle=window.Handle;SelfUpdate.Ready(root,args[2]);ready=true;SelfUpdate.WaitForCache(root);
                        var active=SelfUpdate.State(root).active;
                        if(active==null||active.file.sha256!=Data.Hash(Application.ExecutablePath))return;
                        using(var instance=SelfUpdate.Instance(root)){try{Application.Run(window);}finally{instance.ReleaseMutex();}}
                    }
            } else {
                Engine.CheckGameDirectory(root);
                if(args.Length==2&&args[0]=="--client-updated")SelfUpdate.FinishLegacy(root,args[1]);
                else if(args.Length!=0)throw new IOException("接收端启动参数无效。");
                SelfUpdate.Bootstrap(root,Application.ExecutablePath,(path,arguments)=>Process.Start(new ProcessStartInfo(path,arguments){UseShellExecute=false,WorkingDirectory=root}));
            }
        }catch(Exception e){string log="";try{log="\n详细记录："+SelfUpdate.Record(root,e);}catch{}if(!runtime||ready)MessageBox.Show("接收端启动失败："+e.Message+log,"Lethal company pack Updater");Environment.ExitCode=1;}
    }
}
public class ClientWindow : Form {
    readonly Label message = new Label(); readonly Label versions = new Label(); readonly ProgressBar bar = new ProgressBar(); bool busy = true;
    readonly bool restarted;
    readonly string root;
    readonly Button mods=new Button(),history=new Button(),retry=new Button();bool showMods,showHistory;
    public ClientWindow(bool restarted=false,string root=null) {
        this.restarted=restarted;this.root=root??AppDomain.CurrentDomain.BaseDirectory;
        SuspendLayout();DoubleBuffered=true;AutoScaleMode=AutoScaleMode.Dpi;
        Text = "Pack Updater · "+SelfUpdate.ReleaseLabel(SelfUpdate.Version);
        FormBorderStyle=FormBorderStyle.FixedDialog;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        Icon=Icon.ExtractAssociatedIcon(typeof(ClientWindow).Assembly.Location);
        MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);
        var page=new TableLayoutPanel { ColumnCount=1,RowCount=5,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(14,12,14,12),Margin=Padding.Empty };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,390));
        for(int i=0;i<5;i++)page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(page);
        message.AutoSize=true;message.MaximumSize=new Size(390,0);message.Margin=new Padding(0,0,0,5);
        message.Text="检查更新中……";
        versions.AutoSize=true;versions.MaximumSize=new Size(390,0);versions.Margin=new Padding(0,0,0,7);
        versions.Text="本地版本：正在读取……\n最新版本：正在查询……";
        bar.Dock=DockStyle.Fill;bar.Height=8;bar.Margin=new Padding(0,0,0,10);bar.Style=ProgressBarStyle.Marquee;
        page.Controls.Add(message,0,0);page.Controls.Add(versions,0,1);page.Controls.Add(bar,0,2);
        var nav=new TableLayoutPanel{ColumnCount=3,RowCount=1,Dock=DockStyle.Top,Height=32,Margin=Padding.Empty};
        for(int i=0;i<3;i++)nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        var diagnostics=new Button{Text="故障诊断"};mods.Text="查看 MOD";history.Text="历史更新记录";
        var buttons=new[]{mods,history,diagnostics};
        for(int i=0;i<buttons.Length;i++){
            buttons[i].Dock=DockStyle.Fill;buttons[i].AutoSize=false;buttons[i].Padding=new Padding(2);
            buttons[i].Margin=new Padding(i==0?0:3,0,i==2?0:3,0);
            nav.Controls.Add(buttons[i],i,0);
        }
        page.Controls.Add(nav,0,3);
        mods.Click+=(s,e)=>{showMods=true;if(busy){mods.Text="检查后显示";mods.Enabled=false;}else OpenMods();};
        history.Click+=(s,e)=>{showHistory=true;if(busy){history.Text="检查后显示";history.Enabled=false;}else OpenHistory();};
        diagnostics.Click+=(s,e)=>{using(var window=new DiagnosticsWindow(this.root))window.ShowDialog(this);};
        retry.Text="退出游戏后检查更新";retry.AutoSize=true;retry.Dock=DockStyle.Fill;retry.Margin=new Padding(0,6,0,0);retry.Visible=false;
        retry.Click+=async(s,e)=>{if(!busy)await Run();};page.Controls.Add(retry,0,4);
        Load+=(s,e)=>{page.ColumnStyles[0].Width=396*DeviceDpi/96f;nav.Height=Math.Max(32*DeviceDpi/96,Font.Height+12);};
        Shown+=async(s,e)=>await Run();
        FormClosing += (s,e) => { if (busy) e.Cancel=true; };
        AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
    }
    void OpenMods(){mods.Text="查看 MOD";mods.Enabled=true;using(var window=new ModWindow(root))window.ShowDialog(this);}
    void OpenHistory(){history.Text="历史更新记录";history.Enabled=true;try{var pages=PageContent.ForRoot(root);using(var window=new HistoryWindow(pages.History,pages))window.ShowDialog(this);}catch(Exception e){MessageBox.Show(this,"历史资料无法读取："+e.Message);}}
    async Task Run() {
        if(Engine.GameRunning()){
            busy=false;bar.Visible=false;retry.Visible=true;retry.Enabled=true;
            message.Text="游戏运行中 · 可以浏览 MOD、历史和故障诊断。\n更新、插件安装及配置修改需退出游戏。";
            string state=Path.Combine(root,".lcpack-updater/state.json");try{versions.Text="本地记录："+(File.Exists(state)?Data.Load<LocalState>(state).version:"未确认")+"\n接收端："+SelfUpdate.ReleaseLabel(SelfUpdate.Version)+" · 浏览模式";}catch{versions.Text="接收端："+SelfUpdate.ReleaseLabel(SelfUpdate.Version)+" · 本地版本记录无法读取";}return;
        }
        busy=true;retry.Enabled=false;bar.Visible=true;
        bool success=false;string clientError=null,diagnosticError=null;
        var progress = new Progress<Tuple<string,int>>(p => { message.Text=p.Item1; bar.Style=p.Item2<0?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous; if(p.Item2>=0)bar.Value=Math.Min(100,p.Item2); });
        var versionProgress=new Progress<Tuple<string,string>>(p=>versions.Text="本地记录："+(String.IsNullOrEmpty(p.Item1)?"尚未确认（首次检查）":p.Item1)+"\n最新版本："+(p.Item2??"正在查询……"));
        try {
            using(var transfer=new Transfer()) {
                Engine.CheckGameDirectory(root);Engine.CheckGameStopped();
                try{await SelfUpdate.RefreshLegacyLauncher(root);}catch(Exception e){clientError="旧入口升级未完成："+e.Message;SelfUpdate.Record(root,e);}
                try{DiagnosticsStore.Install(root);DiagnosticsStore.Cleanup(root);}catch(Exception e){diagnosticError=e.Message;SelfUpdate.Record(root,e);}
                if(!restarted) {
                    try {
                        if(await Task.Run(()=>SelfUpdate.Check(root,transfer,(text,value)=>((IProgress<Tuple<string,int>>)progress).Report(Tuple.Create(text,value))))) {busy=false;Close();return;}
                    }catch(Exception e){if(e is InvalidDataException||(!(e is IOException)&&!(e is HttpRequestException)&&!(e is TaskCanceledException)&&!(e is System.ComponentModel.Win32Exception)))throw;clientError=e.Message;SelfUpdate.Record(root,e);}
                }
                var engine=new Engine(root,Engine.CheckGameStopped);
                var result=await Task.Run(()=>engine.Run(transfer,(text,value)=>((IProgress<Tuple<string,int>>)progress).Report(Tuple.Create(text,value)),(local,remote)=>((IProgress<Tuple<string,string>>)versionProgress).Report(Tuple.Create(local,remote))));
                message.Text=result.latest?"已是最新版！":"安装成功！";
                versions.Text="当前版本："+result.version+"\n最新版本："+result.version;
                if(result.skipped>0)message.Text+="\n"+result.skipped+" 个未纳管文件未删除。";
                if(result.backupPending)message.Text+="\n备份仍在 pending 文件夹，下次启动会再次归档。";
                bar.Style=ProgressBarStyle.Continuous;bar.Value=100;success=true;
                if(clientError!=null){message.Text="MOD 更新完成。接收端更新暂未完成，\n下次启动自动重试。";success=false;}
                if(diagnosticError!=null){message.Text+="\n诊断维护未完成，请打开故障诊断检查。";}
            }
        } catch(TaskCanceledException e) { SelfUpdate.Record(root,e);message.Text="更新失败：网络超时，请稍后重试。"; }
          catch(HttpRequestException e) { SelfUpdate.Record(root,e);message.Text="更新失败：无法连接下载源，请检查网络。"; }
          catch(Exception e) { SelfUpdate.Record(root,e);message.Text="更新失败："+e.Message; }
        busy=false;retry.Enabled=true;bar.Style=ProgressBarStyle.Continuous;
        if(showMods)OpenMods();
        if(showHistory)OpenHistory();
        if(!success)bar.Visible=false;
    }
}
}

