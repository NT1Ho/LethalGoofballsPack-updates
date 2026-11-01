using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GameNetcodeStuff;
using LethalLevelLoader;
using UnityEngine;
using WesleyMoonScripts.Components;

public sealed partial class WesleyProbe
{
    private static string Route(ExtendedLevel level) =>
        $"{level.SelectableLevel.sceneName}|{level.NumberlessPlanetName}|{level.IsRouteLocked}|{level.IsRouteHidden}";
    private static void InspectStory(Result r){
        var p=GameNetworkManager.Instance.localPlayerController;
        r.slots=p.ItemSlots.Select((g,index)=>g?$"{index}|{g.NetworkObjectId}|{g.itemProperties.name}":$"{index}|empty").ToArray();
        r.tapes=FindObjectsOfType<LevelCassetteItem>().Where(t=>t.GetComponent<GrabbableObject>()?.IsSpawned==true).Select(t=>{
            var g=t.GetComponent<GrabbableObject>();
            return $"{g.NetworkObjectId}|{g.itemProperties.name}|{g.itemProperties.itemName}|{t.LoaderName}|{t.LevelSceneName}|{t.unlockLevel}|held={g.isHeld}|ship={g.isInShipRoom}";
        }).ToArray();
        r.loaders=FindObjectsOfType<LevelCassetteLoader>().Select(l=>$"{l.NetworkObjectId}|{l.LoaderName}|playing={l.isTapePlaying}").ToArray();
        r.routes=PatchedContent.ExtendedLevels.Where(l=>l&&l.SelectableLevel).Select(Route).ToArray();
    }
    private IEnumerator Story(Request req,Result r){
        var p=GameNetworkManager.Instance.localPlayerController;
        if(req.op=="save"){
            Demand(StartOfRound.Instance.IsHost&&StartOfRound.Instance.inShipPhase,"host in orbit required");
            GameNetworkManager.Instance.SaveGame();yield return new WaitForSecondsRealtime(1);r.message="SaveGame returned; check after process restart.";yield break;
        }
        ulong id=ulong.Parse(req.arg,CultureInfo.InvariantCulture);
        if(req.op=="tapegrab"){
            var item=FindObjectsOfType<GrabbableObject>().Single(g=>g.IsSpawned&&g.NetworkObjectId==id);
            Demand(!p.currentlyHeldObjectServer&&!item.isHeld&&item.GetComponent<LevelCassetteItem>(),"select an active, unheld tape and an empty hand");
            var collider=item.GetComponentsInChildren<Collider>().First(c=>c.enabled&&c.CompareTag("PhysicsProp"));
            var target=collider.bounds.center;
            var fwd=p.gameplayCamera.transform.forward;fwd.y=0;fwd.Normalize();
            var cameraOffset=p.gameplayCamera.transform.position-p.transform.position;
            p.TeleportPlayer(target-fwd*1.1f-cameraOffset);
            yield return new WaitForSecondsRealtime(.25f);
            p.gameplayCamera.transform.LookAt(collider.bounds.center);
            // Native entry raycasts, checks before pickup, then performs the normal grab RPC and animation.
            typeof(PlayerControllerB).GetMethod("BeginGrabObject",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(p,null);
            yield return new WaitForSecondsRealtime(2);
            Demand(p.currentlyHeldObjectServer==item&&item.isHeld,"native pickup did not acquire this tape");
            r.message="Existing scene item acquired through native BeginGrabObject";yield break;
        }
        var loader=FindObjectsOfType<LevelCassetteLoader>().Single(l=>l.IsSpawned&&l.NetworkObjectId==id);
        var held=p.currentlyHeldObjectServer;
        var tape=held?held.GetComponents<LevelCassetteItem>().FirstOrDefault(t=>t.LoaderName==loader.LoaderName&&t.unlockLevel&&t.GetExtendedLevel(out _)):null;
        Demand(tape&&tape.GetExtendedLevel(out _),"hold the matching route tape");
        tape.GetExtendedLevel(out var level);
        Demand(level.IsRouteLocked,"route already open; cannot establish unlock");
        var trigger=FindObjectsOfType<InteractTrigger>().FirstOrDefault(t=>t.interactable&&Enumerable.Range(0,t.onInteract.GetPersistentEventCount()).Any(i=>t.onInteract.GetPersistentTarget(i)==loader&&t.onInteract.GetPersistentMethodName(i)=="StartLoadingCassette"));
        Demand(trigger,"no active authored loader trigger found");
        p.TeleportPlayer(trigger.transform.position+trigger.transform.forward);yield return new WaitForSecondsRealtime(.4f);
        trigger.Interact(p.transform);
        float end=Time.realtimeSinceStartup+10;
        while(level.IsRouteLocked&&Time.realtimeSinceStartup<end)yield return null;
        Demand(!level.IsRouteLocked,"route remained locked after scene interaction");
        r.message="Native authored InteractTrigger -> LevelCassetteLoader; observed locked-to-open: "+Route(level);
    }
}
