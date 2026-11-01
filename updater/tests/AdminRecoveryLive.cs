using System;
using System.IO;
using System.Linq;
using LcPack;
class AdminRecoveryLive {
    static void Main(string[] args){try{
        var ws=new Workspace(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"admin-recovery-runs",Guid.NewGuid().ToString("N")));
        ws.Current=Data.Load<Draft>(args[0]);ws.Save();Console.WriteLine("Before: "+ws.Current.baseVersion+", cache files: "+Directory.GetFiles(Path.Combine(ws.Root,"objects")).Length);
        using(var api=new GitHubApi(null))using(var transfer=new Transfer()){
            var m=Publisher.Remote(api,transfer).GetAwaiter().GetResult();
            if(m.version!="V1.5")throw new Exception("Unexpected remote MOD version: "+m.version);
            ws.CacheCatalog(m.files,transfer).GetAwaiter().GetResult();ws.SetRemote(m);
        }
        var groups=AdminCatalog.Build(ws);
        if(groups.Count!=42||groups.Any(g=>g.version=="未识别"||ModNames.Description(g.name)=="暂无用途说明")||Directory.GetFiles(Path.Combine(ws.Root,"objects")).Length!=2)throw new Exception("Catalog recovery failed");
        Console.WriteLine("PASS old cloud draft with empty object cache -> V1.5, 42 recognized groups, only 2 catalog downloads, zero MOD downloads.");
        Console.WriteLine("FIXTURE: "+ws.Root);
    }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
}
