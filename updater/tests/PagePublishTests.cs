using System;using System.IO;using System.Linq;using System.Net;using System.Net.Http;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Security.Cryptography;using System.Web.Script.Serialization;using LcPack;
class PagePublishTests {
 static string Hash(byte[] b){using(var h=SHA256.Create())return Data.Hex(h.ComputeHash(b));}
 class State {public Dictionary<string,byte[]> cos=new Dictionary<string,byte[]>(),gh=new Dictionary<string,byte[]>();public List<ReleaseAsset> assets=new List<ReleaseAsset>();public string feed;public bool failPointer;public int uploads;}
 class Fake:HttpMessageHandler {
  State st;public Fake(State s){st=s;}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){string p=r.RequestUri.AbsolutePath;byte[] body=r.Content==null?null:await r.Content.ReadAsByteArrayAsync();object obj=null;byte[] bytes=null;HttpStatusCode status=HttpStatusCode.OK;
   if(r.RequestUri.Host.EndsWith("myqcloud.com")){
    if(r.Method==HttpMethod.Put){if(st.failPointer&&p=="/lcpack/manifest.json")return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){RequestMessage=r};if(st.cos.ContainsKey(p)&&r.Headers.Contains("x-cos-forbid-overwrite"))status=HttpStatusCode.Conflict;else st.cos[p]=body;}
    st.cos.TryGetValue(p,out bytes);if(bytes==null)status=HttpStatusCode.NotFound;
   }else if(r.RequestUri.Host=="api.github.com"){
    if(p=="/user")obj=new{login=Channel.Repo.Split('/')[0]};
    else if(p=="/repos/"+Channel.Repo)obj=new{full_name=Channel.Repo,@private=false,archived=false};
    else if(p.Contains("/contents/")){if(r.Method==HttpMethod.Put){var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));st.feed=Encoding.UTF8.GetString(Convert.FromBase64String((string)d["content"]));obj=new{};}else obj=new{sha="feed-sha",content=Convert.ToBase64String(Encoding.UTF8.GetBytes(st.feed))};}
    else if(p.Contains("/tags/"))obj=new Release{id=1,tag_name="V1.9.2",assets=st.assets,draft=false};
    else if(p.EndsWith("/1/assets"))obj=st.assets;
    else if(p.Contains("/releases/assets/")){var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));var a=st.assets.Single(x=>x.id==Int64.Parse(p.Split('/').Last()));st.gh[(string)d["name"]]=st.gh[a.name];st.gh.Remove(a.name);a.name=(string)d["name"];obj=a;}
    else throw new Exception("Unhandled API "+p);
   }else if(r.RequestUri.Host=="uploads.github.com"){
    string name=Uri.UnescapeDataString(r.RequestUri.Query.Substring(6));if(st.gh.ContainsKey(name))throw new Exception("duplicate upload");st.gh[name]=body;var a=new ReleaseAsset{id=st.assets.Count+1,name=name,size=body.Length,digest="sha256:"+Hash(body)};st.assets.Add(a);obj=a;st.uploads++;
   }else {st.gh.TryGetValue(Uri.UnescapeDataString(p.Split('/').Last()),out bytes);if(bytes==null)status=HttpStatusCode.NotFound;}
   if(bytes==null)bytes=Encoding.UTF8.GetBytes(obj==null?"":Data.Json(obj));var response=new HttpResponseMessage(status){RequestMessage=r,Content=new ByteArrayContent(bytes)};
   if(r.RequestUri.Host.EndsWith("myqcloud.com")&&status==HttpStatusCode.OK){response.Headers.Add("x-cos-meta-sha256",Hash(bytes));if(r.Method==HttpMethod.Put)using(var md5=MD5.Create())response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\""+Data.Hex(md5.ComputeHash(body))+"\"");}return response;
  }
 }
 static int count;static void Check(bool v,string s){if(!v)throw new Exception(s);Console.WriteLine("PASS "+s);count++;}
 static async Task Run(){var root=Path.Combine(Path.GetTempPath(),"LcPagesPublish-"+Guid.NewGuid().ToString("N"));var ws=new Workspace(root);ws.SaveToken("fake-token");Cloud.Configure(null);var account=CosSettings.Save(ws,"https://unit-1234567890.cos.ap-guangzhou.myqcloud.com","TEST","TEST");var old=new Manifest{version="V1.9.2"};ws.SetRemote(old);string path=Path.Combine(root,"pages.json");File.WriteAllText(path,"{\"Schema\":1,\"Mods\":[],\"History\":[]}");ws.Add(path,PagePublisher.PathName);var next=Data.Read<Manifest>(Data.Json(new Manifest{version=old.version,files=ws.Current.files,deletes=ws.Current.deletes}));next.files[0].url=Channel.Web+"/releases/download/V1.9.2/"+next.files[0].sha256+".bin";
 Check(PagePublisher.OnlyPages(old,next),"adding first presentation file allowed");var bad=Data.Read<Manifest>(Data.Json(next));bad.files[0].path="BepInEx/config/test.cfg";Check(!PagePublisher.OnlyPages(old,bad),"configuration change rejected");bad=Data.Read<Manifest>(Data.Json(next));bad.deletes.Add("BepInEx/plugins/a.dll");Check(!PagePublisher.OnlyPages(old,bad),"deletion rejected");Check(!PagePublisher.OnlyPages(next,old),"presentation deletion rejected");
 var st=new State();st.feed=CosSettings.Sign(account,"mods",Data.Json(old));st.cos["/lcpack/manifest.json"]=Encoding.UTF8.GetBytes(st.feed);st.cos["/lcpack/versions/V1.9.2.json"]=Encoding.UTF8.GetBytes(st.feed);st.gh["manifest.json"]=Encoding.UTF8.GetBytes(Data.Json(old));st.assets.Add(new ReleaseAsset{id=1,name="manifest.json",size=st.gh["manifest.json"].Length,digest="sha256:"+Hash(st.gh["manifest.json"])});
 var job=new PagePublishJob{Before=old,ReleaseBefore=old,After=next};st.failPointer=true;bool failed=false;
 using(var cos=new CosApi(account,new Fake(st)))using(var transfer=new Transfer(new Fake(st)))try{await PagePublisher.Run(ws,account,cos,transfer,job,s=>{},new GitHubApi("fake",new Fake(st)));}catch(IOException){failed=true;}
 Check(failed&&File.Exists(Path.Combine(root,"page-publish.pending.json")),"partial publication preserves recovery journal");Check(Cloud.Verify(Encoding.UTF8.GetString(st.cos["/lcpack/manifest.json"]),"mods")==Data.Json(old),"failed pointer leaves old COS feed usable");
 st.failPointer=false;using(var cos=new CosApi(account,new Fake(st)))using(var transfer=new Transfer(new Fake(st)))await PagePublisher.Run(ws,account,cos,transfer,job,s=>{},new GitHubApi("fake",new Fake(st)));
 Check(Cloud.Verify(st.feed,"mods")==Data.Json(next)&&Cloud.Verify(Encoding.UTF8.GetString(st.cos["/lcpack/manifest.json"]),"mods")==Data.Json(next),"retry converges both signed feeds");Check(Encoding.UTF8.GetString(st.gh["manifest.json"])==Data.Json(next),"release manifest matches signed payload");Check(st.assets.Any(a=>a.name.StartsWith("manifest.before-pages-")),"old manifest retained");Check(st.uploads==2,"retry does not duplicate content uploads");Check(ws.Current.baseVersion=="V1.9.2"&&!File.Exists(Path.Combine(root,"page-publish.pending.json")),"same version and completed journal");var current=next;
 for(int cycle=0;cycle<3;cycle++){
  File.WriteAllText(path,cycle%2==0?"{\"Schema\":1,\"Mods\":[],\"History\":[]} ":"{\"Schema\":1,\"Mods\":[],\"History\":[]}");ws.Add(path,PagePublisher.PathName);
  var target=Data.Read<Manifest>(Data.Json(new Manifest{version="V1.9.2",files=ws.Current.files,deletes=ws.Current.deletes}));target.files[0].url=Channel.Web+"/releases/download/V1.9.2/"+target.files[0].sha256+".bin";
  using(var cos=new CosApi(account,new Fake(st)))using(var transfer=new Transfer(new Fake(st)))await PagePublisher.Run(ws,account,cos,transfer,new PagePublishJob{Before=current,ReleaseBefore=current,After=target},s=>{},new GitHubApi("fake",new Fake(st)));current=target;
 }
 Check(st.assets.Select(a=>a.name).Distinct().Count()==st.assets.Count,"repeated edit and restore never collide with archived manifests");
 Console.WriteLine(count+" checks passed");}
 static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
