using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LcPack {
internal sealed class GameRecord {
    public DiagnosticSession Session;
    public string Title, Subtitle;
    public static GameRecord From(DiagnosticSession session) {
        var row = new GameRecord { Session = session };
        var metadata = DiagnosticsStore.Read(Path.Combine(session.Path, "session.json"));
        DateTimeOffset start;
        row.Title = DateTimeOffset.TryParse(Convert.ToString(DiagnosticsStore.Value(metadata,"utc")), out start)
            ? start.ToLocalTime().ToString("yyyy/MM/dd   HH:mm") : "开始时间未知";
        try { row.Update(new DiagnosticReview(session.Path)); }
        catch (IOException) { row.Subtitle="记录暂时无法读取"; }
        catch (InvalidOperationException) { row.Subtitle="部分记录无法读取"; }
        catch (ArgumentException) { row.Subtitle="部分记录无法读取"; }
        return row;
    }
    public void Update(DiagnosticReview review) {
        int count = review.Marks.Count(m => m.Manual && !review.Deleted(m.Id));
        Subtitle = "时长 " + PlayerText.Elapsed(review.Duration) + "  ·  " + count + " 个标记";
        if (Session.Status != "正常结束") {
            string status = Session.Status.Contains("停顿") ? "记录停顿" : Session.Status.StartsWith("运行中") ? "进行中" : "异常退出";
            Subtitle = status+" · "+PlayerText.Elapsed(review.Duration)+" · "+count+" 个标记";
        }
    }
    public override string ToString() { return Title + "   " + Subtitle; }
}

internal static class PlayerText {
    public static string Elapsed(double seconds) {
        var t = TimeSpan.FromSeconds(Math.Max(0,seconds));
        return t.TotalHours >= 1 ? ((int)t.TotalHours) + t.ToString(@"\:mm\:ss") : t.ToString(@"mm\:ss");
    }
    public static string Time(DiagnosticMark mark) {
        DateTimeOffset value;
        return DateTimeOffset.TryParse(mark.Utc,out value) ? value.ToLocalTime().ToString("HH:mm:ss") : Elapsed(mark.Seconds);
    }
    public static string Kind(DiagnosticMark mark) {
        switch(mark.Kind) {
            case "manual": return "手动标记";
            case "sustained_frame_drop": return "持续掉帧";
            case "frame_stall": return "画面卡顿";
            case "program_error": return "程序报错";
            case "loading_timeout": return "加载用时过长";
            default: return "自动异常";
        }
    }
    static object Get(Dictionary<string,object> fields,string key) {
        return fields == null ? null : DiagnosticsStore.Value(fields,key);
    }
    static string NameOf(object value) {
        var entity = value as Dictionary<string,object>;
        if(entity == null) return "";
        string item = Get(entity,"item") as string;
        if(!String.IsNullOrWhiteSpace(item)) return item;
        if(Get(entity,"enemyType") != null) return "怪物";
        if(Get(entity,"playerDead") != null) return "玩家";
        if(Get(entity,"interactable") != null) return "可交互物体";
        return (Get(entity,"name") as string ?? "").Replace("(Clone)","").Trim();
    }
    public static string Summary(DiagnosticMark mark) {
        var data = Get(mark.Data,"data") as Dictionary<string,object>;
        var state = Get(data,"state") as Dictionary<string,object> ?? data;
        var lines = new List<string>();
        var planet = Get(state,"planet") as string;
        string area = Convert.ToString(Get(state,"area"));
        string location = area=="facility" ? "设施内" : area=="ship" ? "飞船内" : area=="outside" ? "室外" : "";
        string place = String.Join(" · ", new[]{planet, location}.Where(v=>!String.IsNullOrWhiteSpace(v)));
        if(place.Length>0) lines.Add("所在位置　" + place);
        var position = Get(state,"position") as IList;
        if(position!=null && position.Count==3) {
            double[] xyz=new double[3];
            if(Enumerable.Range(0,3).All(i=>Double.TryParse(Convert.ToString(position[i]),out xyz[i])))
                lines.Add("玩家坐标　" + String.Join("，",xyz.Select(v=>v.ToString("0.#"))));
        }
        string role = Convert.ToString(Get(state,"role"));
        if(role=="host" || role=="client") lines.Add("联机身份　" + (role=="host"?"房主":"客户端"));
        string held=NameOf(Get(state,"held")), aim=NameOf(Get(state,"aim"));
        if(held.Length>0) lines.Add("手持物品　"+held);
        if(aim.Length>0) lines.Add("瞄准对象　"+aim);
        var nearby=Get(state,"nearby") as IList;
        if(nearby!=null) {
            var names=nearby.Cast<object>().Select(NameOf).Where(n=>n.Length>0).Distinct().Take(4).ToArray();
            if(names.Length>0) lines.Add("附近对象　"+String.Join("、",names));
        }
        double ms;
        if(Double.TryParse(Convert.ToString(Get(data,"maxMs")),out ms)) lines.Add("最慢一帧　"+ms.ToString("0")+" 毫秒");
        if(mark.Kind=="sustained_frame_drop"&&Double.TryParse(Convert.ToString(Get(data,"meanMs")),out ms)&&ms>0)
            lines.Add("最近约两秒　"+(1000/ms).ToString("0")+" FPS");
        if(lines.Count==0) lines.Add("这次现场没有更多可读的状态信息。");
        return String.Join(Environment.NewLine,lines);
    }
    public static Bitmap Screenshot(string path,string id) {
        try {
            using(var stream=new FileStream(Path.Combine(path,"shot-"+id+".png"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var image=Image.FromStream(stream)) return new Bitmap(image);
        } catch(IOException) { return null; } catch(ArgumentException) { return null; }
          catch(OutOfMemoryException) { return null; } // GDI+ also uses this for incomplete images.
    }
}
}
