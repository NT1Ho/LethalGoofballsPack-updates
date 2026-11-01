using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("NT1Ho.Wesley.IsolatedQA","Wesley isolated map acceptance","1.0.0")]
public sealed partial class WesleyProbe : BaseUnityPlugin
{
    [Serializable] public class Request { public string id,op,arg; public int seed; }
    [Serializable] public class Result {
        public string id, op, utc, message, scene, flow, level, saveFile;
        public bool ok, host, landed, orbit, inside, seedFixture;
        public int seed;
        public string[] levels, flows, players, exits, items, tapes, loaders, routes, slots;
    }
    private string root;
    private float nextPoll;
    private bool busy;
    private void Awake(){
        if(!File.Exists(Path.Combine(Paths.GameRootPath,"UNIVERSAL_WRENCH_ISOLATED_TEST")))
            throw new InvalidOperationException("isolated game marker required");
        root=Path.Combine(Paths.GameRootPath,"wesley-qa");
        Directory.CreateDirectory(root);
        new HarmonyLib.Harmony("NT1Ho.Wesley.IsolatedQA").PatchAll(typeof(WesleyProbe).Assembly);
    }
    private void Update(){
        if(busy||Time.unscaledTime<nextPoll||!StartOfRound.Instance||!GameNetworkManager.Instance.localPlayerController)return;
        nextPoll=Time.unscaledTime+.25f;
        var path=Path.Combine(root,"request.json");
        if(!File.Exists(path))return;
        var req=JsonUtility.FromJson<Request>(File.ReadAllText(path));
        File.Delete(path);
        if(!Guid.TryParseExact(req.id,"N",out _))return;
        busy=true;
        StartCoroutine(Execute(req));
    }
    private IEnumerator Execute(Request req){
        var r=new Result{id=req.id,op=req.op};
        var work=Act(req,r);
        while(true){
            object current;
            try{if(!work.MoveNext()){r.ok=true;break;}current=work.Current;}
            catch(Exception e){r.message=e.Message;break;}
            yield return current;
        }
        if(req.op=="land")ForcedFlow=null;
        try{Snapshot(r);}catch(Exception e){r.ok=false;r.message+=" snapshot: "+e.Message;}
        var result=Path.Combine(root,req.id+".json");
        File.WriteAllText(result+".tmp",JsonUtility.ToJson(r,true));File.Move(result+".tmp",result);busy=false;
    }
    private static void Demand(bool yes,string error){if(!yes)throw new InvalidOperationException(error);}
    private static void Snapshot(Result r){
        var round=StartOfRound.Instance; var gen=RoundManager.Instance;
        r.utc=DateTime.UtcNow.ToString("o");r.host=round.IsHost;r.landed=round.shipHasLanded;r.orbit=round.inShipPhase;
        r.level=round.currentLevel.PlanetName;r.scene=round.currentLevel.sceneName;r.seed=round.randomMapSeed;
        r.flow=gen.dungeonGenerator?.Generator?.DungeonFlow?.name;r.inside=GameNetworkManager.Instance.localPlayerController.isInsideFactory;
        r.saveFile=new ES3Settings(GameNetworkManager.Instance.currentSaveFileName).FullPath;
        r.players=round.allPlayerScripts.Where(p=>p&&p.isPlayerControlled).Select(p=>$"{p.actualClientId}|{p.isInsideFactory}|{p.isPlayerDead}|{p.transform.position:F1}").ToArray();
        r.exits=FindObjectsOfType<EntranceTeleport>().Select(e=>$"{e.entranceId}|{e.isEntranceToBuilding}|{e.entrancePoint?.position:F1}").ToArray();
    }
    private IEnumerator Act(Request req,Result r){
        var round=StartOfRound.Instance; var p=GameNetworkManager.Instance.localPlayerController;
        var isolated=Path.GetFullPath(Path.Combine(Paths.GameRootPath,"uw-test"))+Path.DirectorySeparatorChar;
        Demand(Path.GetFullPath(new ES3Settings(GameNetworkManager.Instance.currentSaveFileName).FullPath).StartsWith(isolated,StringComparison.OrdinalIgnoreCase),"ES3 path outside designated fixture");
        var until=Time.realtimeSinceStartup+135;
        switch(req.op){
            case "story": InspectStory(r);break;
            case "tapegrab":
            case "tapesubmit":
            case "save":
                var story=Story(req,r);
                while(story.MoveNext()) yield return story.Current;
                InspectStory(r);break;
            case "wrench":
                var wrench=TestApparatus(req,r);while(wrench.MoveNext())yield return wrench.Current;
                break;
            case "state":break;
            case "readui":{
                var term=FindObjectOfType<Terminal>();
                var nodes=term.logEntryFiles.Concat(term.enemyFiles).Where(n=>n!=null).ToArray();
                var node=nodes.FirstOrDefault(n=>n.name.IndexOf(req.arg,StringComparison.OrdinalIgnoreCase)>=0 || (!string.IsNullOrEmpty(n.creatureName)&&n.creatureName.IndexOf(req.arg,StringComparison.OrdinalIgnoreCase)>=0));
                Demand(node!=null,"no matching live terminal node");
                term.BeginUsingTerminal();
                term.LoadNewNode(node);
                yield return new WaitForSecondsRealtime(1.5f);
                var mesh=term.screenText.textComponent;
                mesh.ForceMeshUpdate();
                r.message=node.name+" | "+mesh.font.name+" | lines="+mesh.textInfo.lineCount+" | "+term.screenText.text;
                term.QuitTerminal();break;
            }
            case "catalog":
                r.levels=round.levels.Select(m=>$"{m.levelID}|{m.PlanetName}|{m.sceneName}").ToArray();
                r.flows=RoundManager.Instance.dungeonFlowTypes.Select(m=>m.dungeonFlow?.name).ToArray();
                r.items=round.allItemsList.itemsList.Where(i=>i&&i.spawnPrefab).Select(i=>$"{i.name}|{i.itemName}|{i.spawnPrefab.name}|{i.isScrap}").ToArray();break;
            case "terminal":
                var terminal=FindObjectOfType<Terminal>();
                terminal.terminalInUse=true;
                try {terminal.screenText.SetTextWithoutNotify(req.arg);terminal.textAdded=req.arg.Length;terminal.OnSubmit();r.message=terminal.screenText.text;}
                finally {terminal.terminalInUse=false;} break;
            case "land":
                Demand(round.IsHost&&round.inShipPhase,"host must be in orbit");
                var fixture=req.arg.Split('|');
                var chosen=round.levels.Single(m=>m.sceneName==fixture[0]);
                ForcedFlow=fixture.Length==2 ? LethalLevelLoader.PatchedContent.ExtendedDungeonFlows.Single(f=>f.DungeonFlow.name==fixture[1]) : null;
                // This routes by native RPC to test map content. It is never evidence of a story unlock.
                if(chosen!=round.currentLevel){
                    round.ChangeLevelServerRpc(chosen.levelID,FindObjectOfType<Terminal>().groupCredits);
                    yield return new WaitForSecondsRealtime(1);
                    while(Time.realtimeSinceStartup<until && (round.travellingToNewLevel||round.currentLevel!=chosen))yield return null;
                    Demand(round.currentLevel==chosen&&!round.travellingToNewLevel,"route fixture rejected or timed out");
                }
                var tod=TimeOfDay.Instance;
                if(tod.timeUntilDeadline<tod.totalTime*70)tod.timeUntilDeadline=tod.totalTime*70;
                var previousOverride=round.overrideRandomSeed; var previousSeed=round.overrideSeedNumber;
                try{round.overrideRandomSeed=true;round.overrideSeedNumber=req.seed;r.seedFixture=true;round.StartGameServerRpc();}
                finally{round.overrideRandomSeed=previousOverride;round.overrideSeedNumber=previousSeed;}
                while(Time.realtimeSinceStartup<until&&!round.shipHasLanded)yield return null;
                Demand(round.shipHasLanded&&round.currentLevel==chosen&&SceneManager.GetSceneByName(chosen.sceneName).isLoaded,"no observed landing on intended scene");
                Demand(round.randomMapSeed==req.seed,"seed override was not consumed");
                if(ForcedFlow)Demand(RoundManager.Instance.dungeonGenerator?.Generator?.DungeonFlow==ForcedFlow.DungeonFlow,"forced interior did not actually generate");
                Demand(!p.isPlayerDead&&round.shipBounds.bounds.Contains(p.transform.position),"player did not land in ship");
                break;
            case "enter": case "exit":
                var entering=req.op=="enter";var id=int.Parse(req.arg);
                Demand(round.shipHasLanded&&!p.isPlayerDead&&p.isInsideFactory!=entering,"wrong initial side or dead");
                var doors=FindObjectsOfType<EntranceTeleport>().Where(e=>e.entranceId==id).ToArray();
                var source=doors.Where(e=>e.isEntranceToBuilding==entering&&e.entrancePoint&&e.isActiveAndEnabled)
                    .OrderBy(e=>Vector3.Distance(p.transform.position,e.entrancePoint.position))
                    .FirstOrDefault(e=>e.FindExitPoint()&&e.exitScript&&e.exitScript.entrancePoint);
                Demand(source,"no paired main entrance on this side");
                var destination=source.exitScript;
                Demand(source.entrancePoint&&destination.entrancePoint,"missing entry transform");
                p.TeleportPlayer(source.entrancePoint.position);
                yield return new WaitForSecondsRealtime(.3f);
                source.TeleportPlayer();
                yield return new WaitForSecondsRealtime(.4f);
                Demand(p.isInsideFactory==entering&&Vector3.Distance(p.transform.position,destination.entrancePoint.position)<3,"native teleport did not reach paired exit");
                r.message="Approached by fixture, then used native entrance teleport and checked paired destination";break;
            case "board":
                Demand(!p.isInsideFactory&&!p.isPlayerDead,"player unavailable or inside facility");
                p.TeleportPlayer(round.playerSpawnPositions[(int)p.playerClientId%round.playerSpawnPositions.Length].position);
                yield return new WaitForSecondsRealtime(1);
                Demand(round.shipBounds.bounds.Contains(p.transform.position)&&p.isInElevator,"player not aboard");break;
            case "orbit":
                Demand(round.IsHost&&round.shipHasLanded,"host must have landed");
                round.EndGameServerRpc((int)p.playerClientId);
                while(Time.realtimeSinceStartup<until&&!round.inShipPhase)yield return null;
                Demand(round.inShipPhase,"return to orbit timed out");break;
            default:throw new ArgumentException("unknown op");
        }
    }
}
