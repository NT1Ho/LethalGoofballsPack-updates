using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Drawing;using LcPack;
class DiagnosticsIntegration {
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("PASS "+msg);}
 static object V(Dictionary<string,object> d,string k){return DiagnosticsStore.Value(d,k);}
 static Dictionary<string,object> LatestMark(string path){return Directory.GetFiles(path,"mark-*.json").Select(DiagnosticsStore.Read).Where(m=>Convert.ToString(V(m,"kind"))=="manual").OrderBy(m=>Convert.ToString(V(m,"utc"))).Last();}
 static void Main(string[] args){
  Directory.CreateDirectory(args[2]);var h=DiagnosticsStore.Sessions(args[0]).First();var c=DiagnosticsStore.Sessions(args[1]).First();
  Check(h.Status=="正常结束","host normal exit finalized");Check(!DiagnosticsStore.Active(c.Path)&&!File.Exists(Path.Combine(c.Path,"closed.json")),"forced client exit preserved unclosed session");
  var hm=LatestMark(h.Path);var cm=LatestMark(c.Path);var hs=(Dictionary<string,object>)V(hm,"data");var cs=(Dictionary<string,object>)V(cm,"data");
  Check(Convert.ToString(V(hs,"role"))=="host"&&Convert.ToString(V(cs,"role"))=="client","actual host and client identities");
  Check(Convert.ToString(V(hs,"seed"))==Convert.ToString(V(cs,"seed"))&&Convert.ToString(V(hs,"planet"))==Convert.ToString(V(cs,"planet")),"same actual planet and seed");
  Check(Convert.ToBoolean(V(hs,"landed"))&&Convert.ToBoolean(V(cs,"landed")),"both peers observed landing");
  double dt=Math.Abs(Convert.ToDouble(V(hs,"serverTime"))-Convert.ToDouble(V(cs,"serverTime")));Check(dt<1,"paired mark server-time difference under one second: "+dt.ToString("F3"));
  var hn=((IEnumerable)V(hs,"nearby")).Cast<Dictionary<string,object>>().Where(d=>V(d,"netId")!=null).Select(d=>Convert.ToString(V(d,"netId"))).ToArray();var cn=((IEnumerable)V(cs,"nearby")).Cast<Dictionary<string,object>>().Where(d=>V(d,"netId")!=null).Select(d=>Convert.ToString(V(d,"netId"))).ToArray();Check(hn.Intersect(cn).Any(),"same network objects observed on both peers");
  foreach(var pair in new[]{Tuple.Create(args[0],h,hm,"host"),Tuple.Create(args[1],c,cm,"client-forced-exit")}){
   var session=pair.Item2;string id=Convert.ToString(V(pair.Item3,"id"));using(var image=Image.FromFile(Path.Combine(session.Path,"shot-"+id+".png")))Check(image.Width>100&&image.Height>100,pair.Item4+" PNG decodes "+image.Width+"x"+image.Height);
   var identity=DiagnosticsStore.Read(Path.Combine(session.Path,"identity.json"));var loaded=((IEnumerable)V(identity,"loadedPlugins")).Cast<Dictionary<string,object>>().ToArray();Check(loaded.All(p=>!String.IsNullOrEmpty(Convert.ToString(V(p,"sha256")))),pair.Item4+" all "+loaded.Length+" loaded plugins have actual hashes");
   foreach(var plugin in loaded.Where(p=>Convert.ToString(V(p,"guid"))=="nt1ho.lethaldiagnostics"||Convert.ToString(V(p,"name"))=="UniversalWrench"))Check(Data.Hash(Path.Combine(pair.Item1,Convert.ToString(V(plugin,"path"))))==Convert.ToString(V(plugin,"sha256")),pair.Item4+" independent disk hash: "+V(plugin,"name"));
   int invalid=DiagnosticsStore.Recover(session.Path);Check(invalid==0,pair.Item4+" all persisted lines readable");
   DiagnosticsStore.SaveNote(session.Path,id,pair.Item4=="host"?"隔离双进程实测：Asteroid-13降落后F8记录；随后正常退出。":"隔离双进程实测：Asteroid-13降落后F8记录；随后强制结束客户端，验证恢复。","diagnostics-v1-lan",true);
   DiagnosticsStore.Export(session.Path,Path.Combine(args[2],pair.Item4+".zip"));Check(DiagnosticsStore.Report(session.Path).Contains("diagnostics-v1-lan"),pair.Item4+" shared group included in report");
  }
  File.WriteAllText(Path.Combine(args[2],"association.json"),Data.Json(new{serverTimeDifferenceSeconds=dt,sharedNetworkObjects=hn.Intersect(cn).Count(),hostMarkedUtc=V(hm,"utc"),clientMarkedUtc=V(cm,"utc"),scope="same machine real LAN; not remote internet"}));
 }
}

