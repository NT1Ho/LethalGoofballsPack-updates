using System;using System.IO;using System.Linq;using System.IO.Compression;using LcPack;
class DiagnosticsTests {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(string[] args){
  string root=Path.Combine(Path.GetTempPath(),"lgp-receiver-tests-"+Guid.NewGuid().ToString("N"));string home=DiagnosticsStore.Home(root);Directory.CreateDirectory(home);
  Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data/Managed"));File.WriteAllText(Path.Combine(root,"Lethal Company.exe"),"fixture");File.WriteAllText(Path.Combine(root,"UnityPlayer.dll"),"fixture");File.WriteAllText(Path.Combine(root,"Lethal Company_Data/Managed/Assembly-CSharp.dll"),"fixture");
  DiagnosticsStore.Install(root);string dll=Path.Combine(root,DiagnosticsStore.PluginPath);string hash=Data.Hash(dll);DiagnosticsStore.Install(root);Check(Data.Hash(dll)==hash,"embedded plugin install is idempotent");
  File.WriteAllText(dll,"old fixture version");string old=Data.Hash(dll);DiagnosticsStore.Install(root);Check(Data.Hash(dll)==hash&&File.Exists(Path.Combine(root,".lcpack-updater/diagnostic-backups/"+old+".dll")),"plugin replacement preserves rollback copy");
  DiagnosticsStore.Configure(root,false,"F9");Check(File.ReadAllText(Path.Combine(root,"BepInEx/config/nt1ho.lethaldiagnostics.cfg")).Contains("MarkKey = F9"),"monitor toggle and alternate key saved");
  string path=Path.Combine(home,"20200101T000000000-test");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"session.json"),"{\"utc\":\"2020-01-01\"}");
  File.WriteAllText(Path.Combine(path,"events-000.jsonl"),"{\"kind\":\"state\",\"data\":{\"position\":[1,2,3]}}\n{partial");
  File.WriteAllText(Path.Combine(path,"mark-120000000-001.json"),"{\"id\":\"120000000-001\",\"kind\":\"manual\",\"data\":{\"planet\":\"Experimentation\"}}");
  using(var lease=new FileStream(Path.Combine(path,"active.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.Read)){
   Check(DiagnosticsStore.Active(path),"active writer detected");bool blocked=false;try{DiagnosticsStore.Export(path,Path.Combine(root,"no.zip"));}catch(IOException){blocked=true;}Check(blocked,"active export blocked");
  }
  Check(DiagnosticsStore.Recover(path)==1,"partial crash tail recovered");Check(File.ReadAllText(Path.Combine(path,"events-000.jsonl")).EndsWith("{partial"),"raw crash evidence unchanged");
  DiagnosticsStore.SaveNote(path,"120000000-001","假人死亡后仍然跳舞","friends-test",false);Check(DiagnosticsStore.Report(path).Contains("假人死亡后仍然跳舞"),"Chinese per-mark notes in report");
  File.WriteAllText(Path.Combine(path,"private-token.txt"),"must not export");string zip=Path.Combine(root,"report.zip");DiagnosticsStore.Export(path,zip);using(var z=ZipFile.OpenRead(zip)){Check(z.GetEntry("报告.md")!=null&&z.GetEntry("evidence-manifest.json")!=null,"structured report and evidence manifest");Check(z.GetEntry("private-token.txt")==null,"export allowlist excludes unrelated files");}
  File.WriteAllText(Path.Combine(path,"closed.json"),"{}");Directory.SetCreationTimeUtc(path,DateTime.UtcNow.AddDays(-20));Check(DiagnosticsStore.Cleanup(root)==0&&Directory.Exists(path),"marked session survives automatic cleanup");
  string ordinary=Path.Combine(home,"20200102T000000000-test");Directory.CreateDirectory(ordinary);File.WriteAllText(Path.Combine(ordinary,"session.json"),"{}");File.WriteAllText(Path.Combine(ordinary,"closed.json"),"{}");Directory.SetCreationTimeUtc(ordinary,DateTime.UtcNow.AddDays(-20));Check(DiagnosticsStore.Cleanup(root)==1&&!Directory.Exists(ordinary),"expired ordinary session cleaned");
  if(args.Length>0){var sessions=DiagnosticsStore.Sessions(args[0]);foreach(var s in sessions.Where(s=>!DiagnosticsStore.Active(s.Path))){DiagnosticsStore.SaveNote(s.Path,"sessionNote","本机隔离测试记录","diagnostics-v1-lan",true);DiagnosticsStore.Export(s.Path,Path.Combine(root,s.Id+".zip"));}Console.WriteLine("EXPORTED "+sessions.Count);}
  Console.WriteLine("RESULT "+root);
 }
}
