using System;
using System.IO;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using GameNetcodeStuff;

public sealed partial class Probe
{
    PlayerControllerB Player=>GameNetworkManager.Instance.localPlayerController;
    IEnumerator Equip(GrabbableObject item)
    {
        var type=AccessTools.TypeByName("Harness");
        var driver=BepInEx.Bootstrap.Chainloader.PluginInfos["000.NT1Ho.UniversalWrench.GameHarness"].Instance;
        yield return driver.StartCoroutine((IEnumerator)AccessTools.Method(type,"NativeEquip").Invoke(driver,new object[]{Player,item}));
    }
    GrabbableObject Spawn(Item item,Vector3 pos)
    {
        var g=Instantiate(item.spawnPrefab,pos,Quaternion.identity).GetComponent<GrabbableObject>();g.NetworkObject.Spawn();return g;
    }
    Item ItemFor<T>() where T:Component=>StartOfRound.Instance.allItemsList.itemsList.First(i=>i.spawnPrefab!=null&&i.spawnPrefab.GetComponent<T>()!=null);
    object Call(object target,string method,params object[] args)=>AccessTools.Method(target.GetType(),method).Invoke(target,args);
    IEnumerator Tests(string command)
    {
        var round=StartOfRound.Instance;var p=Player;
        if(command=="land")
        {
            round.StartGameServerRpc();float deadline=Time.realtimeSinceStartup+150;
            while(!round.shipHasLanded&&Time.realtimeSinceStartup<deadline)yield return new WaitForSeconds(1);
            Check("landed with widened ship",round.shipHasLanded);yield break;
        }
        if(command=="shop")
        {
            var t=FindObjectOfType<Terminal>();var item=t.buyableItemsList.Single(x=>x.itemName=="Shells");
            Check("shell price80",item.creditsWorth==80);int initial=t.groupCredits;
            foreach(string cmd in new[]{"shells","confirm"}){t.terminalInUse=true;t.screenText.SetTextWithoutNotify(cmd);t.textAdded=cmd.Length;t.OnSubmit();Log("SHOP "+cmd+" credits="+t.groupCredits+" node="+t.currentNode?.name+" text="+t.currentNode?.displayText);yield return new WaitForSeconds(.3f);}
            Check("native terminal debited80",initial-t.groupCredits==80);
            Check("shell queued for delivery",t.orderedItemsFromTerminal.Contains(Array.IndexOf(t.buyableItemsList,item)));t.terminalInUse=false;yield break;
        }
        if(command=="ship")
        {
            var bounds=round.shipInnerRoomBounds.bounds;var center=bounds.center;
            var wrench=round.allItemsList.itemsList.First(i=>i.name=="UniversalWrench"||i.itemName=="Universal Wrench");
            var keep=new System.Collections.Generic.List<string>();
            foreach(float x in new[]{-5f,0f,5f})
            {
                var start=center+new Vector3(x,1,0);
                Check("floor in widened bay "+x,Physics.Raycast(start,Vector3.down,out var hit,6,268437761,QueryTriggerInteraction.Ignore));
                var item=Spawn(wrench,hit.point+Vector3.up*.3f);yield return new WaitForSeconds(.5f);item.transform.SetParent(round.elevatorTransform,true);item.transform.position=hit.point+Vector3.up*.3f;item.startFallingPosition=item.transform.localPosition;item.isInShipRoom=item.isInElevator=true;item.FallToGround();
                yield return new WaitForSeconds(2);
                Check("wrench settled above floor "+x,item.transform.position.y>hit.point.y-.08f&&item.transform.position.y<hit.point.y+.4f);
                Check("wrench remains in selected bay "+x,Vector2.Distance(new Vector2(item.transform.position.x,item.transform.position.z),new Vector2(hit.point.x,hit.point.z))<.2f);
                keep.Add(item.transform.localPosition.ToString("F4"));Log("FLOOR "+x+" collider="+PathOf(hit.transform)+" pos="+item.transform.position+" floor="+hit.point);
            }
            File.WriteAllLines(Path.Combine(root,"expected-ship.txt"),keep);yield break;
        }
        if(command=="shotgun")
        {
            var gun=NetworkManager.Singleton.IsServer?(ShotgunItem)Spawn(ItemFor<ShotgunItem>(),p.transform.position+Vector3.up):FindObjectsOfType<ShotgunItem>().First(g=>g.IsSpawned&&!g.isHeld);
            yield return Equip(gun);
            foreach(bool safety in new[]{true,false})foreach(int ammo in new[]{0,1,2})
            {
                gun.safetyOn=safety;gun.shellsLoaded=ammo;gun.SetControlTipsForItem();yield return new WaitForSeconds(.2f);
                Check("shotgun state "+safety+" "+ammo,HUDManager.Instance.controlTipLines[1].text.Contains(ammo+" / 2")&&HUDManager.Instance.controlTipLines[3].text.Contains(safety?"已锁定":"已解除"));
                Log("TIPS "+string.Join("|",HUDManager.Instance.controlTipLines.Select(x=>x.text)));
            }
            gun.safetyOn=true;p.DiscardHeldObject();yield return new WaitForSeconds(1);
            Check("shotgun tooltip clears on drop",!HUDManager.Instance.controlTipLines.Any(x=>x.text.Contains("枪内弹药")));yield break;
        }
        if(command=="birdspawn")
        {
            var node=GameObject.FindGameObjectsWithTag("OutsideAINode").OrderBy(x=>Vector3.Distance(x.transform.position,p.transform.position)).First(x=>Vector3.Distance(x.transform.position,p.transform.position)>18);
            var type=Resources.FindObjectsOfTypeAll<EnemyType>().First(x=>x.enemyPrefab!=null&&x.enemyPrefab.GetComponent<RadMechAI>()!=null);
            type.requireNestObjectsToSpawn=false; // Controlled collision fixture, not a production setting.
            var bird=Instantiate(type.enemyPrefab,node.transform.position,Quaternion.identity).GetComponent<RadMechAI>();bird.NetworkObject.Spawn();
            yield return new WaitForSeconds(1);bird.enabled=false;bird.agent.enabled=false;
            var claw=bird.GetComponentsInChildren<EnemyAICollisionDetect>().First(x=>x.name=="ClawTrigger");
            ((IShockableWithGun)claw).ShockWithGun(p);yield return new WaitForSeconds(.3f);((IShockableWithGun)claw).StopShockingWithGun();
            Log("BIRD_SPAWN id="+bird.NetworkObjectId+" shutdown="+bird.GetComponent(AccessTools.TypeByName("MoreCounterplay.Behaviours.OldBirdShutdown")));
            Spawn(ItemFor<Shovel>(),p.transform.position+Vector3.up);yield break;
        }
        if(command=="birdfixture")
        {
            foreach(var type in Resources.FindObjectsOfTypeAll<EnemyType>().Where(x=>x.enemyPrefab!=null&&x.enemyPrefab.GetComponent<RadMechAI>()!=null))type.requireNestObjectsToSpawn=false;
            Log("Controlled bird collision fixture permits spawn without random map nest");yield break;
        }
        if(command=="bird")
        {
            var bird=FindObjectsOfType<RadMechAI>().First(x=>x.IsSpawned);bird.enabled=false;bird.agent.enabled=false;
            var shovel=FindObjectsOfType<Shovel>().First(x=>x.IsSpawned&&!x.isHeld);yield return Equip(shovel);
            AccessTools.Field(typeof(Shovel),"previousPlayerHeldBy").SetValue(shovel,p);
            var shapes=bird.GetComponentsInChildren<Collider>(true).Where(x=>x.name=="PackShovelHitOnly").ToArray();Check("five dedicated hit regions",shapes.Length==5);
            var claw=bird.GetComponentsInChildren<Collider>().First(x=>x.name=="ClawTrigger");var oldSize=((BoxCollider)claw).size;
            var healthType=AccessTools.TypeByName("EverythingCanDieAlternative.Core.HealthManager")??AccessTools.TypeByName("EverythingCanDieAlternative.HealthManager");
            var health=AccessTools.Method(healthType,"GetEnemyHealth");
            foreach(var c in shapes)
            {
                c.enabled=true;Physics.SyncTransforms();var point=c.bounds.center;c.enabled=false;
                p.TeleportPlayer(point-bird.transform.forward*1.5f-Vector3.up*1.6f);p.isInElevator=p.isInHangarShipRoom=p.isInsideFactory=false;
                p.gameplayCamera.transform.LookAt(point);yield return new WaitForEndOfFrame();p.gameplayCamera.transform.LookAt(point);
                float before=Convert.ToSingle(health.Invoke(null,new object[]{bird}));shovel.HitShovel(false);yield return new WaitForSeconds(.4f);float after=Convert.ToSingle(health.Invoke(null,new object[]{bird}));
                Log("HIT "+c.transform.parent.name+" hp="+before+"->"+after+" force="+shovel.shovelHitForce+" camera="+p.gameplayCamera.transform.position+" target="+point);
                Check("one native shovel damage "+c.transform.parent.name,Mathf.Approximately(before-after,shovel.shovelHitForce));
                Check("hit volumes disabled outside query",shapes.All(x=>!x.enabled));
            }
            Check("native grab collider unchanged",((BoxCollider)claw).size==oldSize);
            var hp=Convert.ToSingle(health.Invoke(null,new object[]{bird}));p.TeleportPlayer(bird.transform.position-bird.transform.forward*9);p.gameplayCamera.transform.LookAt(bird.transform.position+Vector3.up*1.5f);shovel.HitShovel(false);yield return new WaitForSeconds(.3f);
            Check("out of native range does not damage",Convert.ToSingle(health.Invoke(null,new object[]{bird}))==hp);p.DiscardHeldObject();yield break;
        }
        if(command=="snapshot")
        {
            Log("SNAP peers="+NetworkManager.Singleton.ConnectedClientsIds.Count+" landed="+round.shipHasLanded);
            foreach(var i in FindObjectsOfType<GrabbableObject>().Where(i=>i.IsSpawned&&(i.itemProperties.itemName.Contains("Wrench")||i.itemProperties.itemName.Contains("Landmine")||i.itemProperties.itemName=="Shells")))Log("ITEM "+i.NetworkObjectId+" "+i.itemProperties.itemName+" pos="+i.transform.localPosition+" ship="+i.isInShipRoom);
            var rev=AccessTools.TypeByName("ReviveCompany.ConfigVariables")??AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>{try{return a.GetTypes();}catch{return new Type[0];}}).First(x=>x.Name=="ConfigVariables"&&AccessTools.Field(x,"DeadPlayerWeight")!=null);
            Check("corpse config=2.125",Convert.ToSingle(AccessTools.Field(rev,"DeadPlayerWeight").GetValue(null))==2.125f);
            Check("shell replicated price80",FindObjectOfType<Terminal>().buyableItemsList.Single(x=>x.itemName=="Shells").creditsWorth==80);
            yield break;
        }
        if(command=="delivery")
        {
            var drop=FindObjectOfType<ItemDropship>();float until=Time.realtimeSinceStartup+120;
            while(!drop.shipLanded&&Time.realtimeSinceStartup<until)yield return new WaitForSeconds(1);
            Check("delivery ship arrived",drop.shipLanded);drop.TryOpeningShip();yield return new WaitForSeconds(2);
            Check("purchased shell spawned and networked",FindObjectsOfType<GrabbableObject>().Any(i=>i.IsSpawned&&i.itemProperties.itemName=="Shells"));yield break;
        }
        if(command=="radarspawn")
        {
            var radar=(RadarBoosterItem)Spawn(ItemFor<RadarBoosterItem>(),round.shipInnerRoomBounds.bounds.center+Vector3.forward*2);
            yield return new WaitForSeconds(1);radar.EnableRadarBooster(true);radar.isBeingUsed=true;
            Log("RADAR id="+radar.NetworkObjectId);yield break;
        }
        if(command=="radar")
        {
            var radar=FindObjectsOfType<RadarBoosterItem>().First(x=>x.IsSpawned);radar.EnableRadarBooster(true);
            var map=round.mapScreen;int index=map.radarTargets.FindIndex(x=>x.transform==radar.transform);Check("radar target registered",index>=0);map.SwitchRadarTargetAndSync(index);yield return new WaitForSeconds(2);
            var screen=map.mesh.GetComponents<MonoBehaviour>().First(x=>x.GetType().Name=="ScreenScript");
            var b=(Bounds)Call(screen,"GetBounds");p.TeleportPlayer(b.center-Vector3.right*1.5f-Vector3.up*1.6f);
            yield return new WaitForSeconds(2);p.gameplayCamera.transform.LookAt(b.center);
            Check("monitor operated from native ship interior",p.isInHangarShipRoom&&round.shipInnerRoomBounds.bounds.Contains(p.transform.position));
            Log("MONITOR bounds="+b+" player="+p.transform.position+" camera="+p.gameplayCamera.transform.position+" range="+p.grabDistance+" dead="+p.isPlayerDead);
            object[] args={null,null,null};bool looking=(bool)AccessTools.Method(screen.GetType(),"IsLookingAtMonitor").Invoke(screen,args);
            Check("touchscreen resolves monitor ray",looking);Log("RADAR aim="+b+" radar="+radar.transform.position+" cam="+map.cam.transform.position);
            Call(screen,"OnPlayerInteraction",false);yield return new WaitForSeconds(.15f);Check("monitor Ping plays native audio",radar.GetComponentsInChildren<AudioSource>().Any(x=>x.isPlaying));
            p.gameplayCamera.transform.LookAt(b.center);Call(screen,"OnPlayerInteraction",true);yield return new WaitForSeconds(.1f);Check("monitor Flash triggers native cooldown",(float)AccessTools.Field(typeof(RadarBoosterItem),"flashCooldown").GetValue(radar)>1f);yield break;
        }
        if(command=="mine")
        {
            var center=round.shipInnerRoomBounds.bounds.center+new Vector3(5,0,0);
            Check("mine widened bay floor",Physics.Raycast(center+Vector3.up,Vector3.down,out var floor,6,268437761,QueryTriggerInteraction.Ignore));
            var type=AccessTools.TypeByName("UniversalWrench.PortableMine");var definition=(Item)AccessTools.Field(type,"Definition").GetValue(null);
            var mine=Spawn(definition,floor.point);yield return new WaitForSeconds(.4f);Call(mine,"Position",floor.point,90f);Call(mine,"Deploy",floor.point,90f);
            var moduleType=AccessTools.TypeByName("UniversalWrench.LandmineDisassembly");var module=Activator.CreateInstance(moduleType,true);
            var wrenchItem=round.allItemsList.itemsList.First(i=>i.itemName=="Universal Wrench");var wrench=Spawn(wrenchItem,floor.point+Vector3.up);
            p.TeleportPlayer(floor.point-Vector3.forward*1.1f+Vector3.up*.1f);p.isInHangarShipRoom=p.isInElevator=true;yield return Equip(wrench);p.Crouch(true);yield return new WaitForSeconds(.6f);
            p.gameplayCamera.transform.LookAt(floor.point+Vector3.up*.08f);
            Check("wrench disarm succeeds in widened bay",(bool)Call(module,"Apply",p,wrench,mine.NetworkObject));yield return new WaitForSeconds(.5f);
            Check("mine sits on new floor",Mathf.Abs(mine.transform.position.y-floor.point.y)<.1f);
            p.Crouch(false);yield return Equip(mine);Check("mine pickup in widened bay",p.currentlyHeldObjectServer==mine);
            p.DiscardHeldObject();yield return new WaitForSeconds(1.2f);
            Call(mine,"Deploy",floor.point,180f);Check("mine redeploy armed",(bool)AccessTools.Field(type,"Armed").GetValue(mine));
            Call(mine,"Disarm");var pluginType=AccessTools.TypeByName("UniversalWrench.Plugin");var facilities=AccessTools.Field(pluginType,"Facilities").GetValue(null);Call(facilities,"PublishMine",mine,null);
            Check("mine preserves ship parent",mine.transform.parent==round.elevatorTransform);yield break;
        }
        if(command=="save")
        {
            if(p.currentlyHeldObjectServer!=null)p.DiscardHeldObject();yield return new WaitForSeconds(1);
            GameNetworkManager.Instance.SaveGame();Call(GameNetworkManager.Instance,"SaveItemsInShip");Log("SAVED "+GameNetworkManager.Instance.currentSaveFileName);yield break;
        }
        if(command=="furniture")
        {
            foreach(var o in FindObjectsOfType<PlaceableShipObject>())Log("FURNITURE_AVAILABLE "+o.name+" id="+o.unlockableID+" parent="+o.parentObject);
            var f=FindObjectsOfType<PlaceableShipObject>().First(x=>x.parentObject!=null&&round.unlockablesList.unlockables[x.unlockableID].unlockableName.ToLower().Contains("cupboard"));
            var original=f.placeObjectCollider.transform.position;var pos=original+Vector3.right*3;
            ShipBuildModeManager.Instance.PlaceShipObject(pos,f.mainMesh.transform.eulerAngles,f,false);
            ShipBuildModeManager.Instance.PlaceShipObjectServerRpc(pos,f.mainMesh.transform.eulerAngles,new NetworkObjectReference(f.parentObject.GetComponent<NetworkObject>()),(int)p.playerClientId);
            yield return new WaitForSeconds(1);Check("native furniture placement recorded",round.unlockablesList.unlockables[f.unlockableID].hasBeenMoved);
            File.WriteAllText(Path.Combine(root,"furniture.txt"),f.unlockableID+"|"+pos.ToString("F4"));Log("FURNITURE "+f.name+" id="+f.unlockableID+" position="+pos);yield break;
        }
        if(command=="kill")
        {
            p.TeleportPlayer(round.shipInnerRoomBounds.bounds.center);p.isInHangarShipRoom=p.isInElevator=true;p.KillPlayer(Vector3.zero,true,CauseOfDeath.Gravity,0);
            yield return new WaitForSeconds(2);Check("isolated client produced real player corpse",p.isPlayerDead&&p.deadBody!=null);yield break;
        }
        if(command=="weight")
        {
            var body=FindObjectsOfType<RagdollGrabbableObject>().First(x=>x.IsSpawned&&x.bodyID>=0);
            if(p.currentlyHeldObjectServer!=null)p.DiscardHeldObject();yield return new WaitForSeconds(.8f);
            p.TeleportPlayer(body.transform.position+Vector3.back);yield return new WaitForSeconds(1);p.gameplayCamera.transform.LookAt(body.GetComponent<Collider>().bounds.center);Physics.SyncTransforms();
            float before=p.carryWeight;Call(p,"BeginGrabObject");yield return new WaitForSeconds(2);float added=p.carryWeight-before;
            Log("CORPSE carry="+before+"->"+p.carryWeight+" addedLb="+(added*105)+" held="+p.currentlyHeldObjectServer);
            Check("native ray picked actual corpse",p.currentlyHeldObjectServer==body);
            Check("player corpse configured weight",body.itemProperties.weight==2.125f);
            Check("actual corpse pickup adds half baseline",Mathf.Abs(added-1.125f)<.001f);Log("CORPSE carry="+before+"->"+p.carryWeight+" addedLb="+(added*105));p.DiscardHeldObject();yield break;
        }
        if(command=="reload")
        {
            var items=FindObjectsOfType<GrabbableObject>().Where(x=>x.IsSpawned&&x.itemProperties.itemName=="Universal Wrench").ToArray();
            foreach(string expected in File.ReadAllLines(Path.Combine(root,"expected-ship.txt")))
            {
                var xyz=expected.Trim('(',')').Split(',').Select(s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var local=new Vector3(xyz[0],xyz[1],xyz[2]);
                Check("saved widened bay item reloaded "+expected,items.Any(x=>Vector3.Distance(x.transform.localPosition,local)<.12f));
            }
            foreach(var mine in FindObjectsOfType<GrabbableObject>().Where(x=>x.IsSpawned&&x.GetType().Name=="PortableMine"))
            {
                Check("saved portable mine above floor",Physics.Raycast(mine.transform.position+Vector3.up*.25f,Vector3.down,out var hit,.5f,268437761,QueryTriggerInteraction.Ignore));
                Check("saved portable mine floor alignment",Mathf.Abs(mine.transform.position.y-hit.point.y)<.1f);
            }
            if(File.Exists(Path.Combine(root,"furniture.txt"))){int id=int.Parse(File.ReadAllText(Path.Combine(root,"furniture.txt")).Split('|')[0]);Check("furniture placement survived reload",round.unlockablesList.unlockables[id].hasBeenMoved);}
            yield break;
        }
        if(command=="windows")
        {
            var type=AccessTools.TypeByName("ShipWindows.ShipWindows");var registry=AccessTools.Field(type,"windowRegistry").GetValue(null);var manager=AccessTools.Field(type,"windowManager").GetValue(null);
            foreach(var window in (IEnumerable)AccessTools.Property(registry.GetType(),"Windows").GetValue(registry))
            {
                string name=(string)AccessTools.Field(window.GetType(),"windowName").GetValue(window);
                if(!name.Contains("Left")&&!name.Contains("Right"))continue;
                Call(manager,"CreateWindow",window,true,true);Log("WINDOW_CREATED "+name);
            }
            yield return new WaitForSeconds(1);
            var list=(IEnumerable)AccessTools.Field(manager.GetType(),"spawnedWindows").GetValue(manager);int n=0;
            foreach(Component window in list){n++;Call(window,"ToggleWindowShutter",false,false);Log("WINDOW "+PathOf(window.transform)+" "+window.transform.position);}
            Check("side windows created with wider ship",n>=2);yield break;
        }
        if(command=="layout")
        {
            var type=AccessTools.TypeByName("ShipWindows.ShipWindows");var manager=AccessTools.Field(type,"windowManager").GetValue(null);
            var windows=((IEnumerable)AccessTools.Field(manager.GetType(),"spawnedWindows").GetValue(manager)).Cast<Component>().ToArray();Check("purchased windows survived reload",windows.Length>=2);
            foreach(var w in windows)Call(w,"ToggleWindowShutter",false,false);
            foreach(var r in FindObjectsOfType<Renderer>().Where(x=>x.name.ToLower().Contains("screen")||x.name.ToLower().Contains("monitor")))Log("SCREEN "+PathOf(r.transform)+" bounds="+r.bounds);
            p.TeleportPlayer(round.shipInnerRoomBounds.bounds.center+Vector3.down);yield return new WaitForSeconds(1);
            var look=windows[0].GetComponentInChildren<Renderer>().bounds.center;float until=Time.realtimeSinceStartup+12;
            while(Time.realtimeSinceStartup<until){p.gameplayCamera.transform.LookAt(look);yield return new WaitForEndOfFrame();}yield break;
        }
    }
}
