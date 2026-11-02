using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using LcDiagnostics;

// Compile with FrameDrop.cs and System.Web.Extensions.dll. Optional input is a local exported events JSONL.
class FrameDropTests {
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static bool Window(FrameDrop d,double ms,bool eligible=true){return d.Observe(100,100*ms,ms,eligible);}
    static int Feed(FrameDrop d,double ms,int n){int marks=0;for(int i=0;i<n;i++)if(Window(d,ms))marks++;return marks;}
    static void Main(string[] args){
        var d=new FrameDrop();
        Check(Feed(d,16.7,100)==0,"stable 60 FPS");
        d.Reset();Feed(d,5,7);
        Check(!d.Observe(100,99*5+1000,1000,true)&&Feed(d,5,2)==0,"one isolated stall excluded");
        Check(Feed(d,17,2)==0&&Feed(d,5,2)==0,"short dip");
        Check(Feed(d,17,30)==1,"sustained relative drop recorded once");
        Feed(d,5,4);for(int i=0;i<5;i++)Check(!Window(d,17,false),"cooldown");
        Check(Window(d,17,true),"ongoing new episode eligible after cooldown");
        d.Reset();Check(Feed(d,60,5)==1,"severe low FPS with no warm baseline");
        if(args.Length==0)return;
        var json=new JavaScriptSerializer();bool landed=false;double next=0;int windows=0,marks=0;
        d.Reset();foreach(var line in File.ReadLines(args[0])){
            var row=json.Deserialize<Dictionary<string,object>>(line);var data=(Dictionary<string,object>)row["data"];
            string kind=(string)row["kind"];double t=Convert.ToDouble(row["mono"]);
            if(kind=="state")landed=(string)data["phase"]=="landed";
            if(kind!="performance")continue;
            windows++;if(!landed||(data.ContainsKey("focused")&&!(bool)data["focused"])){d.Reset();continue;}
            int frames=Convert.ToInt32(data["frames"]);double mean=Convert.ToDouble(data["meanMs"]);
            if(d.Observe(frames,frames*mean,Convert.ToDouble(data["maxMs"]),t>=next)){
                Console.WriteLine("mark mono="+t.ToString("F3")+" meanMs="+mean.ToString("F2")+" referenceMs="+d.ReferenceMs.ToString("F2"));next=t+60;marks++;
            }
        }
        Console.WriteLine(windows+" windows, "+marks+" sustained marks; missing focus assumed foreground");
    }
}
