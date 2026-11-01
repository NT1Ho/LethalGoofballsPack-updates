using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LcPack;
class CosMigration {
    static void Main(){try{Run().GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}}
    static async Task Run(){
        var ws=new Workspace(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LethalGoofballsPackPublisher"));
        var account=CosSettings.Load(ws);if(account==null)throw new IOException("COS setup missing");
        var before=Data.Read<Manifest>(Data.Json(new Manifest{version=ws.Current.baseVersion,files=ws.Current.files,deletes=ws.Current.deletes}));
        Console.WriteLine("Migration: "+before.version+"; "+before.files.Count+" files; "+before.files.Sum(f=>f.size)+" bytes");
        using(var api=new CosApi(account))using(var transfer=new Transfer()){
            var result=await CosPublisher.Publish(ws,account,api,transfer,Console.WriteLine);
            if(!CosPublisher.Equivalent(before,result)||before.version!=result.version)throw new IOException("Pack changed during migration");
            var publicResult=await transfer.Latest();if(!CosPublisher.Equivalent(before,publicResult))throw new IOException("Public manifest mismatch");
            Console.WriteLine("PASS: signed public COS manifest; all pack paths/hashes/sizes/deletions unchanged");
            await CosPublisher.Backup(ws,account,result,Console.WriteLine);
            Console.WriteLine("PASS: GitHub signed backup updated");
        }
    }
}
