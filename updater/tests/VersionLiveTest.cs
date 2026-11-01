using System;
using System.Collections.Generic;
using LcPack;
class VersionLiveTest {
    static void Main(string[] args) {
        try {
            var seen=new List<Tuple<string,string>>();
            using(var transfer=new Transfer()) {
                var result=new Engine(args[0],Engine.CheckGameStopped).Run(transfer,(s,p)=>{},(local,remote)=>seen.Add(Tuple.Create(local,remote))).GetAwaiter().GetResult();
                if(!result.latest||result.downloaded!=0||result.version!="V1.5"||seen.Count!=2||seen[0].Item1!="V1.5"||seen[0].Item2!=null||seen[1].Item2!="V1.5")throw new Exception("Version display values or zero-download check failed");
                Console.WriteLine("PASS public GitHub: local V1.5, latest V1.5, verified installed V1.5, zero downloads. Isolated fixture only.");
            }
        }catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    }
}
