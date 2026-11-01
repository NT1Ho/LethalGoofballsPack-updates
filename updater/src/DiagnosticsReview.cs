using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LcPack {
public sealed class DiagnosticMark {
    public string Id,Kind,Utc;public double Seconds;public Dictionary<string,object> Data;
    public bool Manual {get{return Kind=="manual";}}
    public override string ToString(){DateTimeOffset t;return (DateTimeOffset.TryParse(Utc,out t)?t.ToLocalTime().ToString("HH:mm:ss.fff"):Id)+" · "+(Manual?"F8 现场":Kind);}
}
// Edits only replace notes.json. Recorder evidence stays immutable, including deleted marks.
public sealed class DiagnosticReview {
    public readonly string Path;public readonly List<DiagnosticMark> Marks;
    public readonly Dictionary<string,object> Notes;
    readonly Dictionary<string,object> deleted;HashSet<string> savedDeleted;string baseline,diskHash;
    string NoteFile {get{return System.IO.Path.Combine(Path,"notes.json");}}
    public DiagnosticReview(string path){
        Path=path;Notes=DiagnosticsStore.Read(NoteFile);diskHash=File.Exists(NoteFile)?Data.Hash(NoteFile):"";
        deleted=DiagnosticsStore.Value(Notes,"_deleted") as Dictionary<string,object>??new Dictionary<string,object>();Notes["_deleted"]=deleted;
        Marks=DiagnosticsStore.Evidence(path).Where(f=>System.IO.Path.GetFileName(f).StartsWith("mark-")).Select(f=>{
            var m=DiagnosticsStore.Read(f);return new DiagnosticMark{Id=System.IO.Path.GetFileNameWithoutExtension(f).Substring(5),Kind=Convert.ToString(DiagnosticsStore.Value(m,"kind")),Utc=Convert.ToString(DiagnosticsStore.Value(m,"utc")),Seconds=Convert.ToDouble(DiagnosticsStore.Value(m,"mono")??0),Data=m};
        }).OrderBy(m=>m.Seconds).ThenBy(m=>m.Utc,StringComparer.Ordinal).ThenBy(m=>m.Id).ToList();baseline=Data.Json(Notes);savedDeleted=new HashSet<string>(deleted.Keys);
    }
    public bool Dirty {get{return baseline!=Data.Json(Notes);}}
    public bool Confirmed {get{return !String.IsNullOrEmpty(Note("_confirmed"));}}
    public bool Deleted(string id){return deleted.ContainsKey(id);}
    public bool SavedDeleted(string id){return savedDeleted.Contains(id);}
    public string Note(string id){return Convert.ToString(DiagnosticsStore.Value(Notes,id));}
    public void SetNote(string id,string text){if(Note(id)!=text)Notes[id]=text;}
    public void SetDeleted(string id,bool value){
        var mark=Marks.FirstOrDefault(m=>m.Id==id);if(mark==null||!mark.Manual)throw new IOException("只能删除手动故障标记；自动异常与共享日志会保留。");
        if(value){if(!Deleted(id))deleted[id]=DateTime.UtcNow.ToString("O");}else deleted.Remove(id);
    }
    public void Save(){
        if(DiagnosticsStore.Active(Path))throw new IOException("游戏仍在写入此会话；退出后才能保存更改。");
        if(Notes.Any(p=>p.Key!="_deleted"&&p.Key!="pinned"&&Convert.ToString(p.Value).Length>(p.Key=="group"?100:8000)))throw new IOException("备注最多 8000 字，共同联机编号最多 100 字。");
        // Serializes receiver edits/exports across windows without touching the recorder's lock.
        using(var gate=new FileStream(System.IO.Path.Combine(Path,"review.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            if((File.Exists(NoteFile)?Data.Hash(NoteFile):"")!=diskHash)throw new IOException("记录已在其他窗口修改，请刷新后重新编辑。");
            Notes["_confirmed"]=DateTime.UtcNow.ToString("O");
            Data.Atomic(NoteFile,Data.Json(Notes));diskHash=Data.Hash(NoteFile);baseline=Data.Json(Notes);savedDeleted=new HashSet<string>(deleted.Keys);
        }
    }
    public void Export(string destination){if(Dirty||!Confirmed)throw new IOException("请先点击‘保存更改’确认时间轴；导出不会自动保存。");DiagnosticsStore.Export(Path,destination,diskHash);}
    public double Duration {
        get{double end=Marks.Select(m=>m.Seconds).DefaultIfEmpty(0).Max();var h=DiagnosticsStore.Read(System.IO.Path.Combine(Path,"heartbeat.json"));end=Math.Max(end,Convert.ToDouble(DiagnosticsStore.Value(h,"mono")??0));
            DateTimeOffset start,finish;var s=DiagnosticsStore.Read(System.IO.Path.Combine(Path,"session.json"));var c=DiagnosticsStore.Read(System.IO.Path.Combine(Path,"closed.json"));
            if(DateTimeOffset.TryParse(Convert.ToString(DiagnosticsStore.Value(s,"utc")),out start)&&DateTimeOffset.TryParse(Convert.ToString(DiagnosticsStore.Value(c,"utc")),out finish))end=Math.Max(end,(finish-start).TotalSeconds);return Math.Max(1,end);}
    }
}
}
