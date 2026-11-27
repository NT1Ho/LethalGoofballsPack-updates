using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LcPack;

class HatchetConfigMigrationTests {
    const string PathInPack="BepInEx/config/NT1Ho.Hatchet.cfg";
    sealed class Fixture : HttpMessageHandler {
        public byte[] download;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(download)});
        }
    }
    static void Check(bool ok,string message) {
        if(!ok)throw new InvalidOperationException(message);
        Console.WriteLine("PASS "+message);
    }
    static void Apply(Engine engine,Transfer transfer,Manifest manifest) {
        var apply=typeof(Engine).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
        ((Task<UpdateResult>)apply.Invoke(engine,new object[]{manifest,transfer,(Action<string,int>)((_,i)=>{})})).GetAwaiter().GetResult();
    }
    static int Main(string[] args) {
        try {
            var configure=typeof(Cloud).GetMethod("Configure",BindingFlags.Static|BindingFlags.NonPublic);
            configure.Invoke(null,new object[]{null});
            byte[] defaults=Encoding.UTF8.GetBytes("[Aim HUD]\nCrosshair = Global\nHit Feedback = HatchetOnly\n");
            string tmp=Path.Combine(args[0],"defaults.cfg");
            Directory.CreateDirectory(args[0]);
            File.WriteAllBytes(tmp,defaults);
            string hash=Data.Hash(tmp);
            var file=new PackFile{path=PathInPack,sha256=hash,size=defaults.Length,
                url="https://github.com/NT1Ho/LethalGoofballsPack-updates/releases/download/V2.6.2/"+hash+".bin"};
            var manifest=new Manifest{version="V2.6.2",files={file}};
            var bom=new UTF8Encoding(true);
            string original="[Aim HUD]\r\nCrosshair = Off\r\nHit Feedback = Global\r\n\r\n[Other]\r\nCrosshair = Off\r\n";
            foreach(string mode in new[]{"unmanaged","V2.6","new","missing-key","missing-section","header-at-eof"}) {
                string root=Path.Combine(args[0],mode), local=Path.Combine(root,PathInPack);
                Directory.CreateDirectory(Path.GetDirectoryName(local));
                if(mode!="new") {
                    string text=mode=="missing-key"?"[Aim HUD]\nHit Feedback = Off\n":mode=="missing-section"?"[Weapon Inertia]\nStrength = 0.5\n":
                        mode=="header-at-eof"?"[Aim HUD]":original;
                    File.WriteAllText(local,text,bom);
                }
                if(mode=="V2.6") {
                    string home=Path.Combine(root,".lcpack-updater");
                    Directory.CreateDirectory(home);
                    Data.Atomic(Path.Combine(home,"state.json"),Data.Json(new LocalState{version="V2.6"}));
                }
                using(var transfer=new Transfer(new Fixture{download=defaults})) {
                    var engine=new Engine(root,()=>{});
                    Apply(engine,transfer,manifest);
                    string text=File.ReadAllText(local);
                    if(mode=="unmanaged"||mode=="V2.6") {
                        Check(text==original.Replace("Crosshair = Off\r\nHit Feedback","Crosshair = Global\r\nHit Feedback") &&
                            File.ReadAllBytes(local).Take(3).SequenceEqual(bom.GetPreamble()),mode+" preserves feedback, unrelated section, CRLF and BOM");
                        File.WriteAllText(local,original,bom);
                        Apply(engine,transfer,manifest);
                        Check(File.ReadAllText(local)==original,mode+" subsequent user change preserved");
                    } else Check(text.Contains("Crosshair = Global") && text.Split(new[]{"[Aim HUD]"},StringSplitOptions.None).Length==2,mode+" adds one Global key and one section");
                }
            }
            return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
