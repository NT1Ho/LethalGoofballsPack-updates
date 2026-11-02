using System;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LcPack {
public sealed class AdminReleaseJob {
    public string BaseHash,DraftHash,Stage,Cumulative;
    public Manifest Target;
}
public static class AdminRelease {
    static string DraftId(Draft d){
        using(var hash=SHA256.Create())return Data.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(Data.Json(d))));
    }
    public static async Task Run(AdminWorkspace draft,Action<string> progress,Func<HttpMessageHandler> transport=null){
        var ws=draft.Store;
        var account=CosSettings.Load(ws);
        if(account==null||String.IsNullOrEmpty(ws.Token()))throw new IOException("请先在渠道设置中配置 COS 和 GitHub 凭据。");
        if(Cloud.Config==null||account.channel.publicKey!=Cloud.Config.publicKey||account.channel.endpoint!=Cloud.Config.endpoint)
            throw new IOException("当前签名身份与接收端渠道不符。");
        using(var cos=transport==null?new CosApi(account):new CosApi(account,transport()))
        using(var transfer=transport==null?new Transfer():new Transfer(transport()))
        using(var github=transport==null?new GitHubApi(ws.Token()):new GitHubApi(ws.Token(),transport())){
            await github.ValidateAccount().ConfigureAwait(false);
            string previous=File.Exists(draft.JobFile)?Data.Load<AdminReleaseJob>(draft.JobFile).BaseHash:ws.Current.baseManifestHash;
            string fallback=await github.OptionalModFeed().ConfigureAwait(false);
            if(!String.IsNullOrEmpty(fallback)&&!File.Exists(Path.Combine(ws.Root,"page-publish.pending.json"))){
                string live=Workspace.Fingerprint(Data.Read<Manifest>(Cloud.Verify(fallback,"mods")));
                if(live!=previous&&(!File.Exists(draft.JobFile)||live!=Workspace.Fingerprint(Data.Load<AdminReleaseJob>(draft.JobFile).Target)))
                    throw new IOException("GitHub 备用源与已知基线不符，请先核对渠道。");
            }
            if(!draft.Pending){
                ws.CheckBase(await cos.Remote().ConfigureAwait(false));
                var target=draft.Prepare(progress);
                progress("接收端正在隔离目录演练安装 "+target.version);
                await InstallCheck.Run(draft,target,transfer,progress).ConfigureAwait(false);
                if(target.version!=ws.Current.baseVersion){
                    var existing=await github.FindPublishTarget(target.version).ConfigureAwait(false);
                    if(existing!=null&&!existing.draft)throw new IOException("GitHub 已有该正式版本，请核对版本名。");
                    if(existing!=null){
                        var assets=await github.Assets(existing.id).ConfigureAwait(false);
                        var manifest=assets.FirstOrDefault(a=>a.name=="manifest.json");
                        if(manifest!=null&&manifest.digest!="sha256:"+Workspace.Fingerprint(target))throw new IOException("GitHub 同名草稿的清单与候选不同。");
                    }
                }
            }
            await Execute(draft,cos.Remote,()=>CosPublisher.Publish(ws,account,cos,transfer,progress,transport),
                m=>CosPublisher.Backup(ws,account,m,progress,transport,Data.Load<AdminReleaseJob>(draft.JobFile).BaseHash,ReleaseNotes(draft,m)),async m=>{
                    string payload=Data.Json(m);
                    if(await transfer.CloudVersion("manifest.json","mods").ConfigureAwait(false)!=payload||
                       Cloud.Verify(await github.ReadModFeed().ConfigureAwait(false),"mods")!=payload)
                        throw new IOException("双端公开清单未一致，保留恢复记录。");
                    var job=Data.Load<AdminReleaseJob>(draft.JobFile);
                    var previousFiles=String.IsNullOrEmpty(job.BaseHash)?new Manifest():Data.Load<Manifest>(Path.Combine(ws.Root,"baseline-"+job.BaseHash+".json"));
                    var known=new System.Collections.Generic.HashSet<string>(previousFiles.files.Select(f=>f.sha256));
                    string temp=Path.GetTempFileName();
                    try{foreach(var f in m.files.Where(f=>!known.Contains(f.sha256)).GroupBy(f=>f.sha256).Select(g=>g.First())){
                        progress("双渠道回读安装文件 · "+f.path);await transfer.VerifyCloudFile(f,temp).ConfigureAwait(false);
                        await transfer.VerifyGitHubFile(f,temp).ConfigureAwait(false);
                    }}finally{if(File.Exists(temp))File.Delete(temp);}
                    Cumulative.Archive(ws,job.Cumulative,m.version);
                },progress).ConfigureAwait(false);
        }
    }
    static string ReleaseNotes(AdminWorkspace w,Manifest target){
        var h=w.Pages().History.FirstOrDefault(r=>r.Version==target.version);if(h==null)return null;
        string text=String.Join("\n",h.Changes.Select(e=>"• "+e.Kind+" · "+e.Mod+"　"+e.Summary));
        return text.Length<=60000?text:text.Substring(0,59950)+"\n完整说明请在接收端历史记录中查看。";
    }
    public static async Task Execute(AdminWorkspace draft,Func<Task<Manifest>> read,
        Func<Task<Manifest>> publish,Func<Manifest,Task> backup,Func<Manifest,Task> verify,Action<string> progress){
        var ws=draft.Store;
        string file=draft.JobFile;
        string legacy=Path.Combine(ws.Root,"page-publish.pending.json");
        AdminReleaseJob job;
        var remote=await read().ConfigureAwait(false);
        if(File.Exists(file))job=Data.Load<AdminReleaseJob>(file);
        else {
            Manifest target;
            if(File.Exists(legacy))target=Data.Load<PagePublishJob>(legacy).After;
            else {
                ws.CheckBase(remote);
                if((remote==null?"":remote.version)!=ws.Current.baseVersion)throw new IOException("工作区基线不是当前正式版本，请先核对渠道。");
                target=draft.Prepare(progress);
            }
            job=new AdminReleaseJob{Target=target,BaseHash=ws.Current.baseManifestHash,DraftHash=DraftId(ws.Current),Cumulative=ws.Current.cumulative,Stage="等待 COS 提交"};
            Data.Atomic(file,Data.Json(job));
        }
        SafePath.ValidateManifest(job.Target);
        string targetHash=Workspace.Fingerprint(job.Target);
        bool committed=remote!=null&&Workspace.Fingerprint(remote)==targetHash;
        bool legacyHash=false;
        if(ws.Current.cumulative==null){var d=ws.Current;using(var sha=SHA256.Create())
            legacyHash=job.DraftHash==Data.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Data.Json(new{d.baseVersion,d.baseManifestHash,d.releaseVersion,d.files,d.deletes}))));}
        if(!legacyHash&&job.DraftHash!=DraftId(ws.Current)&&!(ws.Current.baseManifestHash==targetHash&&Data.Json(ws.Current)==Data.Json(new Draft{
            baseVersion=job.Target.version,baseManifestHash=targetHash,files=job.Target.files,deletes=job.Target.deletes})))
            throw new IOException("恢复记录与当前草稿不匹配，已停止发布。");
        if(!committed||File.Exists(legacy)){
            if(!File.Exists(legacy)&&(remote==null?"":Workspace.Fingerprint(remote))!=job.BaseHash)throw new IOException("云端内容已变化，恢复操作已停止。");
            var result=await publish().ConfigureAwait(false);
            if(Workspace.Fingerprint(result)!=targetHash)throw new IOException("提交结果与已确认候选不符。");
        }
        // A crash after the COS pointer moved is recoverable without writing it a second time.
        if(Workspace.Fingerprint(await read().ConfigureAwait(false))!=targetHash)throw new IOException("COS 正式清单与目标不符，停止 GitHub 操作。");
        ws.SetRemote(job.Target);draft.RefreshBase();
        progress("COS 已提交并验签，正在检查 GitHub 附件");job.Stage="COS 已提交，等待 GitHub 备份";Data.Atomic(file,Data.Json(job));
        await backup(job.Target).ConfigureAwait(false);
        progress("GitHub 已提交 · 正在回读两个正式清单及安装资源");job.Stage="双端结果待核对";Data.Atomic(file,Data.Json(job));
        await verify(job.Target).ConfigureAwait(false);
        File.Move(file,Path.Combine(ws.Root,"admin-release.done-"+Guid.NewGuid().ToString("N")+".json"));
    }
}
}
