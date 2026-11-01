using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using LcDiagnostics;
class Tests {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  string root=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"lgp-diagnostic-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var r=new Recorder(root,Json.Obj("test",true));r.Add("before",Json.Obj("value","中文\n\"\\"));string id=r.Mark("manual",Json.Obj("pos",new[]{1,2,3}));r.Add("after",Json.Obj("value",2));
  if(args.Length>1&&args[1]=="crash"){Console.WriteLine(r.DirectoryPath);Thread.Sleep(3000);Environment.FailFast("intentional test child crash");}
  r.Dispose();var window=File.ReadAllLines(Path.Combine(r.DirectoryPath,"incident-"+id+".jsonl"));Check(window.Any(s=>s.Contains("before"))&&window.Any(s=>s.Contains("after")),"incident retains real pre/post queued records");
  foreach(string l in window)using(JsonDocument.Parse(l)){}Check(File.Exists(Path.Combine(r.DirectoryPath,"closed.json")),"normal close marker durable");
  using(var lease=File.Open(Path.Combine(r.DirectoryPath,"active.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(true,"writer releases lease");
  var flood=new Recorder(root,Json.Obj("test",true));for(int i=0;i<100000;i++)flood.Add("flood",Json.Obj("seq",i,"payload",new string('x',4096)));Thread.Sleep(500);var mark=flood.Mark("manual",Json.Obj("protected",true));flood.Dispose();Check(flood.Dropped>0,"bounded queue reports overflow");Check(File.Exists(Path.Combine(flood.DirectoryPath,"mark-"+mark+".json")),"mark survives saturated ordinary queue");Check(new FileInfo(Path.Combine(flood.DirectoryPath,"incident-"+mark+".jsonl")).Length<=4*1024*1024,"pre-window also respects incident byte limit");
  var rotate=new Recorder(root,Json.Obj("test",true));string payload=new string('z',4500);for(int i=0;i<12000;i++){while(!rotate.Add("rotation",Json.Obj("seq",i,"payload",payload)))Thread.Sleep(1);}rotate.Dispose();Check(Directory.GetFiles(rotate.DirectoryPath,"events-*.jsonl").Length==8&&!File.Exists(Path.Combine(rotate.DirectoryPath,"events-000.jsonl")),"54MB stream rotates and removes oldest segments");Console.WriteLine("RESULT "+root);
 }
}
