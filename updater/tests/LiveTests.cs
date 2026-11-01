using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LcPack;

class CountRequests:DelegatingHandler {
    public int files, manifests; public bool credentials;
    public CountRequests():base(new HttpClientHandler {AllowAutoRedirect=true,MaxAutomaticRedirections=5}) {}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t) {
        if(r.Headers.Authorization!=null)credentials=true;
        if(r.RequestUri.ToString()==Channel.Latest)Interlocked.Increment(ref manifests);else Interlocked.Increment(ref files);
        return base.SendAsync(r,t);
    }
}
class LiveTests {
    static int passed;
    static void Check(bool value,string name) {if(!value)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
    static UpdateResult Run(string root,string label,out CountRequests counts) {
        counts=new CountRequests();var timer=Stopwatch.StartNew();int last=-10;
        using(var t=new Transfer(counts)) {
            var result=new Engine(root,Engine.CheckGameStopped).Run(t,(s,p)=>{lock(timer){if(p<0||p>=last+10){last=p;Console.WriteLine(label+": "+s);}}}).GetAwaiter().GetResult();
            Console.WriteLine(label+": "+timer.Elapsed.TotalSeconds.ToString("0.00")+" s; downloads="+counts.files+"; latest="+result.latest);return result;
        }
    }
    static void Main() {
        try {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-runs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
            File.WriteAllText(Path.Combine(root,"Lethal Company.exe"),"isolated test fixture");File.WriteAllText(Path.Combine(root,"UnityPlayer.dll"),"fixture");File.WriteAllText(Path.Combine(root,"Lethal Company_Data","Managed","Assembly-CSharp.dll"),"fixture");
            Console.WriteLine("ROOT "+root);
            CountRequests first;var initial=Run(root,"FIRST",out first);
            var state=Data.Load<LocalState>(Path.Combine(root,".lcpack-updater","state.json"));
            Check(state.files.Count==475&&initial.downloaded==475&&first.files==475,"real anonymous first installation of all 475 files");
            Check(!first.credentials,"no publisher token sent by client");
            Check(state.files.All(f=>Data.Hash(SafePath.Target(root,f.path))==f.sha256),"every installed SHA-256 matches published manifest");
            CountRequests latest;Check(Run(root,"LATEST",out latest).latest&&latest.files==0,"real latest check downloads no MOD files");
            var selected=state.files.Where(f=>f.path.StartsWith("BepInEx/config/")&&f.size>0&&f.size<2000).Take(2).ToArray();Check(selected.Length==2,"two small isolated fixtures selected");
            string a=SafePath.Target(root,selected[0].path),b=SafePath.Target(root,selected[1].path);
            File.WriteAllText(a,"local test change");File.Delete(b);
            CountRequests repair;Check(Run(root,"INCREMENTAL",out repair).downloaded==2&&repair.files==2,"real incremental repair downloads only changed and missing files");
            Check(Data.Hash(a)==selected[0].sha256&&Data.Hash(b)==selected[1].sha256,"incremental repair restores exact original data");
            File.WriteAllText(a,"before failed transaction A");File.WriteAllText(b,"before failed transaction B");bool failed=false;
            using(var locked=new FileStream(b,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                try {CountRequests failure;Run(root,"ROLLBACK",out failure);}catch(IOException){failed=true;}
            }
            Check(failed&&File.ReadAllText(a)=="before failed transaction A"&&File.ReadAllText(b)=="before failed transaction B","real downloads followed by locked-file failure automatically roll back");
            CountRequests recovery;Run(root,"RECOVER",out recovery);
            Check(state.files.All(f=>Data.Hash(SafePath.Target(root,f.path))==f.sha256),"all 475 files correct after retry");
            CountRequests last;Check(Run(root,"FINAL",out last).latest&&last.files==0,"final latest check downloads zero files");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","dist",Channel.ClientName),Path.Combine(root,Channel.ClientName));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-live-root.txt"),root);
            Console.WriteLine("RESULT "+passed+" live checks passed; isolated filesystem; no real game launched or modified.");
        }catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    }
}
