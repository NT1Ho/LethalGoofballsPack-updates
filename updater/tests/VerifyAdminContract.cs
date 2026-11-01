using System;using System.IO;using System.Linq;using System.Reflection;using LcPack;
class VerifyAdminContract {
    static int Main(string[] args){
        try{
            var config=Data.Load<CloudConfig>(Path.Combine(args[0],"qa-public.json"));
            typeof(Cloud).GetMethod("Configure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{config});
            var m=Data.Read<Manifest>(Cloud.Verify(File.ReadAllText(Path.Combine(args[0],"qa-signed.json")),"mods"));
            SafePath.ValidateManifest(m);
            var pages=PageContent.Load(Path.Combine(args[0],"qa-pages.json"));
            var sample=ModInventory.Scan(Path.Combine(args[0],"qa-game"),true).Single(x=>x.name=="Sample");
            if(!pages.History.Any(h=>h.Version==m.version)||sample.version!="1.0.2")throw new Exception("Receiver catalog mismatch");
            Console.WriteLine("PASS official receiver "+SelfUpdate.Version+" verifies simulated signature, accepts manifest/history and recognizes imported package Sample 1.0.2");
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
