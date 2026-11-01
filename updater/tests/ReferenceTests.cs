using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LcPack;
class ReferenceTests {
 class Fake:HttpMessageHandler {
  public bool missing,badSize,badHash,draft;public int calls;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken t){
   calls++;string p=q.RequestUri.AbsolutePath;object data;
   if(p.Contains("/tags/"))data=new Release{id=p.EndsWith("/old")?1:2,tag_name=p.Split('/').Last(),draft=draft};
   else if(p.EndsWith("/assets"))data=new List<ReleaseAsset>(missing&&p.Contains("/2/")?new ReleaseAsset[0]:new[]{new ReleaseAsset{name=new string('a',64)+".bin",size=badSize?4:3,digest="sha256:"+new string(badHash?'b':'a',64)}});
   else throw new Exception("Unexpected request");
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=q,Content=new StringContent(Data.Json(data))});
  }
 }
 static PackFile F(string path,string tag){return new PackFile{path=path,sha256=new string('a',64),size=3,url=Channel.Web+"/releases/download/"+tag+"/"+new string('a',64)+".bin"};}
 static void Check(bool b,string text){if(!b)throw new Exception(text);Console.WriteLine("PASS "+text);}
 static async Task Reject(Func<Task> f,string text){try{await f();}catch(System.IO.IOException){Check(true,text);return;}throw new Exception("Accepted "+text);}
 static async Task Run(){
  var m=new Manifest{version="new",files=new List<PackFile>{F("BepInEx/core/MonoMod.dll","old"),F("BepInEx/patchers/MonoMod.dll","new")}};
  string temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"lcpack-base-"+Guid.NewGuid().ToString("N"));var ws=new Workspace(temp);ws.SetRemote(m);ws.CheckBase(m);
  var changed=Data.Read<Manifest>(Data.Json(m));changed.files[0].url=changed.files[1].url;
  await Reject(()=>Task.Run(()=>ws.CheckBase(changed)),"same-version cloud change rejects stale publisher draft");
  Check(CosPublisher.BackupUploads(m).Length==1&&CosPublisher.BackupUploads(m)[0].path.Contains("patchers"),"duplicate hash with historical URL cannot hide new-release upload");
  var fake=new Fake();using(var api=new GitHubApi("fixture",fake))using(var transfer=new Transfer()){
   await api.ValidateReferences(m,transfer);Check(fake.calls==4,"each referenced release checked even for identical content");
   fake.missing=true;await Reject(()=>api.ValidateReferences(m,transfer),"missing reused attachment blocks publication");
   await api.ValidateReferences(m,transfer,"new");Check(true,"only pending release skipped before its upload");
   fake.missing=false;fake.badSize=true;await Reject(()=>api.ValidateReferences(m,transfer),"wrong attachment size blocked");
   fake.badSize=false;fake.badHash=true;await Reject(()=>api.ValidateReferences(m,transfer),"wrong attachment hash blocked");
   fake.badHash=false;fake.draft=true;await Reject(()=>api.ValidateReferences(m,transfer),"inherited private draft is not a public download source");
  }
 }
 static void Main(){try{Run().GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
