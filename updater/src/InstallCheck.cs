using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace LcPack {
internal static class InstallCheck {
    // Serve verified candidate bytes to the same transfer, journal and installer used by the receiver.
    sealed class Cache:HttpMessageHandler {
        readonly Workspace ws;readonly Dictionary<string,PackFile> urls;
        public Cache(Workspace ws,Manifest m){this.ws=ws;urls=new Dictionary<string,PackFile>();foreach(var f in m.files){urls[f.url]=f;if(Cloud.Enabled)urls[Cloud.Blob(f)]=f;}}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            PackFile f;if(!urls.TryGetValue(r.RequestUri.GetLeftPart(UriPartial.Path),out f))throw new IOException("未知的隔离下载路径");
            var stream=File.OpenRead(ws.ObjectPath(f.sha256));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=r,Content=new StreamContent(stream)});
        }
    }
    public static async Task Run(AdminWorkspace w,Manifest m,Transfer transfer,Action<string> progress){
        string root=Path.Combine(Path.GetTempPath(),"lgp-install-"+Guid.NewGuid().ToString("N"));
        SafePath.NoLinks(root);Directory.CreateDirectory(root);
        try{
            Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
            foreach(string marker in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllBytes(Path.Combine(root,marker),new byte[0]);
            var old=w.Base;var ws=w.Store;var saved=Path.Combine(root,".lcpack-updater");Directory.CreateDirectory(saved);
            Data.Atomic(Path.Combine(saved,"state.json"),Data.Json(new LocalState{version=old.version,files=old.files}));
            int count=0;
            foreach(var f in old.files.Concat(m.files).GroupBy(f=>f.sha256).Select(g=>g.First())){
                var p=ws.ObjectPath(f.sha256);SafePath.NoLinks(p);
                if(!File.Exists(p)||Data.Hash(p)!=f.sha256){
                    if(String.IsNullOrEmpty(f.url))throw new IOException("候选对象缺失："+f.path);
                    string tmp=p+".verify-"+Guid.NewGuid().ToString("N");try{await transfer.Download(f,tmp,null).ConfigureAwait(false);Data.Replace(tmp,p);}finally{if(File.Exists(tmp))File.Delete(tmp);}
                }
                progress("安装预检 · 核验对象 "+(++count)+" · "+f.path);
            }
            foreach(var f in old.files){string p=SafePath.Target(root,f.path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.Copy(ws.ObjectPath(f.sha256),p);}
            string sentinel=Path.Combine(root,"private-save.fixture");File.WriteAllText(sentinel,"keep");
            string personal=Path.Combine(root,"BepInEx","config","qa-personal-"+Guid.NewGuid().ToString("N")+".cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(personal));File.WriteAllText(personal,"player");
            using(var local=new Transfer(new Cache(ws,m)))await new Engine(root,()=>{}).Apply(m,local,(s,i)=>progress("隔离安装 · "+s)).ConfigureAwait(false);
            var state=Data.Load<LocalState>(Path.Combine(saved,"state.json"));
            if(state.version!=m.version||Data.Json(state.files)!=Data.Json(m.files)||File.ReadAllText(sentinel)!="keep"||File.ReadAllText(personal)!="player")throw new IOException("接收端安装状态不符");
            foreach(var f in m.files){string p=SafePath.Target(root,f.path);if(!File.Exists(p)||Data.Hash(p)!=f.sha256)throw new IOException("接收端安装文件不符："+f.path);}
            foreach(var f in m.deletes)if(File.Exists(SafePath.Target(root,f)))throw new IOException("接收端未清理旧文件："+f);
            if(old.files.Any(f=>!m.files.Any(n=>String.Equals(n.path,f.path,StringComparison.OrdinalIgnoreCase))&&!m.deletes.Contains(f.path,StringComparer.OrdinalIgnoreCase)))throw new IOException("候选遗漏旧文件的删除指令");
            var pages=PageContent.ForRoot(root);pages.Validate();
            if(ws.Current.cumulative!=null){
                var entries=ModInventory.Scan(root,true);var bundle=Data.Read<CumulativeBundle>(ws.Current.cumulative);
                var touched=new HashSet<string>(bundle.results.SelectMany(r=>r.files).Where(f=>f.sha256!=null&&f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Select(f=>f.path),StringComparer.OrdinalIgnoreCase);
                if(touched.Count!=0){
                    var ids=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var f in m.files.Where(f=>f.path.StartsWith("BepInEx/plugins/",StringComparison.OrdinalIgnoreCase)&&f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).OrderByDescending(f=>touched.Contains(f.path))){
                        foreach(string id in AdminInventory.PluginIds(SafePath.Target(root,f.path))){
                            string other;if(ids.TryGetValue(id,out other)&&(touched.Contains(other)||touched.Contains(f.path)))throw new IOException("检测到同一插件 "+id+" 的两份 DLL，请确认旧文件删除规则："+other+"、"+f.path);
                            ids[id]=f.path;
                        }
                    }
                }
                foreach(var mod in bundle.results.Where(r=>r.kind!="移除")){
                    string key=mod.package.Substring(mod.package.IndexOf('-')+1);var found=entries.FirstOrDefault(r=>r.name==key&&r.version==mod.version);
                    if((found==null&&mod.files.Any(f=>f.sha256!=null&&!f.path.StartsWith("BepInEx/config/",StringComparison.OrdinalIgnoreCase)))||pages.Find(key)==null)
                        throw new IOException("接收端未识别登记版本或展示资料："+mod.package);
                }
                if(!pages.History.Any(h=>h.Version==m.version&&h.Changes.Count>0))throw new IOException("安装结果缺少对应版本历史");
            }
        }finally{SafePath.NoLinks(root);Directory.Delete(root,true);}
    }
}
}
