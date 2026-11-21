using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LcPack {
public sealed class MultiplayerEvent {
    public string Room, Role, Peer, Client, Player, Source, Category, Code, Status, Link, Detail, Utc;
    public double Time, LocalElapsed, ReceivedTime;
    public int Sequence, Index;
    public long EventId;
    public string[] Marks;
    public string ClientLabel {
        get {
            if(Role=="host")return "房主";
            if(Role=="local")return "本机";
            return "客机 "+(Player.Length>0?"P"+Player:Client.Length>0?"#"+Client:"")+(Peer.Length>=6?" · "+Peer.Substring(0,6):"");
        }
    }
}
public sealed class MultiplayerTimeline {
    sealed class MarkPoint {
        public string Id,Room;
        public double Time;
    }
    public readonly List<MultiplayerEvent> Rows=new List<MultiplayerEvent>();
    public readonly List<Dictionary<string,object>> Peers=new List<Dictionary<string,object>>();
    public readonly List<Dictionary<string,object>> Rooms=new List<Dictionary<string,object>>();
    public readonly List<Dictionary<string,object>> Links=new List<Dictionary<string,object>>();
    readonly Dictionary<string,object> delivery=new Dictionary<string,object>();
    public int InvalidLines {get;private set;}
    public int DuplicateBatches {get;private set;}
    public long RejectedItems {get;private set;}
    public long MaxCriticalLost {get;private set;}
    public long MaxOrdinaryLost {get;private set;}
    public bool Available {get{return Rows.Count>0||Peers.Count>0||Rooms.Count>0;}}

    static object Value(Dictionary<string,object> row,string key){return DiagnosticsStore.Value(row,key);}
    static string Text(Dictionary<string,object> row,string key){return Convert.ToString(Value(row,key),CultureInfo.InvariantCulture)??"";}
    static double Number(Dictionary<string,object> row,string key,double fallback=-1){
        double value;
        return Double.TryParse(Text(row,key),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!Double.IsInfinity(value)&&!Double.IsNaN(value)?value:fallback;
    }
    void Event(Dictionary<string,object> envelope,Dictionary<string,object> body,Dictionary<string,object> record,int index){
        Rows.Add(new MultiplayerEvent {
            Room=Text(envelope,"room"),Role=Text(envelope,"role"),Peer=Text(envelope,"peer"),
            Client=Text(envelope,"client"),Player=Text(envelope,"player"),
            Source=Text(body,"source"),Category=Text(body,"category"),Code=Text(body,"code"),
            Status=Text(body,"status"),Link=Text(body,"link"),Detail=Text(body,"detail"),Utc=Text(body,"utc"),
            Time=Number(body,"time"),LocalElapsed=Number(record,"mono"),ReceivedTime=Number(envelope,"receivedTime"),
            Sequence=(int)Number(envelope,"seq",0),Index=index,EventId=(long)Number(body,"id",0)
        });
    }
    public static MultiplayerTimeline Load(DiagnosticReview review) {
        var timeline=new MultiplayerTimeline();
        var marks=new List<MarkPoint>();
        var batches=new HashSet<string>(StringComparer.Ordinal);
        var records=new HashSet<string>(StringComparer.Ordinal);
        var evidence=DiagnosticsStore.Evidence(review.Path).ToArray();
        var incidents=new HashSet<string>(review.Marks.Where(m=>!review.Deleted(m.Id)).Select(m=>"incident-"+m.Id+".jsonl"),StringComparer.Ordinal);
        foreach(string file in evidence.Where(IsJournal).OrderBy(p=>Path.GetFileName(p),StringComparer.Ordinal)
            .Concat(evidence.Where(p=>incidents.Contains(Path.GetFileName(p))).OrderBy(p=>p,StringComparer.Ordinal))){
            foreach(var row in DiagnosticsStore.Events(file,()=>timeline.InvalidLines++)){
                var d=Value(row,"data") as Dictionary<string,object>;
                string kind=Text(row,"kind");
                if(d==null||!kind.StartsWith("coop_",StringComparison.Ordinal))continue;
                string recordId=Text(row,"recordId");
                if(recordId.Length>0&&!records.Add(recordId))continue;
                switch(kind){
                    case "coop_event":
                        var signal=Value(d,"event") as Dictionary<string,object>;
                        if(signal!=null)timeline.Event(d,signal,row,0);
                        break;
                    case "coop_batch":
                        string scope=Text(d,"room")+"/"+Text(d,"peer");
                        if(!batches.Add(scope+"/"+Text(d,"seq"))){timeline.DuplicateBatches++;break;}
                        var items=Value(d,"items") as IEnumerable;
                        if(items==null)break;
                        int index=0;
                        foreach(object item in items){
                            var body=item as Dictionary<string,object>;
                            if(body!=null)timeline.Event(d,body,row,index++);
                        }
                        timeline.delivery[scope]=new{room=Text(d,"room"),peer=Text(d,"peer"),client=Text(d,"client"),lastBatch=Number(d,"seq"),
                            criticalLost=Number(d,"criticalLost",0),ordinaryLost=Number(d,"ordinaryLost",0),lastReceiveTime=Number(d,"receivedTime")};
                        timeline.RejectedItems+=(long)Number(d,"rejectedItems",0);
                        timeline.MaxCriticalLost=Math.Max(timeline.MaxCriticalLost,(long)Number(d,"criticalLost",0));
                        timeline.MaxOrdinaryLost=Math.Max(timeline.MaxOrdinaryLost,(long)Number(d,"ordinaryLost",0));
                        break;
                    case "coop_peer": timeline.Peers.Add(d);break;
                    case "coop_room": timeline.Rooms.Add(d);break;
                    case "coop_link":
                    case "coop_fault":
                    case "coop_packet_rejected":
                    case "coop_drop":
                        timeline.Links.Add(d);break;
                    case "coop_mark":
                        string id=Text(d,"id");
                        if(id.Length>0&&!review.Deleted(id))marks.Add(new MarkPoint{Id=id,Room=Text(d,"room"),Time=Number(d,"time")});
                        break;
                }
            }
        }
        var byRoom=marks.Where(m=>m.Time>=0).ToLookup(m=>m.Room);
        foreach(var row in timeline.Rows)
            row.Marks=row.Time<0?new string[0]:byRoom[row.Room].Where(m=>row.Time>=m.Time-60&&row.Time<=m.Time+30).Select(m=>m.Id).Distinct().ToArray();
        var roomOrder=timeline.Rows.GroupBy(r=>r.Room).ToDictionary(g=>g.Key,g=>g.Min(r=>r.LocalElapsed));
        timeline.Rows.Sort((a,b)=>{
            int i=roomOrder[a.Room].CompareTo(roomOrder[b.Room]);if(i!=0)return i;
            i=String.CompareOrdinal(a.Room,b.Room);if(i!=0)return i;
            i=a.Time.CompareTo(b.Time);if(i!=0)return i;
            i=a.LocalElapsed.CompareTo(b.LocalElapsed);if(i!=0)return i;
            i=a.Sequence.CompareTo(b.Sequence);return i!=0?i:a.Index.CompareTo(b.Index);
        });
        return timeline;
    }
    static bool IsJournal(string path){string name=Path.GetFileName(path);return name.StartsWith("events-")||name.StartsWith("key-");}
    internal object Coverage(){
        var errors=Rows.Where(r=>r.Category=="exception").GroupBy(r=>new{r.Room,r.Peer,r.Source,r.Link}).Select(g=>new{
            g.Key.Room,g.Key.Peer,g.Key.Source,g.Key.Link,records=g.Count(),samples=g.OrderBy(r=>r.Time).Take(2).Select(r=>new{r.Status,r.Detail,r.Utc,r.Time}).ToArray()
        }).ToArray();
        return new{schema=1,peerStates=Peers,rooms=Rooms,linkEvents=Links,delivery=delivery.Values.ToArray(),
            rowCount=Rows.Count,invalidLines=InvalidLines,duplicateBatches=DuplicateBatches,rejectedItems=RejectedItems,exceptions=errors,notes=new[]{
                "Compare network time only within the same room. Negative times are not synchronized. Original client UTC and host receipt time are retained.",
                "Waiting and unreported mean no compatible evidence has arrived. An unavailable client cannot report all missing records.",
                "Capacity, unconfirmed batches and known drops do not establish whether an unrecorded event happened.",
                "Third-party coverage includes loading, dependencies, warnings and observable results, not private internal state."}};
    }
    public string Summary(){
        var states=Peers.GroupBy(p=>Text(p,"room")+"/"+Text(p,"client")).Select(g=>{
            var p=g.LastOrDefault(e=>Text(e,"status")!="disconnected"&&Text(e,"status")!="room_closed")??g.Last();
            return "客机 "+Text(p,"client")+" · "+Text(p,"status")+(p==g.Last()?"":" → "+Text(g.Last(),"status"))+
                (Text(p,"plugin").Length>0?" · "+Text(p,"plugin"):"");
        }).ToArray();
        return Rows.Count+" 条跨端事件 · "+states.Length+" 次客机连接"+
            (states.Length==0?"":"\r\n"+String.Join("；",states.Take(8)))+
            (InvalidLines>0?" · "+InvalidLines+" 行损坏或尚未写完":"");
    }
    internal long Write(TextWriter writer){
        long count=0;
        foreach(var row in Rows){
            string line=Data.Json(row);writer.WriteLine(line);
            count+=Encoding.UTF8.GetByteCount(line)+writer.NewLine.Length;
        }
        return count;
    }
}
}

