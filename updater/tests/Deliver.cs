using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LcPack;
class Deliver {
    static void Main(){try{Run().GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
    static async Task Run(){
        string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),folder=Path.Combine(desktop,"LethalGoofballsPack_Updater");
        var files=new[]{Tuple.Create(Path.Combine(folder,Channel.ClientName),Channel.ClientName),Tuple.Create(Path.Combine(folder,"LethalGoofballsPack Publisher.exe"),"LethalGoofballsPack Publisher.exe"),Tuple.Create(Path.Combine(desktop,"LethalGoofballsPack_Updater_v1.1.zip"),"LethalGoofballsPack_Updater_v1.1.zip"),Tuple.Create(Path.Combine(folder,"源码.zip"),"Updater_Source_v1.1.zip")};
        var ws=new Workspace(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LethalGoofballsPackPublisher"));
        using(var api=new GitHubApi(ws.Token())){
            await api.ValidateAccount();var release=await api.LatestRelease();
            if(release==null||release.draft||release.tag_name!="20261025-020649-e9f837")throw new IOException("Unexpected release; no assets uploaded.");
            await Task.WhenAll(files.Select(async item=>{
                string hash=Data.Hash(item.Item1);var old=release.assets.FirstOrDefault(a=>a.name==item.Item2.Replace(' ','.'));
                if(old!=null){if(old.digest!="sha256:"+hash)throw new IOException("Existing delivery asset differs: "+item.Item2);return;}
                using(var stream=File.OpenRead(item.Item1))await api.Upload(release.id,item.Item2,stream,"application/octet-stream",stream.Length,hash);
            }));
            var complete=await api.GetRelease(release.id);
            using(var http=new HttpClient())foreach(var item in files){
                var asset=complete.assets.Single(a=>a.name==item.Item2.Replace(' ','.'));byte[] data=await http.GetByteArrayAsync(asset.browser_download_url);string actual;
                using(var sha=SHA256.Create())actual=Data.Hex(sha.ComputeHash(data));
                if(actual!=Data.Hash(item.Item1))throw new IOException("Anonymous delivery checksum failed: "+item.Item2);
                Console.WriteLine("VERIFIED "+item.Item2+" | "+data.Length+" | "+asset.browser_download_url);
            }
        }
    }
}
