using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;

class RetirementInstallTests {
    sealed class Feed:HttpMessageHandler {
        public string signed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            if(!r.RequestUri.AbsolutePath.EndsWith("/manifest.json"))throw new Exception("Unexpected download");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=r,Content=new StringContent(signed)});
        }
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static int Main(string[] args){try{
        using(var rsa=new RSACryptoServiceProvider(2048)){
            rsa.PersistKeyInCsp=false;
            var config=new CloudConfig{endpoint="https://fixture-123.cos.ap-guangzhou.myqcloud.com",publicKey=rsa.ToXmlString(false)};
            typeof(Cloud).GetMethod("Configure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{config});
            var manifest=new Manifest{version="qa-retirement",deletes=RetiredFiles.Snatch.ToList()};
            string payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(Data.Json(manifest)));
            var signed=new SignedDocument{kind="mods",payload=payload,signature=Convert.ToBase64String(rsa.SignData(Cloud.SignBytes("mods",payload),CryptoConfig.MapNameToOID("SHA256")))};
            foreach(string version in new[]{"unmanaged","V1.9","V2.1.2","V2.2"}){
                string root=Path.Combine(args[0],version);Directory.CreateDirectory(Path.Combine(root,"Lethal Company_Data","Managed"));
                foreach(string path in new[]{"Lethal Company.exe","UnityPlayer.dll","Lethal Company_Data/Managed/Assembly-CSharp.dll"})File.WriteAllText(Path.Combine(root,path),"fixture");
                foreach(string path in RetiredFiles.Snatch){string p=SafePath.Target(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,"edited old contents");}
                if(version!="unmanaged"){
                    string home=Path.Combine(root,".lcpack-updater");Directory.CreateDirectory(home);
                    Data.Atomic(Path.Combine(home,"state.json"),Data.Json(new LocalState{version=version,files=RetiredFiles.Snatch.Select(p=>new PackFile{path=p,sha256=new string('0',64)}).ToList()}));
                }
                using(var t=new Transfer(new Feed{signed=Data.Json(signed)})){
                    var result=new Engine(root,()=>{}).Run(t,(s,i)=>{}).GetAwaiter().GetResult();
                    Check(result.deleted==5&&result.skipped==0&&RetiredFiles.Snatch.All(p=>!File.Exists(SafePath.Target(root,p))),"signed candidate cleans "+version+" including edited config");
                    Check(new Engine(root,()=>{}).Run(t,(s,i)=>{}).GetAwaiter().GetResult().deleted==0,"repeat check stays clean "+version);
                }
            }
            signed.signature=Convert.ToBase64String(new byte[256]);string badRoot=Path.Combine(args[0],"unmanaged");
            string retained=SafePath.Target(badRoot,RetiredFiles.Snatch[0]);File.WriteAllText(retained,"retain on bad signature");
            bool rejected=false;using(var t=new Transfer(new Feed{signed=Data.Json(signed)}))try{new Engine(badRoot,()=>{}).Run(t,(s,i)=>{}).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}
            Check(rejected&&File.Exists(retained),"invalid signature cannot authorize retirement");
            Console.WriteLine("Receiver candidate "+typeof(Engine).Assembly.GetName().Version+"; isolated signed feed only.");
        }
        return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
