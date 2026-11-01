using System;
using System.Net;
using System.Net.Http;
using LcPack;
class FeedProbe {
    static void Main(){Run().GetAwaiter().GetResult();}
    static async System.Threading.Tasks.Task Run(){
        Console.WriteLine(SelfUpdate.Version+" "+typeof(SelfUpdate).Assembly.Location);
        using(var transfer=new Transfer())Console.WriteLine("ClientLatest="+(await transfer.ClientLatest()).version);
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        using(var http=new HttpClient()){
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LethalGoofballsPack-Updater/1.0");http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.raw+json");
            foreach(var url in new[]{SelfUpdate.Feed,"https://api.github.com/repos/"+Channel.Repo+"/contents/client-update.json?ref=main"}){
                using(var response=await http.GetAsync(url+(url.Contains("?")?"&":"?")+"check="+Guid.NewGuid().ToString("N"))){Console.WriteLine(response.RequestMessage.RequestUri+" status="+(int)response.StatusCode+" date="+response.Headers.Date+" age="+response.Headers.Age);Console.WriteLine(await response.Content.ReadAsStringAsync());}
            }
        }
    }
}
