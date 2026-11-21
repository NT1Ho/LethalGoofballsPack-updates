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
[BepInPlugin("nt1ho.lethaldiagnostics","LethalGoofballs Diagnostics",NetworkEvidence.Version)]
public sealed class Plugin : BaseUnityPlugin, ILogListener {
    readonly FrameTiming[] timing = new FrameTiming[1];
    Process cpu;
    double lastCpuMs, lastWall;
    long captureTicks,lastWriterTicks;
    float nextStallEvidence;
    static Plugin current;
    NetworkEvidence coop;
    GameEvidence native;
    Action<string,string,string,string,string,string,bool> bridge;
    readonly Dictionary<(string Source,string Message),Repeat> repeats=new Dictionary<(string,string),Repeat>();
    sealed class Repeat { public long Last;public int Extra;public string Stack; }
    string lastGrab="";
    long errorSecond;
    int errorsThisSecond,errorsSuppressed;
    internal static void Native(string category,string code,string status,string link,string detail){
        try{current?.coop?.Emit("LethalCompany",category,code,status,link,detail,true);}catch(Exception){}
    }
    static void BeforeControllerSet(Animator __instance, RuntimeAnimatorController __0) {
        if (__0 || !__instance || !current || current.quitting || __instance.name!="metarig") return;
        var m=__instance.GetComponentInParent<MaskedPlayerEnemy>();
        if (!m || m.creatureAnimator!=__instance || !__instance.runtimeAnimatorController) return;
        current.recorder.Add("masked_controller_clear",Json.Obj("netId",m.NetworkObjectId,"dead",m.isEnemyDead,
            "previous",__instance.runtimeAnimatorController.name,"caller",Cut(new StackTrace(1,false).ToString(),1800)));
    }
    static void StageStart(out long __state){__state=Stopwatch.GetTimestamp();}
    static void StageEnd(long __state,MethodBase __originalMethod){
        double ms=(Stopwatch.GetTimestamp()-__state)*1000.0/Stopwatch.Frequency;
        if(ms>=30 && current && !current.quitting) current.recorder.Add("slow_generation_stage",Json.Obj("method",__originalMethod.Name,"wallMs",ms,"frame",Time.frameCount));
    }
    static void MaskEvent(Component __instance, MethodBase __originalMethod){
        if(!current || current.quitting || !__instance)return;
        var m=__instance.GetComponentInParent<MaskedPlayerEnemy>();if(!m)return;
        var rig=m.creatureAnimator;
        current.recorder.Add("masked_animation_event",Json.Obj("event",__originalMethod.Name,"frame",Time.frameCount,"netId",m.NetworkObjectId,"dead",m.isEnemyDead,"enemyState",m.currentBehaviourStateIndex,"position",Pos(m.transform.position),"animator",rig?rig.name:null,"controller",rig&&rig.runtimeAnimatorController?rig.runtimeAnimatorController.name:null,"stateHash",rig&&rig.runtimeAnimatorController?(object)rig.GetCurrentAnimatorStateInfo(0).fullPathHash:null));
    }
    sealed class MaskBinding { public int id; public string name; public bool sampled; }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<MaskedPlayerEnemy,MaskBinding> masks =
        new System.Runtime.CompilerServices.ConditionalWeakTable<MaskedPlayerEnemy,MaskBinding>();
    void SampleMaskBindings(){
        var enemies=RoundManager.Instance?.SpawnedEnemies;
        if(enemies==null)return;
        for(int i=0;i<enemies.Count && i<64;i++){
            var m=enemies[i] as MaskedPlayerEnemy;if(!m)continue;
            var rig=m.creatureAnimator;var ctrl=rig?rig.runtimeAnimatorController:null;
            int id=ctrl?ctrl.GetInstanceID():0;var prev=masks.GetOrCreateValue(m);
            if(prev.sampled && prev.id==id)continue;
            recorder.Add("masked_binding",Json.Obj("netId",m.NetworkObjectId,"frame",Time.frameCount,"first",!prev.sampled,"previous",prev.name,
                "current",ctrl?ctrl.name:null,"animator",rig?rig.name:null,"dead",m.isEnemyDead,"position",Pos(m.transform.position)));
            prev.sampled=true;prev.id=id;prev.name=ctrl?ctrl.name:null;
        }
    }
    void ObserveStages(){
        var hooks=new HarmonyLib.Harmony("nt1ho.diagnostics.masked");
        foreach(var name in new[]{"SpawnScrapInLevel","SpawnSyncedProps","GenerateNewFloor","SetLevelObjectVariables","FinishGeneratingNewLevelClientRpc"}){
            var method=HarmonyLib.AccessTools.DeclaredMethod(typeof(RoundManager),name);
            if(method!=null)hooks.Patch(method,new HarmonyLib.HarmonyMethod(typeof(Plugin),nameof(StageStart)),new HarmonyLib.HarmonyMethod(typeof(Plugin),nameof(StageEnd)));
        }
        var emote=HarmonyLib.AccessTools.TypeByName("TooManyEmotes.EmoteControllerMaskedEnemy");
        foreach(var name in new[]{"Start","HitEnemy","KillEnemy","PerformEmote","StopPerformingEmote"}){
            var type=name.EndsWith("Emote")?emote:typeof(MaskedPlayerEnemy);
            var method=type==null?null:HarmonyLib.AccessTools.DeclaredMethod(type,name);
            if(method!=null)hooks.Patch(method,postfix:new HarmonyLib.HarmonyMethod(typeof(Plugin),nameof(MaskEvent)));
        }
    }
    readonly FrameDrop frameDrop=new FrameDrop();float nextDropMark;
    Recorder recorder;ConfigEntry<bool> monitor;ConfigEntry<Key> hotkey;
    float nextSample,nextFrameReport,lastMark=-10,nextAuto;int frames,slowFrames,gc0;double sum,max,ownTicks;long logSecond;int logCount;long suppressed;object lastError;
    int pendingError;bool quitting;string lastState="",loadingState="";float loadingSince;bool loadingReported;
    readonly Collider[] nearby=new Collider[128];readonly Queue<double> frameSamples=new Queue<double>();
    public LogLevel LogLevelFilter {get{return LogLevel.All;}}
    void Awake(){
        monitor=Config.Bind("General","Enabled",true,"Continuous local diagnostics. Does not change gameplay.");
        var share=Config.Bind("General","ShareWithHost",true,"Share bounded game events, MOD inventory and sanitized important exceptions with the current compatible diagnostic host. No guest screenshots or chat. Set false to keep local diagnostics.");
        hotkey=Config.Bind("General","MarkKey",Key.F8,"Manual incident and screenshot. Change if another MOD uses F8.");
        if(!monitor.Value){enabled=false;return;}
        try{
            var process=cpu=Process.GetCurrentProcess(); lastCpuMs=cpu.TotalProcessorTime.TotalMilliseconds;lastWall=Time.realtimeSinceStartupAsDouble;
            recorder=new Recorder(Path.Combine(Paths.GameRootPath,".lcpack-diagnostics"),Json.Obj("schema",2,"plugin",NetworkEvidence.Version,"utc",DateTime.UtcNow.ToString("O"),"pid",process.Id,"processStartUtc",process.StartTime.ToUniversalTime().ToString("O"),"unity",Application.unityVersion,"application",Application.version,"cpu",SystemInfo.processorType,"logicalCores",SystemInfo.processorCount,"ramMB",SystemInfo.systemMemorySize,"gpu",SystemInfo.graphicsDeviceName,"vramMB",SystemInfo.graphicsMemorySize,"os",SystemInfo.operatingSystem,"privacy","screenshots and full logs local; redacted key evidence can go to the current compatible host; no chat or configuration values","preSeconds",60,"postSeconds",30));
            BepInEx.Logging.Logger.Listeners.Add(this);Application.logMessageReceivedThreaded+=UnityLog;
            current=this;
            coop=new NetworkEvidence(recorder,share.Value);
            bridge=coop.Emit;
            AppDomain.CurrentDomain.SetData(NetworkEvidence.Bridge,bridge);
            try{native=new GameEvidence();}catch(Exception error){recorder.AddImportant("coop_fault",Json.Obj("type","native_observer","detail",error.GetType().Name));}
            try{new HarmonyLib.Harmony("nt1ho.diagnostics.masked").Patch(HarmonyLib.AccessTools.PropertySetter(typeof(Animator),"runtimeAnimatorController"),prefix:new HarmonyLib.HarmonyMethod(typeof(Plugin),nameof(BeforeControllerSet)));}catch(Exception ex){Logger.LogWarning("Masked controller write observer unavailable: "+ex.GetType().Name);}
            try{ObserveStages();}catch(Exception ex){Logger.LogWarning("Stage observer unavailable: "+ex.Message);}
            SceneManager.sceneLoaded+=Loaded;SceneManager.sceneUnloaded+=Unloaded;StartCoroutine(IdentityAfterLoad());
        }catch(Exception e){Logger.LogError("Diagnostics unavailable: "+e.Message);enabled=false;}
    }
    void Loaded(Scene s,LoadSceneMode mode){recorder.Add("scene_loaded",Json.Obj("scene",s.name,"mode",mode.ToString()));coop?.Emit("Unity","scene","scene_loaded","observed","",s.name,false);}
    void Unloaded(Scene s){recorder.Add("scene_unloaded",Json.Obj("scene",s.name));}
    public void LogEvent(object sender,LogEventArgs e){
        if(e.Source.SourceName=="Unity Log")return;
        long start=Stopwatch.GetTimestamp();
        try{CaptureLog(e.Source.SourceName,e.Level.ToString(),e.Data==null?"":e.Data.ToString(),"",(e.Level&(LogLevel.Error|LogLevel.Fatal))!=0);}
        catch(Exception){}
        finally{Interlocked.Add(ref captureTicks,Stopwatch.GetTimestamp()-start);}
    }
    void UnityLog(string message,string stack,LogType type){
        long start=Stopwatch.GetTimestamp();
        try{CaptureLog("Unity",type.ToString(),message,stack,type==LogType.Error||type==LogType.Exception||type==LogType.Assert);}
        catch(Exception){}
        finally{Interlocked.Add(ref captureTicks,Stopwatch.GetTimestamp()-start);}
    }
    static string Cut(string s,int size){return String.IsNullOrEmpty(s)?s:s.Substring(0,Math.Min(size,s.Length));}
    void CaptureLog(string source,string level,string message,string stack,bool error){
        if(recorder==null||quitting||NetworkEvidence.PrivateLog(source,message))return;
        long second=Stopwatch.GetTimestamp()/Stopwatch.Frequency;
        if(!error && (source=="Hatchet"||source=="Short Hatchet") &&
            (message.StartsWith("FEEDBACK ",StringComparison.Ordinal)||message.StartsWith("SFX ",StringComparison.Ordinal)))
            coop?.Emit("NT1Ho.Hatchet",message.StartsWith("SFX ",StringComparison.Ordinal)?"audio":"effects","native_trace","observed","",message,false);
        if(error){
            string key=NetworkEvidence.Clean(message,1024);
            lock(repeats){
                if(repeats.TryGetValue((source,key),out var prior)&&second-prior.Last<10){
                    prior.Extra++;Interlocked.Increment(ref suppressed);return;
                }
                if(second!=errorSecond){errorSecond=second;errorsThisSecond=0;}
                if(++errorsThisSecond>8){errorsSuppressed++;Interlocked.Increment(ref suppressed);return;}
                if(repeats.Count<128||prior!=null)repeats[(source,key)]=new Repeat{Last=second,Extra=prior?.Extra??0,Stack=Cut(stack,1800)};
            }
            var data=Json.Obj("source",NetworkEvidence.Clean(source,128),"level",level,"message",NetworkEvidence.Clean(message,4096),"stack",NetworkEvidence.Clean(stack,8192));
            recorder.AddImportant("log",data);
            Interlocked.Exchange(ref lastError,data);Interlocked.Exchange(ref pendingError,1);
            coop?.Emit(source,"exception","runtime_error","error","error:"+HashText(source+"\n"+key).Substring(0,16),Cut(key,370)+"\n"+Cut(stack,500),true);
            return;
        }
        lock(frameSamples){if(second!=logSecond){logSecond=second;logCount=0;}if(++logCount>16){Interlocked.Increment(ref suppressed);return;}}
        recorder.Add("log",Json.Obj("source",Cut(source,128),"level",level,"message",NetworkEvidence.Clean(message,4096),"stack",NetworkEvidence.Clean(stack,8192)));
    }
    void SummarizeErrors(){
        var now=Stopwatch.GetTimestamp()/Stopwatch.Frequency;
        lock(repeats){
            if(errorsSuppressed>0){
                coop?.Emit("BepInEx","exception","distinct_errors_bounded","missing","","unique diagnostics suppressed="+errorsSuppressed,true);
                errorsSuppressed=0;
            }
            foreach(var entry in repeats.Where(e=>now-e.Value.Last>=10).ToArray()){
                var r=entry.Value;
                if(r.Extra>0)coop?.Emit(entry.Key.Source,"exception","error_repeats","aggregated","error:"+HashText(entry.Key.Source+"\n"+entry.Key.Message).Substring(0,16),
                    "repeated="+r.Extra+" | "+entry.Key.Message+"\n"+r.Stack,true);
                repeats.Remove(entry.Key);
            }
        }
    }
    void Update(){
        if(recorder==null)return;long begin=Stopwatch.GetTimestamp();
        try{
            coop.Tick();float now=Time.realtimeSinceStartup;double dt=Time.unscaledDeltaTime*1000;frames++;sum+=dt;max=Math.Max(max,dt);if(dt>100)slowFrames++;
            if(Keyboard.current!=null&&Keyboard.current[hotkey.Value].wasPressedThisFrame&&now-lastMark>=2){lastMark=now;ManualMark();}
            if(now>=nextSample){nextSample=now+1;SampleMaskBindings();var state=Snapshot(false);recorder.Add("state",state);
                SummarizeErrors();
                var player=GameNetworkManager.Instance?.localPlayerController;
                var enemy=player?player.inAnimationWithEnemy:null;
                string grab=enemy?enemy.NetworkObjectId+" "+enemy.GetType().FullName:"";
                if(lastGrab!=grab){
                    Native("grab",grab.Length==0?"grab_released":"grab_observed","observed",player?"player:"+player.NetworkObjectId:"",grab.Length==0?lastGrab:grab);
                    lastGrab=grab;
                }
                var signature=Json.Text(Json.Obj("planet",state["planet"],"landed",state["landed"],"players",state["players"],"connected",state["connected"],"held",state["held"],"dead",state["dead"]));
                if(signature!=lastState){recorder.Add("state_change",state);lastState=signature;coop.Emit("LethalCompany","state","local_state_change","observed","","map="+state["planet"]+" phase="+state["phase"]+" item="+Json.Text(state["held"])+" dead="+state["dead"],false);}
                string loading=Convert.ToString(state["phase"]);if(loading!=loadingState){loadingState=loading;loadingSince=now;loadingReported=false;}
                if(loading=="landing"&&now-loadingSince>120&&!loadingReported){loadingReported=true;AutoMark("loading_timeout",state);}
            }
            if(Interlocked.Exchange(ref pendingError,0)==1&&now>=nextAuto){AutoMark("program_error",Json.Obj("error",lastError,"state",Snapshot(false)));nextAuto=now+60;}
            if(dt>=500 && now>=nextStallEvidence){nextStallEvidence=now+10;recorder.Add("long_frame",Json.Obj("frameMs",dt,"secondsSinceManual",lastMark>=0?(object)(now-lastMark):null,"state",Snapshot(false)));}
            if(now>=nextFrameReport){
                double cpuMs=cpu.TotalProcessorTime.TotalMilliseconds, wall=Time.realtimeSinceStartupAsDouble;
                var n=FrameTimingManager.GetLatestTimings(1,timing);FrameTimingManager.CaptureFrameTimings();
                long writerTicks=Interlocked.Read(ref recorder.WriterActiveTicks);
                recorder.Add("performance",Json.Obj("sampleSeconds",wall-lastWall,"logCallbackMs",Interlocked.Exchange(ref captureTicks,0)*1000.0/Stopwatch.Frequency,"writerActiveMs",(writerTicks-lastWriterTicks)*1000.0/Stopwatch.Frequency,"cpuCores",(cpuMs-lastCpuMs)/Math.Max(1,(wall-lastWall)*1000),"unityGpuMs",n>0 && timing[0].gpuFrameTime>0?(object)timing[0].gpuFrameTime:null,"unityCpuMs",n>0 && timing[0].cpuFrameTime>0?(object)timing[0].cpuFrameTime:null,"frames",frames,"meanMs",frames==0?0:sum/frames,"maxMs",max,"over100ms",slowFrames,"monitorMainMs",ownTicks*1000/Stopwatch.Frequency,"gc0",GC.CollectionCount(0)-gc0,"managedBytes",GC.GetTotalMemory(false),"logsSuppressed",Interlocked.Read(ref suppressed),"queueDropped",recorder.Dropped,"importantDropped",recorder.DroppedImportant,"multiplayer",coop.Metrics,"writerFailure",recorder.Failure,"focused",Application.isFocused,"targetFps",Application.targetFrameRate,"vSync",QualitySettings.vSyncCount));
                lastCpuMs=cpuMs;lastWall=wall;lastWriterTicks=writerTicks;
                var round=StartOfRound.Instance;
                if(!round||!round.shipHasLanded||!Application.isFocused)frameDrop.Reset();
                else if(frameDrop.Observe(frames,sum,max,now>=nextDropMark)){
                    AutoMark("sustained_frame_drop",Json.Obj("meanMs",sum/frames,"referenceMs",frameDrop.ReferenceMs,"windows",3,"state",Snapshot(false)));
                    nextDropMark=now+60;
                }
                if(now>30&&max>500&&now>=nextAuto){AutoMark("frame_stall",Json.Obj("maxMs",max,"state",Snapshot(false)));nextAuto=now+60;}
                gc0=GC.CollectionCount(0);frames=slowFrames=0;sum=max=ownTicks=0;nextFrameReport=now+2;
            }
        }catch(Exception e){recorder.Add("capture_unavailable",Json.Obj("error",e.GetType().Name,"message",Cut(e.Message,512)));}
        finally{ownTicks+=Stopwatch.GetTimestamp()-begin;}
    }
    void AutoMark(string kind,object data){recorder.Mark(kind,data);}
    void ManualMark(){
        if(coop.IsGuest)return;string id=recorder.Mark("manual",Snapshot(true));
        if(id==null){Tip("记录未保存：请检查诊断磁盘空间或会话标记上限。");return;}
        coop.Mark(id);StartCoroutine(Screenshot(id));
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
        return Json.Obj("name",c.name,"type",c.GetType().FullName,"position",Pos(c.transform.position),"netId",n&&n.IsSpawned?(object)n.NetworkObjectId:null,"owner",n&&n.IsSpawned?(object)n.OwnerClientId:null,"item",g?g.itemProperties?.itemName:null,"held",g?(object)g.isHeld:null,"inShip",g?(object)g.isInShipRoom:null,"scrapValue",g?(object)g.scrapValue:null,"enemyType",e?e.GetType().FullName:null,"enemyDead",e?(object)e.isEnemyDead:null,"enemyState",e?(object)e.currentBehaviourStateIndex:null,"animator",e&&e.creatureAnimator?e.creatureAnimator.name:null,"controller",e&&e.creatureAnimator&&e.creatureAnimator.runtimeAnimatorController?e.creatureAnimator.runtimeAnimatorController.name:null,"animState",e&&e.creatureAnimator&&e.creatureAnimator.runtimeAnimatorController?(object)e.creatureAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash:null,"playerDead",p?(object)p.isPlayerDead:null,"interactable",t?(object)t.interactable:null);
    }
    Dictionary<string,object> Snapshot(bool detail){
        var r=StartOfRound.Instance;var p=GameNetworkManager.Instance?.localPlayerController;var net=NetworkManager.Singleton;var tm=TimeOfDay.Instance;
        var peers=new List<object>();if(r!=null&&r.allPlayerScripts!=null)foreach(var player in r.allPlayerScripts.Take(32))if(player!=null&&(player.isPlayerControlled||player.isPlayerDead))peers.Add(Json.Obj("clientId",player.actualClientId,"dead",player.isPlayerDead,"inside",player.isInsideFactory,"ship",player.isInHangarShipRoom,"heldId",player.currentlyHeldObjectServer? (object)player.currentlyHeldObjectServer.NetworkObjectId:null));
        var d=Json.Obj("frame",Time.frameCount,"gameVersion",GameNetworkManager.Instance?(object)GameNetworkManager.Instance.gameVersionNum:null,"gameTime",tm?(object)tm.globalTime:null,"serverTime",net&&net.IsListening?(object)net.ServerTime.Time:null,"networkTick",net&&net.IsListening?(object)net.ServerTime.Tick:null,"role",net&&net.IsHost?"host":net&&net.IsClient?"client":"offline","clientId",net&&net.IsListening?(object)net.LocalClientId:null,"connected",net&&net.IsConnectedClient,"planet",r?.currentLevel?.PlanetName,"seed",r?(object)r.randomMapSeed:null,"landed",r?(object)r.shipHasLanded:null,"phase",!r?"menu":r.shipHasLanded?"landed":r.inShipPhase?"orbit":"landing","position",p?Pos(p.transform.position):null,"area",!p?null:p.isInsideFactory?"facility":p.isInHangarShipRoom?"ship":"outside","dead",p?(object)p.isPlayerDead:null,"held",p?Entity(p.currentlyHeldObjectServer):null,"players",peers);
        if(p){
            d["audioTrigger"]=p.currentAudioTrigger?p.currentAudioTrigger.name:null;
            d["viewForward"]=p.gameplayCamera?Pos(p.gameplayCamera.transform.forward):null;
        }
        // Steam lobby identity is read reflectively so LAN and optional Steam APIs remain supported.
        try{var lobby=GameNetworkManager.Instance?.currentLobby;d["lobby"]=lobby.HasValue?HashText(lobby.Value.Id.ToString()):null;}catch{d["lobby"]=null;}
        if(detail&&p){
            // F8 only. Bounds intersections are candidates, not evidence that a callback ran.
            var body=p.GetComponent<CharacterController>();
            if(body){
                var bounds=body.bounds;
                int touches=Physics.OverlapBoxNonAlloc(bounds.center,bounds.extents,nearby,Quaternion.identity,~0,QueryTriggerInteraction.Collide);
                var zones=new List<object>();
                for(int i=0;i<touches;i++){
                    var trigger=nearby[i].GetComponent<AudioReverbTrigger>();
                    if(trigger)zones.Add(Json.Obj("trigger",trigger.name,"parent",trigger.transform.parent?trigger.transform.parent.name:null,"shipRoom",trigger.isShipRoom));
                }
                d["reverbNearBodyBounds"]=zones;d["reverbQueryAtLimit"]=touches==nearby.Length;
            }
            RaycastHit hit;d["aim"]=p.gameplayCamera&&Physics.Raycast(p.gameplayCamera.transform.position,p.gameplayCamera.transform.forward,out hit,12,~0,QueryTriggerInteraction.Collide)?Entity(hit.collider):null;
            int count=Physics.OverlapSphereNonAlloc(p.transform.position,15,nearby,~0,QueryTriggerInteraction.Collide);var list=new List<object>();var seen=new HashSet<int>();
            for(int i=0;i<count&&list.Count<48;i++){var col=nearby[i];if(!col)continue;Component c=col.GetComponentInParent<EnemyAI>();if(!c)c=col.GetComponentInParent<GrabbableObject>();if(!c)c=col.GetComponentInParent<PlayerControllerB>();if(!c)c=col.GetComponentInParent<InteractTrigger>();if(c&&seen.Add(c.GetInstanceID()))list.Add(Entity(c));}
            d["nearby"]=list;d["nearbyColliderLimit"]=count==nearby.Length;d["nearbyObjectLimit"]=list.Count==48;d["hoverTip"]=Cut(p.cursorTip?.text,256);d["missing"]="No full-world scan; nearby radius 15m / 128 colliders / 48 entities; no animation root cause inferred; Room ID and server time associate multiplayer evidence when both peers support sharing.";
        }
        return d;
    }
    static string HashText(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
    IEnumerator IdentityAfterLoad(){
        yield return new WaitForSeconds(10);
        var loaded=new List<Dictionary<string,object>>();
        foreach(var info in Chainloader.PluginInfos.Values){try{var a=info.Instance.GetType().Assembly;loaded.Add(Json.Obj("guid",info.Metadata.GUID,"name",info.Metadata.Name,"version",info.Metadata.Version.ToString(),"path",info.Location,"mvid",a.ManifestModule.ModuleVersionId.ToString(),"dependencies",info.Dependencies.Select(dep=>Json.Obj("guid",dep.DependencyGUID,"min",dep.MinimumVersion?.ToString(),"flags",dep.Flags.ToString())).ToArray(),"loaded",true));}catch(Exception e){recorder.Add("identity_unavailable",Json.Obj("error",e.Message));}}
        var assemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>Json.Obj("name",a.GetName().Name,"version",a.GetName().Version.ToString(),"mvid",a.ManifestModule.ModuleVersionId.ToString())).ToList();
        string root=Paths.GameRootPath;var target=recorder;var transport=coop;new Thread(()=>Inventory(root,target,loaded,assemblies,transport)){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="LGP identity"}.Start();
    }
    static void Inventory(string root,Recorder target,List<Dictionary<string,object>> loaded,List<Dictionary<string,object>> assemblies,NetworkEvidence transport){
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
            var loadedIds=new HashSet<string>(loaded.Select(x=>(string)x["guid"]),StringComparer.OrdinalIgnoreCase);foreach(var plugin in diskPlugins)plugin["loaded"]=loadedIds.Contains((string)plugin["guid"]);var duplicateGuids=diskPlugins.GroupBy(d=>(string)d["guid"]).Where(g=>g.Count()>1).Select(g=>Json.Obj("guid",g.Key,"paths",g.Select(d=>d["path"]).ToArray())).ToArray();
            Json.Atomic(Path.Combine(target.DirectoryPath,"identity.json"),Json.Obj("schema",1,"capturedUtc",DateTime.UtcNow.ToString("O"),"packVersionClaim",version,"updaterStateSha256",manifestHash,"updaterExpectedFiles",expected.Select(f=>Json.Obj("path",f.Key,"sha256",f.Value)).ToArray(),"loadedPlugins",loaded,"loadedAssemblies",assemblies,"diskPlugins",diskPlugins,"duplicateDiskGuids",duplicateGuids,"files",files,"limits","SHA256 is actual disk content sampled once at startup; MVID identifies loaded assemblies, not runtime patched IL. updaterExpectedFiles preserves the whole updater manifest as claims, not freshly verified asset hashes; config values deliberately omitted. Duplicate disk files are evidence, not automatically a duplicate loaded plugin."));
            var startup=new List<string>();
            string log=Path.Combine(root,"BepInEx/LogOutput.log");
            if(File.Exists(log))
            using(var input=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var reader=new StreamReader(input)){
                for(int i=0;i<640&&startup.Count<32;i++){
                    string line=reader.ReadLine();
                    if(line==null||line.IndexOf("Chainloader startup complete",StringComparison.Ordinal)>=0)break;
                    if(line.IndexOf("BepInEx]",StringComparison.Ordinal)<0)continue;
                    if(line.IndexOf("Could not load",StringComparison.OrdinalIgnoreCase)<0&&
                        line.IndexOf("Missing dependencies",StringComparison.OrdinalIgnoreCase)<0&&
                        line.IndexOf("Skipping",StringComparison.OrdinalIgnoreCase)<0)continue;
                    if(!NetworkEvidence.PrivateLog("BepInEx",line))startup.Add(NetworkEvidence.Clean(line,475));
                }
            }
            foreach(var skipped in diskPlugins.Where(d=>Equals(d["loaded"],false)).Take(48))
                startup.Add($"present on disk but not loaded: {skipped["guid"]} {skipped["version"]}");
            foreach(string issue in startup)target.AddImportant("startup_issue",Json.Obj("source","BepInEx","detail",issue));
            transport.SetStartupIssues(startup.ToArray());
            target.Add("identity_complete",Json.Obj("loadedPlugins",loaded.Count,"files",files.Count));
        }catch(Exception e){target.Add("identity_failed",Json.Obj("error",e.Message));}
    }
    void OnApplicationQuit(){Close();}
    void OnDestroy(){if(!quitting)Close();}
    void Close(){if(quitting)return;quitting=true;if(ReferenceEquals(AppDomain.CurrentDomain.GetData(NetworkEvidence.Bridge),bridge))AppDomain.CurrentDomain.SetData(NetworkEvidence.Bridge,null);native?.Stop();SummarizeErrors();coop?.Dispose();if(current==this){new HarmonyLib.Harmony("nt1ho.diagnostics.masked").UnpatchSelf();current=null;}cpu?.Dispose();if(recorder==null)return;BepInEx.Logging.Logger.Listeners.Remove(this);Application.logMessageReceivedThreaded-=UnityLog;SceneManager.sceneLoaded-=Loaded;SceneManager.sceneUnloaded-=Unloaded;recorder.Add("session_end",Json.Obj("utc",DateTime.UtcNow.ToString("O")));recorder.Dispose();}
    void IDisposable.Dispose(){}
}
}
