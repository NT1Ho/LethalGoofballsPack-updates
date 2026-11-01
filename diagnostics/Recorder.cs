using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LcDiagnostics {
// Shared by the Mono plugin and the Windows receiver; no Unity objects cross this boundary.
public static class Json {
    public static Dictionary<string,object> Obj(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d; }
    public static string Text(object value) {
        if(value==null)return "null";
        var s=value as string;if(s!=null){var b=new StringBuilder("\"");foreach(char c in s){switch(c){case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;default:if(c<32)b.Append("\\u"+((int)c).ToString("x4"));else b.Append(c);break;}}return b.Append('"').ToString();}
        if(value is bool)return (bool)value?"true":"false";
        var dict=value as IDictionary<string,object>;if(dict!=null){var a=new List<string>();foreach(var p in dict)a.Add(Text(p.Key)+":"+Text(p.Value));return "{"+String.Join(",",a)+"}";}
        var seq=value as System.Collections.IEnumerable;if(seq!=null){var a=new List<string>();foreach(var x in seq)a.Add(Text(x));return "["+String.Join(",",a)+"]";}
        var n=Convert.ToString(value,CultureInfo.InvariantCulture);return n=="NaN"||n=="Infinity"||n=="-Infinity"?"null":n;
    }
    public static void Atomic(string path,object data){string tmp=path+".tmp";File.WriteAllText(tmp,Text(data),new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
}
public sealed class Recorder : IDisposable {
    sealed class Entry { public string Kind;public object Data;public double Seconds;public string Utc;public string Mark; }
    sealed class Incident { public StreamWriter Writer;public double Until;public long Bytes; }
    readonly Queue<Entry> queue=new Queue<Entry>();readonly Queue<Entry> priority=new Queue<Entry>();
    readonly Queue<Entry> ring=new Queue<Entry>();readonly List<Incident> incidents=new List<Incident>();
    readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Thread thread;FileStream lease;StreamWriter journal;int segment,markCount;long bytes;bool stopping,normal;
    public long Dropped;public string Failure;public readonly string DirectoryPath;public double Seconds{get{return clock.Elapsed.TotalSeconds;}}
    public Recorder(string home,object session){
        Directory.CreateDirectory(home);DirectoryPath=Path.Combine(home,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(DirectoryPath);
        lease=new FileStream(Path.Combine(DirectoryPath,"active.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.Read);
        Json.Atomic(Path.Combine(DirectoryPath,"session.json"),session);
        thread=new Thread(Work){IsBackground=true,Name="LGP diagnostics writer",Priority=ThreadPriority.BelowNormal};thread.Start();
        Add("session_start",session);
    }
    public bool Add(string kind,object data){return Enqueue(kind,data,null);}
    bool Enqueue(string kind,object data,string mark){
        lock(gate){if(stopping||Failure!=null)return false;var q=mark==null?queue:priority;if(q.Count>=(mark==null?1024:32)){Interlocked.Increment(ref Dropped);return false;}q.Enqueue(new Entry{Kind=kind,Data=data,Seconds=Seconds,Utc=DateTime.UtcNow.ToString("O"),Mark=mark});}wake.Set();return true;
    }
    public string Mark(string kind,object data){
        if(Interlocked.Increment(ref markCount)>128)return null;
        string id=DateTime.UtcNow.ToString("HHmmssfff")+"-"+markCount.ToString("D3");
        return Enqueue(kind,data,id)?id:null;
    }
    static string Line(Entry e){return Json.Text(Json.Obj("schema",1,"utc",e.Utc,"mono",e.Seconds,"kind",e.Kind,"mark",e.Mark,"data",e.Data));}
    StreamWriter Open(string name){return new StreamWriter(new FileStream(Path.Combine(DirectoryPath,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false),16384);}
    static void Durable(StreamWriter w){w.Flush();((FileStream)w.BaseStream).Flush(true);}
    void Work(){
        try{
            journal=Open("events-000.jsonl");double lastFlush=0;long reported=0;
            while(true){
                Entry e=null;lock(gate){if(priority.Count>0&&(queue.Count==0||priority.Peek().Seconds<=queue.Peek().Seconds))e=priority.Dequeue();else if(queue.Count>0)e=queue.Dequeue();else if(stopping)break;}
                if(e!=null){
                    string line=Line(e);int size=Encoding.UTF8.GetByteCount(line)+1;
                    if(bytes+size>4*1024*1024){Durable(journal);journal.Dispose();segment++;journal=Open("events-"+segment.ToString("D3")+".jsonl");bytes=0;if(segment>=8)File.Delete(Path.Combine(DirectoryPath,"events-"+(segment-8).ToString("D3")+".jsonl"));}
                    journal.WriteLine(line);bytes+=size;
                    if(e.Mark!=null){
                        var inc=new Incident{Writer=Open("incident-"+e.Mark+".jsonl"),Until=e.Seconds+30};
                        var history=ring.ToArray();var beforeLines=new List<string>();long remaining=4*1024*1024-size-512;bool truncated=false;
                        for(int i=history.Length-1;i>=0;i--){string l=Line(history[i]);int n=Encoding.UTF8.GetByteCount(l)+1;if(n>remaining){truncated=true;break;}beforeLines.Add(l);remaining-=n;}
                        if(truncated){string boundary=Json.Text(Json.Obj("kind","pre_window_truncated","reason","4MB incident budget"));inc.Writer.WriteLine(boundary);inc.Bytes+=Encoding.UTF8.GetByteCount(boundary)+1;}
                        beforeLines.Reverse();foreach(string l in beforeLines){inc.Writer.WriteLine(l);inc.Bytes+=Encoding.UTF8.GetByteCount(l)+1;}
                        inc.Writer.WriteLine(line);inc.Bytes+=size;Durable(inc.Writer);incidents.Add(inc);
                        Json.Atomic(Path.Combine(DirectoryPath,"mark-"+e.Mark+".json"),Json.Obj("schema",1,"id",e.Mark,"kind",e.Kind,"utc",e.Utc,"mono",e.Seconds,"data",e.Data));
                    }
                    foreach(var inc in incidents.ToArray()){
                        if(inc.Until<e.Seconds||inc.Bytes+size>4*1024*1024){inc.Writer.WriteLine(Json.Text(Json.Obj("kind","window_end","budgetExceeded",inc.Bytes+size>4*1024*1024)));Durable(inc.Writer);inc.Writer.Dispose();incidents.Remove(inc);}
                        else if(e.Mark==null){inc.Writer.WriteLine(line);inc.Bytes+=size;}
                    }
                    ring.Enqueue(e);while(ring.Count>1024||(ring.Count>0&&ring.Peek().Seconds<e.Seconds-60))ring.Dequeue();
                }
                if(Seconds-lastFlush>=2){
                    if(Dropped!=reported){journal.WriteLine(Json.Text(Json.Obj("kind","dropped","count",Dropped,"mono",Seconds)));reported=Dropped;}
                    Durable(journal);foreach(var inc in incidents)Durable(inc.Writer);lastFlush=Seconds;
                    Json.Atomic(Path.Combine(DirectoryPath,"heartbeat.json"),Json.Obj("utc",DateTime.UtcNow.ToString("O"),"mono",Seconds,"dropped",Dropped,"segment",segment));
                }
                if(e==null)wake.WaitOne(200);
            }
            foreach(var inc in incidents){inc.Writer.WriteLine(Json.Text(Json.Obj("kind","window_end","reason","process_exit_before_or_at_window_end")));Durable(inc.Writer);inc.Writer.Dispose();}incidents.Clear();
            Durable(journal);if(normal)Json.Atomic(Path.Combine(DirectoryPath,"closed.json"),Json.Obj("utc",DateTime.UtcNow.ToString("O"),"dropped",Dropped));
        }catch(Exception ex){Failure=ex.GetType().Name+": "+ex.Message;try{Json.Atomic(Path.Combine(DirectoryPath,"writer-error.json"),Json.Obj("error",Failure));}catch{}}
        finally{if(journal!=null)journal.Dispose();foreach(var i in incidents)try{i.Writer.Dispose();}catch{}lease.Dispose();}
    }
    public void Dispose(){lock(gate){normal=true;stopping=true;}wake.Set();thread.Join(2500);}
}
}
