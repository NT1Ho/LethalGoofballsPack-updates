using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using LcPack;

// Compile against the previous official receiver and place it beside this verifier.
class VerifyReceiverUpgrade {
    static async Task Verify(string expectedVersion,string expectedHash) {
        string root=Path.Combine(Path.GetTempPath(),"lgp-upgrade-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data/Managed"));
        foreach(string name in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})
            File.WriteAllText(Path.Combine(root,name),"fixture");
        Directory.CreateDirectory(SelfUpdate.Home(root));
        using(var transfer=new Transfer()){
            var release=await transfer.ClientLatest();
            if(!SelfUpdate.IsNewer(release)||release.version!=expectedVersion||release.file.sha256!=expectedHash)
                throw new InvalidDataException("Unexpected signed receiver release.");
            await transfer.PrepareDownload(release.file);
            await SelfUpdate.Prepare(root,release,transfer,(message,percent)=>{});
            string cached=SelfUpdate.CachedExe(root,release);
            SelfUpdate.ValidateRuntime(root,cached);
            using(var http=new HttpClient()){
                string signed=await http.GetStringAsync("https://raw.githubusercontent.com/"+Channel.Repo+"/main/client-update.signed.json?check="+Guid.NewGuid().ToString("N"));
                var fallback=Data.Read<ClientRelease>(Cloud.Verify(signed,"client"));
                if(fallback.version!=release.version||fallback.file.sha256!=expectedHash)throw new InvalidDataException("Backup feed mismatch.");
            }
            await transfer.VerifyGitHubFile(release.file,Path.Combine(root,"github.exe"));
            await transfer.VerifyCloudFile(release.file,Path.Combine(root,"cos.exe"));
            Console.WriteLine("PASS original receiver "+SelfUpdate.Version+" discovered, downloaded and cached "+release.version);
            Console.WriteLine("PASS signed backup feed and both public binary sources: "+expectedHash+" ("+release.file.size+" bytes)");
        }
    }
    static int Main(string[] args){
        try{Verify(args[0],args[1]).GetAwaiter().GetResult();return 0;}
        catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
