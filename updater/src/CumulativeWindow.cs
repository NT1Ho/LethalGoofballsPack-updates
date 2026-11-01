using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace LcPack {
public partial class PublisherWindow {
    readonly Func<HttpMessageHandler> rehearsal;
    CumulativeBundle selectedBundle;
    async Task PickStandard(){using(var d=new OpenFileDialog{Filter="标准累计更新包|*.zip",InitialDirectory=Cumulative.Home(workspace)})if(d.ShowDialog(this)==DialogResult.OK)await LoadStandard(d.FileName);}
    async Task BuildStandard(){
        string path=await Task.Run(()=>Cumulative.Build(draft));
        if(Directory.GetDirectories(Cumulative.Inbox(workspace)).Length==0){status.Text="已生成空累计包；尚无已登记的成果。";return;}
        await LoadStandard(path);
    }
    async Task LoadStandard(string path){
        selectedBundle=null;ReloadView();await EnsureCatalog();
        if(!isolated||rehearsal!=null)using(var t=rehearsal==null?new Transfer():new Transfer(rehearsal())){
            var live=Data.Read<Manifest>(await t.CloudVersion("manifest.json","mods"));workspace.CheckBase(live);
        }
        var bundle=await Task.Run(()=>Cumulative.Read(draft,path));
        string name=workspace.Current.releaseVersion;if(String.IsNullOrEmpty(name))name=workspace.Current.baseVersion=="preview"?"preview2":"preview";
        await Task.Run(()=>Cumulative.Apply(draft,bundle,name,false));selectedBundle=bundle;tabs.SelectedIndex=2;
        var baseline=draft.Base.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
        changes.ShowRows(bundle.results.SelectMany(m=>m.files.Select(f=>new AdminItem(new[]{f.sha256==null?"删除":f.before==null?"新增":"替换",f.path,
            f.before==null?"—":AdminWorkspace.Size(baseline[f.path].size),f.sha256==null?"—":AdminWorkspace.Size(new FileInfo(workspace.ObjectPath(f.sha256)).Length)},f.path))));
        RefreshState();FitTab();
        status.Text="检查通过 · "+String.Join("、",bundle.results.Select(m=>m.kind+" "+m.package));version.Focus();
    }
    async Task ShowPublishProgress(string target){
        using(var monitor=new Form{Text="发布进度 · "+target,ClientSize=new Size(760,220),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimizeBox=false}){
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),RowCount=4,ColumnCount=1};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var labels=new[]{"版本 "+target,"COS 资源上传 · 等待检查","GitHub 资源上传 · 等待检查","正在连接渠道"}.Select(t=>new Label{Text=t,Dock=DockStyle.Fill}).ToArray();
            for(int i=0;i<labels.Length;i++){body.RowStyles.Add(new RowStyle(SizeType.Percent,25));body.Controls.Add(labels[i],0,i);}monitor.Controls.Add(body);
            bool working=true;monitor.FormClosing+=(s,e)=>{if(working)e.Cancel=true;};monitor.Show(this);
            var ui=new Progress<string>(s=>{if(monitor.IsDisposed)return;int idx=s.StartsWith("COS · 文件")?1:s.StartsWith("GitHub · 文件")?2:3;labels[idx].Text=s;status.Text=s;});
            var watch=System.Diagnostics.Stopwatch.StartNew();try{await Task.Run(()=>AdminRelease.Run(draft,s=>{lock(watch){if(s.StartsWith("安装预检 · 核验对象")&&watch.ElapsedMilliseconds<150)return;watch.Restart();}((IProgress<string>)ui).Report(s);},rehearsal));}finally{working=false;monitor.Close();}
        }
    }
}
}
