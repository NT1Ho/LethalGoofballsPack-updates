using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using LogLevel = BepInEx.Logging.LogLevel;
using ThreadPriority = System.Threading.ThreadPriority;

namespace LcDiagnostics {
[BepInPlugin("nt1ho.lethaldiagnostics","LethalGoofballs Diagnostics","0.1.0")]
public sealed class Plugin : BaseUnityPlugin, ILogListener {
    Recorder recorder;ConfigEntry<bool> monitor;ConfigEntry<Key> hotkey;
    float nextSample,nextFrameReport,lastMark=-10,nextAuto;int frames,slowFrames,gc0;double sum,max,ownTicks;long logSecond;int logCount,errorCount;long suppressed;object lastError;
    int pendingError;bool quitting;string lastState="",loadingState="";float loadingSince;bool loadingReported;
    readonly Collider[] nearby=new Collider[128];readonly Queue<double> frameSamples=new Queue<double>();
    public LogLevel LogLevelFilter {get{return LogLevel.All;}}
    void Awake(){
        monitor=Config.Bind("General","Enabled",true,"Continuous local diagnostics. Does not change gameplay.");
        hotkey=Config.Bind("General","MarkKey",Key.F8,"Manual incident and screenshot. Change if another MOD uses F8.");
        if(!monitor.Value){enabled=false;return;}
        try{
            var process=Process.GetCurrentProcess();
            recorder=new Recorder(Path.Combine(Paths.GameRootPath,".lcpack-diagnostics"),Json.Obj("schema",1,"plugin","0.1.0","utc",DateTime.UtcNow.ToString("O"),"pid",process.Id,"processStartUtc",process.StartTime.ToUniversalTime().ToString("O"),"unity",Application.unityVersion,"application",Application.version,"cpu",SystemInfo.processorType,"logicalCores",SystemInfo.processorCount,"ramMB",SystemInfo.systemMemorySize,"gpu",SystemInfo.graphicsDeviceName,"vramMB",SystemInfo.graphicsMemorySize,"os",SystemInfo.operatingSystem,"privacy","local only; screenshots/logs may contain names; config values are not collected","preSeconds",60,"postSeconds",30));
            BepInEx.Logging.Logger.Listeners.Add(this);Application.logMessageReceivedThreaded+=UnityLog;
            SceneManager.sceneLoaded+=Loaded;SceneManager.sceneUnloaded+=Unloaded;StartCoroutine(IdentityAfterLoad());
        }catch(Exception e){Logger.LogError("Diagnostics unavailable: "+e.Message);enabled=false;}
    }
    void Loaded(Scene s,LoadSceneMode mode){recorder.Add("scene_loaded",Json.Obj("scene",s.name,"mode",mode.ToString()));}
    void Unloaded(Scene s){recorder.Add("scene_unloaded",Json.Obj("scene",s.name));}
    public void LogEvent(object sender,LogEventArgs e){
        if(e.Source.SourceName=="Unity Log")return;
        CaptureLog(e.Source.SourceName,e.Level.ToString(),e.Data==null?"":e.Data.ToString(),"",(e.Level&(LogLevel.Error|LogLevel.Fatal))!=0);
    }
    void UnityLog(string message,string stack,LogType type){CaptureLog("Unity",type.ToString(),message,stack,type==LogType.Error||type==LogType.Exception||type==LogType.Assert);}
    static string Cut(string s,int size){return String.IsNullOrEmpty(s)?s:s.Substring(0,Math.Min(size,s.Length));}
    void CaptureLog(string source,string level,string message,string stack,bool error){
        if(recorder==null||quitting)return;
        long second=Stopwatch.GetTimestamp()/Stopwatch.Frequency;
        lock(frameSamples){if(second!=logSecond){logSecond=second;logCount=errorCount=0;}if(error?++errorCount>8:++logCount>16){Interlocked.Increment(ref suppressed);return;}}
        var data=Json.Obj("source",Cut(source,128),"level",level,"message",Cut(message,4096),"stack",Cut(stack,8192));
        if(error){Interlocked.Exchange(ref lastError,data);Interlocked.Exchange(ref pendingError,1);}recorder.Add("log",data);
    }
    void Update(){
        if(recorder==null)return;long begin=Stopwatch.GetTimestamp();
        try{
            float now=Time.realtimeSinceStartup;double dt=Time.unscaledDeltaTime*1000;frames++;sum+=dt;max=Math.Max(max,dt);if(dt>100)slowFrames++;
            if(Keyboard.current!=null&&Keyboard.current[hotkey.Value].wasPressedThisFrame&&now-lastMark>=2){lastMark=now;ManualMark();}
            if(now>=nextSample){nextSample=now+1;var state=Snapshot(false);recorder.Add("state",state);
                var signature=Json.Text(Json.Obj("planet",state["planet"],"landed",state["landed"],"players",state["players"],"connected",state["connected"],"held",state["held"],"dead",state["dead"]));
                if(signature!=lastState){recorder.Add("state_change",state);lastState=signature;}
                string loading=Convert.ToString(state["phase"]);if(loading!=loadingState){loadingState=loading;loadingSince=now;loadingReported=false;}
                if(loading=="landing"&&now-loadingSince>120&&!loadingReported){loadingReported=true;AutoMark("loading_timeout",state);}
            }
            if(Interlocked.Exchange(ref pendingError,0)==1&&now>=nextAuto){AutoMark("program_error",Json.Obj("error",lastError,"state",Snapshot(false)));nextAuto=now+60;}
            if(now>=nextFrameReport){
                recorder.Add("performance",Json.Obj("frames",frames,"meanMs",frames==0?0:sum/frames,"maxMs",max,"over100ms",slowFrames,"monitorMainMs",ownTicks*1000/Stopwatch.Frequency,"gc0",GC.CollectionCount(0)-gc0,"managedBytes",GC.GetTotalMemory(false),"logsSuppressed",Interlocked.Read(ref suppressed),"queueDropped",recorder.Dropped,"writerFailure",recorder.Failure));
                if(now>30&&max>500&&now>=nextAuto){AutoMark("frame_stall",Json.Obj("maxMs",max,"state",Snapshot(false)));nextAuto=now+60;}
                gc0=GC.CollectionCount(0);frames=slowFrames=0;sum=max=ownTicks=0;nextFrameReport=now+2;
            }
        }catch(Exception e){recorder.Add("capture_unavailable",Json.Obj("error",e.GetType().Name,"message",Cut(e.Message,512)));}
        finally{ownTicks+=Stopwatch.GetTimestamp()-begin;}
    }
    void AutoMark(string kind,object data){recorder.Mark(kind,data);}
    void ManualMark(){
        string id=recorder.Mark("manual",Snapshot(true));
        if(id==null){Tip("记录未保存：请检查诊断磁盘空间或会话标记上限。");return;}
        StartCoroutine(Screenshot(id));
    }
    void Tip(string s){if(HUDManager.Instance!=null)HUDManager.Instance.DisplayTip("故障诊断",s,false,false,"LC_DIAGNOSTICS");}
    IEnumerator Screenshot(string id){
        yield return new WaitForEndOfFrame();
        string path=Path.Combine(recorder.DirectoryPath,"shot-"+id+".png");
        try{
            if(Directory.GetFiles(recorder.DirectoryPath,"shot-*.png").Sum(f=>new FileInfo(f).Length)>256L*1024*1024){recorder.Add("screenshot_unavailable",Json.Obj("id",id,"reason","session screenshot budget 256MB"));Tip("现场已标记；本次截图已达256MB，请退出后整理。");yield break;}
            // Native capture includes the rendered game and UI. Never capture the desktop.
            ScreenCapture.CaptureScreenshot(path,1);
            recorder.Add("screenshot_requested",Json.Obj("id",id,"frame",Time.frameCount,"width",Screen.width,"height",Screen.height));
        }catch(Exception e){recorder.Add("screenshot_failed",Json.Obj("id",id,"error",e.Message));Tip("现场已标记，截图失败。退出后可添加备注。");yield break;}
        float deadline=Time.realtimeSinceStartup+10;
        while(!File.Exists(path)&&Time.realtimeSinceStartup<deadline)yield return new WaitForSeconds(.2f);
        bool ok=File.Exists(path)&&new FileInfo(path).Length>8;
        recorder.Add(ok?"screenshot_saved":"screenshot_unavailable",Json.Obj("id",id,"file",Path.GetFileName(path)));
        Tip(ok?"现场和截图已保存。退出后到接收端添加备注。":"现场已标记；截图尚未确认，请退出后检查。");
    }
    static object Pos(Vector3 p){return new[]{Math.Round(p.x,3),Math.Round(p.y,3),Math.Round(p.z,3)};}
    static object Entity(Component c){
        if(c==null)return null;var n=c.GetComponentInParent<NetworkObject>();var g=c.GetComponentInParent<GrabbableObject>();var e=c.GetComponentInParent<EnemyAI>();var p=c.GetComponentInParent<PlayerControllerB>();var t=c.GetComponentInParent<InteractTrigger>();
        return Json.Obj("name",c.name,"type",c.GetType().FullName,"position",Pos(c.transform.position),"netId",n&&n.IsSpawned?(object)n.NetworkObjectId:null,"owner",n&&n.IsSpawned?(object)n.OwnerClientId:null,"item",g?g.itemProperties?.itemName:null,"held",g?(object)g.isHeld:null,"inShip",g?(object)g.isInShipRoom:null,"scrapValue",g?(object)g.scrapValue:null,"enemyType",e?e.GetType().FullName:null,"enemyDead",e?(object)e.isEnemyDead:null,"enemyState",e?(object)e.currentBehaviourStateIndex:null,"animState",e&&e.creatureAnimator&&e.creatureAnimator.runtimeAnimatorController?(object)e.creatureAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash:null,"playerDead",p?(object)p.isPlayerDead:null,"interactable",t?(object)t.interactable:null);
    }
    Dictionary<string,object> Snapshot(bool detail){
        var r=StartOfRound.Instance;var p=GameNetworkManager.Instance?.localPlayerController;var net=NetworkManager.Singleton;var tm=TimeOfDay.Instance;
        var peers=new List<object>();if(r!=null&&r.allPlayerScripts!=null)foreach(var player in r.allPlayerScripts.Take(32))if(player!=null&&(player.isPlayerControlled||player.isPlayerDead))peers.Add(Json.Obj("clientId",player.actualClientId,"dead",player.isPlayerDead,"inside",player.isInsideFactory,"ship",player.isInHangarShipRoom,"heldId",player.currentlyHeldObjectServer? (object)player.currentlyHeldObjectServer.NetworkObjectId:null));
        var d=Json.Obj("frame",Time.frameCount,"gameVersion",GameNetworkManager.Instance?(object)GameNetworkManager.Instance.gameVersionNum:null,"gameTime",tm?(object)tm.globalTime:null,"serverTime",net&&net.IsListening?(object)net.ServerTime.Time:null,"networkTick",net&&net.IsListening?(object)net.ServerTime.Tick:null,"role",net&&net.IsHost?"host":net&&net.IsClient?"client":"offline","clientId",net&&net.IsListening?(object)net.LocalClientId:null,"connected",net&&net.IsConnectedClient,"planet",r?.currentLevel?.PlanetName,"seed",r?(object)r.randomMapSeed:null,"landed",r?(object)r.shipHasLanded:null,"phase",!r?"menu":r.shipHasLanded?"landed":r.inShipPhase?"orbit":"landing","position",p?Pos(p.transform.position):null,"area",!p?null:p.isInsideFactory?"facility":p.isInHangarShipRoom?"ship":"outside","dead",p?(object)p.isPlayerDead:null,"held",p?Entity(p.currentlyHeldObjectServer):null,"players",peers);
        // Steam lobby identity is read reflectively so LAN and optional Steam APIs remain supported.
        try{var lobby=GameNetworkManager.Instance?.currentLobby;d["lobby"]=lobby.HasValue?HashText(lobby.Value.Id.ToString()):null;}catch{d["lobby"]=null;}
        if(detail&&p){
            RaycastHit hit;d["aim"]=p.gameplayCamera&&Physics.Raycast(p.gameplayCamera.transform.position,p.gameplayCamera.transform.forward,out hit,12,~0,QueryTriggerInteraction.Collide)?Entity(hit.collider):null;
            int count=Physics.OverlapSphereNonAlloc(p.transform.position,15,nearby,~0,QueryTriggerInteraction.Collide);var list=new List<object>();var seen=new HashSet<int>();
            for(int i=0;i<count&&list.Count<48;i++){var col=nearby[i];if(!col)continue;Component c=col.GetComponentInParent<EnemyAI>();if(!c)c=col.GetComponentInParent<GrabbableObject>();if(!c)c=col.GetComponentInParent<PlayerControllerB>();if(!c)c=col.GetComponentInParent<InteractTrigger>();if(c&&seen.Add(c.GetInstanceID()))list.Add(Entity(c));}
            d["nearby"]=list;d["nearbyColliderLimit"]=count==nearby.Length;d["nearbyObjectLimit"]=list.Count==48;d["hoverTip"]=Cut(p.cursorTip?.text,256);d["missing"]="No full-world scan; nearby radius 15m / 128 colliders / 48 entities; no animation root cause inferred; LAN association requires shared user group plus seed/serverTime.";
        }
        return d;
    }
    static string HashText(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
    IEnumerator IdentityAfterLoad(){
        yield return new WaitForSeconds(10);
        var loaded=new List<Dictionary<string,object>>();
        foreach(var info in Chainloader.PluginInfos.Values){try{var a=info.Instance.GetType().Assembly;loaded.Add(Json.Obj("guid",info.Metadata.GUID,"name",info.Metadata.Name,"version",info.Metadata.Version.ToString(),"path",info.Location,"mvid",a.ManifestModule.ModuleVersionId.ToString()));}catch(Exception e){recorder.Add("identity_unavailable",Json.Obj("error",e.Message));}}
        var assemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>Json.Obj("name",a.GetName().Name,"version",a.GetName().Version.ToString(),"mvid",a.ManifestModule.ModuleVersionId.ToString())).ToList();
        string root=Paths.GameRootPath;var target=recorder;new Thread(()=>Inventory(root,target,loaded,assemblies)){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="LGP identity"}.Start();
    }
    static void Inventory(string root,Recorder target,List<Dictionary<string,object>> loaded,List<Dictionary<string,object>> assemblies){
        try{
            var files=new List<object>();var diskPlugins=new List<Dictionary<string,object>>();var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string version=null,manifestHash=null;
            string state=Path.Combine(root,".lcpack-updater/state.json");
            if(File.Exists(state)){var raw=File.ReadAllText(state);var j=JObject.Parse(raw);version=(string)j["version"];manifestHash=HashText(raw);foreach(var f in (JArray)j["files"])expected[(string)f["path"]]=(string)f["sha256"];}
            var paths=new List<string>();foreach(string folder in new[]{"BepInEx/plugins","BepInEx/patchers","BepInEx/core","BepInEx/config"}){string full=Path.Combine(root,folder);if(Directory.Exists(full))paths.AddRange(Directory.GetFiles(full,"*",SearchOption.AllDirectories).Where(p=>p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)));}
            paths.Add(Path.Combine(root,"Lethal Company_Data/Managed/Assembly-CSharp.dll"));
            var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string path in paths){
                string rel=path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1).Replace('\\','/');string hash=null,error=null;var f=new FileInfo(path);long size=f.Exists?f.Length:0;var modified=f.Exists?f.LastWriteTimeUtc:DateTime.MinValue;
                try{using(var h=SHA256.Create())using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))hash=BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();f.Refresh();if(f.Length!=size||f.LastWriteTimeUtc!=modified){error="changed_during_hash";hash=null;}}catch(Exception e){error=e.GetType().Name;}
                string wanted;expected.TryGetValue(rel,out wanted);hashes[Path.GetFullPath(path).Replace('\\','/')]=hash;
                files.Add(Json.Obj("path",rel,"size",size,"modifiedUtc",modified.ToString("O"),"sha256",hash,"expected",wanted,"status",error??(wanted==null?"unmanaged":hash==wanted?"match":"mismatch")));
                if(rel.StartsWith("BepInEx/plugins/",StringComparison.OrdinalIgnoreCase)&&rel.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))try{
                    using(var assembly=Mono.Cecil.AssemblyDefinition.ReadAssembly(path))foreach(var type in assembly.MainModule.Types)foreach(var attr in type.CustomAttributes)if(attr.AttributeType.FullName=="BepInEx.BepInPlugin")diskPlugins.Add(Json.Obj("guid",Convert.ToString(attr.ConstructorArguments[0].Value),"name",Convert.ToString(attr.ConstructorArguments[1].Value),"version",Convert.ToString(attr.ConstructorArguments[2].Value),"path",rel,"sha256",hash));
                }catch(BadImageFormatException){}catch(Exception ex){files.Add(Json.Obj("path",rel,"status","metadata_unavailable","error",ex.GetType().Name));}
                Thread.Sleep(1);
            }
            foreach(var wanted in expected.Where(x=>(x.Key.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||x.Key.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase))&&!File.Exists(Path.Combine(root,x.Key))))files.Add(Json.Obj("path",wanted.Key,"expected",wanted.Value,"sha256",null,"status","missing"));
            foreach(var m in loaded){string path=(string)m["path"];string hash;hashes.TryGetValue(Path.GetFullPath(path).Replace('\\','/'),out hash);m["sha256"]=hash;m["path"]=path.StartsWith(root,StringComparison.OrdinalIgnoreCase)?path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1).Replace('\\','/'):"outside-game/"+Path.GetFileName(path);}
            var duplicateGuids=diskPlugins.GroupBy(d=>(string)d["guid"]).Where(g=>g.Count()>1).Select(g=>Json.Obj("guid",g.Key,"paths",g.Select(d=>d["path"]).ToArray())).ToArray();
            Json.Atomic(Path.Combine(target.DirectoryPath,"identity.json"),Json.Obj("schema",1,"capturedUtc",DateTime.UtcNow.ToString("O"),"packVersionClaim",version,"updaterStateSha256",manifestHash,"loadedPlugins",loaded,"loadedAssemblies",assemblies,"diskPlugins",diskPlugins,"duplicateDiskGuids",duplicateGuids,"files",files,"limits","SHA256 is actual disk content sampled once at startup; MVID identifies loaded assemblies, not runtime patched IL. Asset bundles reuse updater claims only; config values deliberately omitted. Duplicate disk files are evidence, not automatically a duplicate loaded plugin."));
            target.Add("identity_complete",Json.Obj("loadedPlugins",loaded.Count,"files",files.Count));
        }catch(Exception e){target.Add("identity_failed",Json.Obj("error",e.Message));}
    }
    void OnApplicationQuit(){Close();}
    void OnDestroy(){if(!quitting)Close();}
    void Close(){if(quitting)return;quitting=true;if(recorder==null)return;BepInEx.Logging.Logger.Listeners.Remove(this);Application.logMessageReceivedThreaded-=UnityLog;SceneManager.sceneLoaded-=Loaded;SceneManager.sceneUnloaded-=Unloaded;recorder.Add("session_end",Json.Obj("utc",DateTime.UtcNow.ToString("O")));recorder.Dispose();}
    void IDisposable.Dispose(){}
}
}
