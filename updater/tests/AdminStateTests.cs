using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LcPack;
class AdminStateTests {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,StringBuilder path,uint length,uint flags);
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
    [STAThread]static void Main(){try{UiStartup.Run(Run);}catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}}
    static void Run(){
        string root=Workspace.DefaultRoot;var ws=new Workspace(root);string before=Data.Hash(Path.Combine(root,"draft.json"));
        Check(root==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".LethalGoofballsPackPublisher"),"stable per-user publication folder");
        using(var stream=File.OpenRead(Path.Combine(root,"draft.json"))){var final=new StringBuilder(2048);Check(GetFinalPathNameByHandle(stream.SafeFileHandle,final,2048,0)>0&&final.ToString()=="\\\\?\\"+Path.Combine(root,"draft.json"),"draft resolves to the same physical folder without AppData redirection");}
        string publicKey=Cloud.Config.publicKey;var account=CosSettings.Load(ws);
        Check(account!=null,"existing encrypted COS account loaded");Check(account.channel.publicKey==publicKey,"original signing identity matches the distributed client");Check(!String.IsNullOrWhiteSpace(ws.Token()),"existing GitHub credential retained");
        using(var api=new CosApi(account)){var remote=api.Remote().GetAwaiter().GetResult();Check(remote!=null&&remote.version==ws.Current.baseVersion&&remote.files.Count==ws.Current.files.Count,"saved draft matches signed cloud version "+ws.Current.baseVersion+" / "+ws.Current.files.Count+" files");var hashes=remote.files.ToDictionary(f=>f.path,f=>f.sha256,StringComparer.OrdinalIgnoreCase);Check(ws.Current.files.All(f=>hashes.ContainsKey(f.path)&&hashes[f.path]==f.sha256),"every saved file matches the signed cloud manifest");}
        using(var window=new PublisherWindow()){
            var actual=(Workspace)typeof(PublisherWindow).GetField("workspace",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            Check(actual.Root==root,"normal admin constructor uses the same publication folder");
            var status=(System.Windows.Forms.Label)typeof(PublisherWindow).GetField("header",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            Check(status.Text.Contains(ws.Current.baseVersion),"admin footer displays the saved current version");
        }
        Check(before==Data.Hash(Path.Combine(root,"draft.json")),"diagnosis did not rewrite the draft");
        Console.WriteLine("RESULT: existing settings and signed cloud verified; no publication or visible window.");
    }
}
