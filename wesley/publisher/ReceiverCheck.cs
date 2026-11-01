using System;
using System.IO;
using System.Linq;
using LcPack;
class ReceiverCheck {
  static void Main(string[] args){try{Check(args);}catch(Exception e){do{Console.WriteLine(e.GetType().Name+" "+e.Message);e=e.InnerException;}while(e!=null); Environment.ExitCode=1;}}
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  static void Check(string[] args){
    var pages=PageContent.Load(Path.Combine(args[0],PageContent.RelativePath));
    var mods=ModInventory.Scan(args[0],true);
    foreach(var name in new[]{"Wesleys_Moons","WesleysInteriors","Facto","Decrepit_Store","Universal Wrench"}){
        var row=mods.Single(m=>m.name==name || (name=="Universal Wrench" && m.path.IndexOf("/UniversalWrench/UniversalWrench.dll",StringComparison.OrdinalIgnoreCase)>=0));var page=pages.Find(name);
        Console.WriteLine(name+"|"+row.version+"|"+row.status+"|"+page.Order+"|own="+page.Own+"|"+page.Purpose);
        if(name=="Universal Wrench"?page.Order!=0||!page.Own:page.Own||row.version.Contains("未确认"))throw new Exception("Bad "+name);
    }
    Console.WriteLine("History: "+pages.History[0].Version+"; previous="+pages.History[1].Version);
  }
}
