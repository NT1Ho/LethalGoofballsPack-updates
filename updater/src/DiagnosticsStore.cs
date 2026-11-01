using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LcPack {
public class DiagnosticSession {
    public string Path,Id,Status;public long Bytes;public int Marks;public bool Protected;
    public override string ToString(){return Id+" | "+Status+" | "+Marks+" 份原始现场 | "+(Bytes/1048576.0).ToString("F1")+" MB"+(Protected?" | 保留":"");}
}
public static class DiagnosticsStore {
    public const string PluginPath="BepInEx/plugins/NT1Ho-LethalDiagnostics/LethalDiagnostics.dll";
    public static string Home(string root){return System.IO.Path.Combine(root,".lcpack-diagnostics");}
    public static bool Active(string path){
        string p=System.IO.Path.Combine(path,"active.lock");if(!File.Exists(p))return false;
        try{using(var s=new FileStream(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None))return false;}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}
    }
    static bool Plain(string path){return (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
    public static IEnumerable<string> Evidence(string path){return Directory.GetFiles(path).Where(p=>Plain(p)&&Regex.IsMatch(System.IO.Path.GetFileName(p),@"^(session|identity|heartbeat|closed|writer-error|notes|recovered)\.json$|^events-\d{3,}\.jsonl$|^(mark-\d{9}-\d{3}\.json|incident-\d{9}-\d{3}\.jsonl|shot-\d{9}-\d{3}\.png)$"));}
    public static Dictionary<string,object> Read(string file){if(!File.Exists(file))return new Dictionary<string,object>();if(new FileInfo(file).Length>8*1024*1024)throw new IOException("诊断文件超过读取限制。");return Data.Load<Dictionary<string,object>>(file);}
    public static object Value(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?v:null;}
    static string Text(Dictionary<string,object> d,string key){return Convert.ToString(Value(d,key));}
    public static List<DiagnosticSession> Sessions(string root){
        string home=Home(root);if(!Directory.Exists(home))return new List<DiagnosticSession>();
        return Directory.GetDirectories(home).Where(p=>Plain(p)&&File.Exists(System.IO.Path.Combine(p,"session.json"))).Select(p=>{
            bool active=Active(p);int marks=Directory.GetFiles(p,"mark-*.json").Length;var notes=Read(System.IO.Path.Combine(p,"notes.json"));string heartbeat=System.IO.Path.Combine(p,"heartbeat.json");
            return new DiagnosticSession{Path=p,Id=System.IO.Path.GetFileName(p),Status=active?(File.Exists(heartbeat)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(heartbeat)>TimeSpan.FromSeconds(10)?"运行中／写入心跳停顿":"运行中"):File.Exists(System.IO.Path.Combine(p,"closed.json"))?"正常结束":"异常退出／待恢复",Marks=marks,Bytes=Evidence(p).Sum(f=>new FileInfo(f).Length),Protected=marks>0||Convert.ToBoolean(Value(notes,"pinned")??false)||notes.Any(n=>n.Key!="pinned"&&!n.Key.StartsWith("_",StringComparison.Ordinal)&&!String.IsNullOrWhiteSpace(Convert.ToString(n.Value)))};
        }).OrderByDescending(s=>s.Id).ToList();
    }
    public static void SaveNote(string path,string id,string note,string group,bool pinned){
        var review=new DiagnosticReview(path);review.SetNote(id,note);review.SetNote("group",group);review.Notes["pinned"]=pinned||!String.IsNullOrWhiteSpace(note);review.Save();
    }
    public static IEnumerable<Dictionary<string,object>> Events(string file,Action bad){
        using(var s=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(s)){
            string line;while((line=reader.ReadLine())!=null){Dictionary<string,object> item=null;try{if(line.Length>65536)throw new IOException();item=Data.Read<Dictionary<string,object>>(line);}catch{bad();}if(item!=null)yield return item;}
        }
    }
    public static int Recover(string path){
        if(Active(path))throw new IOException("游戏仍在写入这次记录。");int invalid=0;long valid=0;
        foreach(string file in Evidence(path).Where(p=>p.EndsWith(".jsonl")))foreach(var e in Events(file,()=>invalid++))valid++;
        Data.Atomic(System.IO.Path.Combine(path,"recovered.json"),Data.Json(new{utc=DateTime.UtcNow.ToString("O"),validRecords=valid,invalidOrPartialLines=invalid,originalEvidencePreserved=true}));return invalid;
    }
    public static string Report(string path){
        var review=new DiagnosticReview(path);var b=new StringBuilder();var session=Read(System.IO.Path.Combine(path,"session.json"));var notes=review.Notes;
        b.AppendLine("# LethalGoofballsPack 游戏现场诊断").AppendLine().AppendLine("会话："+System.IO.Path.GetFileName(path)).AppendLine("启动 UTC："+Text(session,"utc")).AppendLine("结束状态："+(Active(path)?"运行中（尚未完成）":File.Exists(System.IO.Path.Combine(path,"closed.json"))?"正常结束":"异常退出或记录器未完成关闭；原因尚未确定"));
        b.AppendLine("共同联机编号："+Text(notes,"group")).AppendLine("会话备注："+Text(notes,"sessionNote"));
        b.AppendLine("确认保存 UTC："+Text(notes,"_confirmed"));
        b.AppendLine("运行环境："+Text(session,"cpu")+" / "+Text(session,"gpu")+" / "+Text(session,"os")+" / Unity "+Text(session,"unity"));
        b.AppendLine("最后写入心跳："+Data.Json(Read(System.IO.Path.Combine(path,"heartbeat.json"))));
        if(File.Exists(System.IO.Path.Combine(path,"writer-error.json")))b.AppendLine("记录器写入故障："+Data.Json(Read(System.IO.Path.Combine(path,"writer-error.json"))));
        b.AppendLine().AppendLine("## 已确认的现场");
        foreach(var mark in review.Marks.Where(m=>!review.Deleted(m.Id))){
            string f=System.IO.Path.Combine(path,"mark-"+mark.Id+".json");
            var m=Read(f);string id=Text(m,"id");b.AppendLine("### "+id+" / "+Text(m,"kind")+" / "+Text(m,"utc"));
            b.AppendLine("玩家备注："+Text(notes,id));var state=Value(m,"data") as Dictionary<string,object>;if(state!=null){var nested=Value(state,"state") as Dictionary<string,object>;if(nested!=null)state=nested;b.AppendLine("位置："+Text(state,"planet")+" / "+Text(state,"area")+"；坐标 "+Data.Json(Value(state,"position"))+"；身份 "+Text(state,"role")+"；游戏时间 "+Text(state,"gameTime")+"；实际运行版本字段 "+Text(state,"gameVersion"));b.AppendLine("手持物："+Data.Json(Value(state,"held"))+"；瞄准对象："+Data.Json(Value(state,"aim")));}
            b.AppendLine("判定："+(Text(m,"kind")=="manual"?"玩家主动标记，原因尚未判断。":"自动阈值触发，疑似异常；不能据此确认根因。"));b.AppendLine("完整现场与附近对象："+System.IO.Path.GetFileName(f));
            string shot="shot-"+id+".png";b.AppendLine(File.Exists(System.IO.Path.Combine(path,shot))?"截图：["+shot+"]("+shot+")":"截图：未取得（自动异常默认不截图）");b.AppendLine("前后事件：incident-"+id+".jsonl（最多前60秒、后30秒；窗口提前结束见 window_end）。");
        }
        b.AppendLine().AppendLine("## 实际版本与文件身份");
        string identity=System.IO.Path.Combine(path,"identity.json");if(File.Exists(identity)){
            var d=Read(identity);b.AppendLine("整合包声明版本："+Text(d,"packVersionClaim")).AppendLine("接收端状态清单 SHA256："+Text(d,"updaterStateSha256"));
            var plugins=Value(d,"loadedPlugins") as System.Collections.IEnumerable;
            if(plugins!=null)foreach(Dictionary<string,object> m in plugins)b.AppendLine("- "+Text(m,"name")+" "+Text(m,"version")+" | "+Text(m,"guid")+" | SHA256 "+Text(m,"sha256")+" | MVID "+Text(m,"mvid"));
            b.AppendLine("文件差异（不据此直接判定故障原因）：");var files=Value(d,"files") as System.Collections.IEnumerable;
            if(files!=null)foreach(Dictionary<string,object> f in files)if(Text(f,"status")!="match")b.AppendLine("- "+Text(f,"status")+" "+Text(f,"path"));
            b.AppendLine("磁盘重复插件GUID候选："+Data.Json(Value(d,"duplicateDiskGuids")));b.AppendLine(Text(d,"limits"));
        }else b.AppendLine("未取得完整运行时身份；可能在启动采集完成前退出。不可拿当前磁盘版本冒充当时加载版本。");
        int bad=0;long count=0,suppressed=0,dropped=0;double maxMs=0;var errors=new Dictionary<string,int>();
        foreach(string f in Evidence(path).Where(p=>System.IO.Path.GetFileName(p).StartsWith("events-")).OrderBy(p=>p))foreach(var e in Events(f,()=>bad++)){
            if(Excluded(e,review))continue;count++;var d=Value(e,"data") as Dictionary<string,object>;if(d==null)continue;
            if(Text(e,"kind")=="log"&&(Text(d,"level")=="Error"||Text(d,"level")=="Exception"||Text(d,"level")=="Fatal"||Text(d,"level")=="Assert")){string key=Text(d,"source")+": "+Text(d,"message");int n;errors.TryGetValue(key,out n);if(errors.Count<200||errors.ContainsKey(key))errors[key]=n+1;}
            if(Text(e,"kind")=="performance"){maxMs=Math.Max(maxMs,Convert.ToDouble(Value(d,"maxMs")??0));suppressed=Math.Max(suppressed,Convert.ToInt64(Value(d,"logsSuppressed")??0));dropped=Math.Max(dropped,Convert.ToInt64(Value(d,"queueDropped")??0));}
        }
        b.AppendLine().AppendLine("## 疑似异常与证据完整性").AppendLine("保留事件 "+count+"；截断／无效行 "+bad+"；最慢采样帧 "+maxMs.ToString("F2")+" ms；日志限流 "+suppressed+"；队列丢弃 "+dropped+"。");
        foreach(var e in errors.OrderByDescending(x=>x.Value).Take(20))b.AppendLine("- "+e.Value+" 次："+e.Key);
        b.AppendLine().AppendLine("## 分析边界").AppendLine("程序错误、超过500ms的帧、降落超过120秒是触发条件，不是根因结论。没有错误日志不等于没有玩法故障。持续日志分段轮转最多约32MB；前置环形记录最多1024条；日志突发限流24条/秒。异常原始窗口单个最多4MB，自动标记冷却60秒。崩溃可能损失最后约2秒缓存；磁盘或系统故障可损失更多。JSONL 尾部损坏只在读取时跳过，原证据保留。").AppendLine("同局对比：优先相同哈希 lobby、seed、planet 和 serverTime/networkTick；LAN 或重开局请填相同共同联机编号。UTC依赖各机系统时钟；不要仅凭UTC强行对齐。网络对象编号只在同一局内有意义。").AppendLine("不采集聊天、语音、密码或完整配置值；MOD 自己输出的日志和手动截图仍可能含个人信息。分享前检查。未持续录像，不扫描全世界，不记录每个RPC，不自动归因到MOD。identity.json 的配置哈希可查差异，但不包含配置值；动态内存补丁不能用磁盘SHA完全代表。");
        b.AppendLine("导出规则：仅含已确认保留的现场；删除节点的专属文件与事件引用不导出，共享性能、日志和自动异常继续保留。原始文件留在本机用于恢复。存在删除时，导出 JSONL 按此规则筛选，无效行不复制；无效行统计见上文。");
        return b.ToString();
    }
    static bool Excluded(Dictionary<string,object> e,DiagnosticReview review){
        if(review.Deleted(Text(e,"mark")))return true;
        var d=Value(e,"data") as Dictionary<string,object>;
        return Text(e,"kind").StartsWith("screenshot_",StringComparison.Ordinal)&&d!=null&&review.Deleted(Text(d,"id"));
    }
    public static void Export(string path,string destination,string expectedNotesHash=null){
        if(Active(path))throw new IOException("请先退出游戏，再导出完整诊断包。");
        using(var gate=new FileStream(System.IO.Path.Combine(path,"review.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
        string notesPath=System.IO.Path.Combine(path,"notes.json");
        if(expectedNotesHash!=null&&(File.Exists(notesPath)?Data.Hash(notesPath):"")!=expectedNotesHash)throw new IOException("已保存的时间轴在其他窗口发生变化，请刷新后再导出。");
        var review=new DiagnosticReview(path);if(!review.Confirmed)throw new IOException("请先点击‘保存更改’确认时间轴，再导出。");bool filtered=review.Marks.Any(m=>review.Deleted(m.Id));Recover(path);string report=Report(path);string temp=destination+".partial-"+Guid.NewGuid().ToString("N");
        try{var names=new List<object>();using(var output=new FileStream(temp,FileMode.CreateNew))using(var zip=new ZipArchive(output,ZipArchiveMode.Create)){
            long total=0;foreach(string f in Evidence(path)){
                string name=System.IO.Path.GetFileName(f);var match=Regex.Match(name,@"^(mark|incident|shot)-(\d{9}-\d{3})\.");if(match.Success&&review.Deleted(match.Groups[2].Value))continue;
                total+=new FileInfo(f).Length;if(total>1024L*1024*1024)throw new IOException("单次导出超过 1GB，请先检查截图和记录。");
                byte[] bytes;
                if(name=="notes.json"){var visible=review.Notes.Where(p=>p.Key!="_deleted"&&!review.Deleted(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);bytes=Encoding.UTF8.GetBytes(Data.Json(visible));}
                else if(filtered&&name.EndsWith(".jsonl")){var text=new StringBuilder();foreach(var e in Events(f,()=>{}))if(!Excluded(e,review))text.AppendLine(Data.Json(e));bytes=Encoding.UTF8.GetBytes(text.ToString());}
                else {zip.CreateEntryFromFile(f,name,CompressionLevel.Fastest);names.Add(new{file=name,size=new FileInfo(f).Length,sha256=Data.Hash(f)});continue;}
                using(var entry=zip.CreateEntry(name,CompressionLevel.Fastest).Open())entry.Write(bytes,0,bytes.Length);
                using(var hash=System.Security.Cryptography.SHA256.Create())names.Add(new{file=name,size=bytes.Length,sha256=Data.Hex(hash.ComputeHash(bytes))});
            }
            using(var w=new StreamWriter(zip.CreateEntry("报告.md").Open(),new UTF8Encoding(false)))w.Write(report);
            using(var w=new StreamWriter(zip.CreateEntry("evidence-manifest.json").Open(),new UTF8Encoding(false)))w.Write(Data.Json(new{schema=1,files=names}));
        }Data.Replace(temp,destination);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    public static int Cleanup(string root){
        var sessions=Sessions(root);long size=sessions.Sum(s=>s.Bytes);int n=0;
        foreach(var s in sessions.OrderBy(s=>s.Id)){
            if(s.Protected||s.Status=="运行中"||s.Status!="正常结束")continue;
            if(DateTime.UtcNow-Directory.GetCreationTimeUtc(s.Path)<TimeSpan.FromDays(14)&&size<=1024L*1024*1024)continue;
            // Only our flat, completed, unmarked sessions qualify. Unknown/nested files prevent cleanup.
            if(Directory.GetDirectories(s.Path).Length!=0||Directory.GetFiles(s.Path).Any(f=>System.IO.Path.GetFileName(f)!="active.lock"&&System.IO.Path.GetFileName(f)!="review.lock"&&!Evidence(s.Path).Contains(f)))continue;
            foreach(string f in Directory.GetFiles(s.Path))File.Delete(f);Directory.Delete(s.Path);size-=s.Bytes;n++;
        }return n;
    }
    public static void Install(string root){
        Engine.CheckGameDirectory(root);Engine.CheckGameStopped();string path=System.IO.Path.Combine(root,PluginPath.Replace('/',System.IO.Path.DirectorySeparatorChar));
        using(var resource=typeof(DiagnosticsStore).Assembly.GetManifestResourceStream("LethalDiagnostics.dll")){
            if(resource==null)throw new IOException("此接收端未包含诊断插件。");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string tmp=path+".new";
            using(var output=File.Create(tmp))resource.CopyTo(output);
            if(File.Exists(path)&&Data.Hash(path)==Data.Hash(tmp)){File.Delete(tmp);return;}
            if(File.Exists(path)){string backup=System.IO.Path.Combine(root,".lcpack-updater/diagnostic-backups");Directory.CreateDirectory(backup);string old=System.IO.Path.Combine(backup,Data.Hash(path)+".dll");if(!File.Exists(old))File.Copy(path,old);}
            Data.Replace(tmp,path);
        }
    }
    public static void Configure(string root,bool enabled,string key){
        Engine.CheckGameStopped();if(!Regex.IsMatch(key,@"^F([1-9]|1[0-2])$"))throw new IOException("请选择 F1 至 F12。");
        string path=System.IO.Path.Combine(root,"BepInEx/config/nt1ho.lethaldiagnostics.cfg");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        Data.Atomic(path,"[General]\nEnabled = "+enabled.ToString().ToLowerInvariant()+"\nMarkKey = "+key+"\n");
    }
}
}
