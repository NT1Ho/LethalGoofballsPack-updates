using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;

public sealed partial class Probe
{
    private IEnumerator Three(string c)
    {
        var p=Player;
        var round=StartOfRound.Instance;
        var net=NetworkManager.Singleton;
        var doors=FindObjectsOfType<DoorLock>().Where(d=>d.IsSpawned).OrderBy(d=>d.NetworkObjectId).ToArray();
        if(c=="three-info") {Log($"STATE host={net.IsHost} players={round.connectedPlayersAmount+1} landed={round.shipHasLanded} hp={p.health} doors={doors.Length} time={TimeOfDay.Instance.globalTimeSpeedMultiplier}");yield break;}
        if(c=="three-clock")
        {
            var t=TimeOfDay.Instance;
            var rate=t.globalTimeSpeedMultiplier;
            if(!net.IsHost)
                p.TeleportPlayer(GameObject.FindGameObjectsWithTag("OutsideAINode").OrderBy(n=>Vector3.Distance(n.transform.position,round.shipInnerRoomBounds.bounds.center)).First(n=>!round.shipBounds.bounds.Contains(n.transform.position)).transform.position);
            yield return new WaitForSecondsRealtime(2);
            var configType=AccessTools.TypeByName("LethalRegeneration.config.Configuration");
            var config=AccessTools.Property(configType.BaseType,"Instance").GetValue(null);
            int step=0;
            foreach(var multiplier in new[]{rate/.6f,rate})
            {
                t.globalTimeSpeedMultiplier=multiplier;
                AccessTools.Property(configType,"RegenerationPower").SetValue(config,step==0?5:8);
                AccessTools.Property(configType,"RegenerationPowerOutsideShip").SetValue(config,step++==0?2:3);
                p.DamagePlayer(Math.Max(0,p.health-30));
                float start=Time.realtimeSinceStartup, clock=t.globalTime;int hp=p.health;
                int previous=hp, healed=0, pulses=0;
                while(Time.realtimeSinceStartup-start<60){
                    yield return null;
                    if(p.health>previous){healed+=p.health-previous;pulses++;}
                    if(p.health>=70)p.DamagePlayer(p.health-30);
                    previous=p.health;
                }
                Log($"CLOCK host={net.IsHost} inside={p.isInHangarShipRoom} multiplier={multiplier:F3} seconds={Time.realtimeSinceStartup-start:F2} clockDelta={t.globalTime-clock:F2} hp={hp}->{p.health} scale={Time.timeScale} recovered={healed} pulses={pulses}");
            }
            yield break;
        }
        if(c.StartsWith("three-door"))
        {
            var d=doors.First();
            var obj=d.GetComponent("DoorHealth");
            var anim=d.GetComponent<AnimatedObjectTrigger>();
            if(c=="three-doorlock"){d.LockDoor();Log($"LOCKED {d.NetworkObjectId}");yield break;}
            if(c=="three-doorprep")
            {
                if(net.IsHost) {var item=round.allItemsList.itemsList.First(i=>i.spawnPrefab&&i.spawnPrefab.GetComponent<Shovel>()&&i.itemName=="Shovel"); Spawn(item,p.transform.position+Vector3.up);Spawn(item,p.transform.position+Vector3.up);}
                yield return new WaitForSeconds(1);
                var g=FindObjectsOfType<Shovel>().First(x=>x.IsSpawned&&!x.isHeld);yield return Equip(g);
                Log($"EQUIP native {p.currentlyHeldObjectServer?.itemProperties.itemName}");yield break;
            }
            if(c.StartsWith("three-doorhit"))
            {
                var s=p.currentlyHeldObjectServer as Shovel;Check("shovel equipped",s!=null);
                var at=d.transform.position;
                p.TeleportPlayer(at-d.transform.forward*1.3f);
                yield return new WaitForSeconds(.3f);
                AccessTools.Field(typeof(Shovel),"previousPlayerHeldBy").SetValue(s,p);
                p.gameplayCamera.transform.LookAt(at+Vector3.up*.5f);
                Call(s,"HitShovel",false);
                yield return new WaitForSeconds(.4f);
            }
            Log($"DOOR host={net.IsHost} id={d.NetworkObjectId} hp={Call(obj,"GetHealth")} lock={d.isLocked} open={anim.boolValue}");yield break;
        }
        var nodes=GameObject.FindGameObjectsWithTag("AINode").OrderBy(n=>n.name).ToArray();
        if(c=="three-bracken-place")
        {
            var pos=nodes.First().transform.position;
            // Keep host away from the client victim during the spawn.
            if(net.IsHost)
            {
                var type=Resources.FindObjectsOfTypeAll<EnemyType>().First(x=>x.enemyPrefab&&x.enemyPrefab.GetComponent<FlowermanAI>()&&x.enemyName=="Flowerman");
                var b=Instantiate(type.enemyPrefab,pos,Quaternion.identity).GetComponent<FlowermanAI>();b.NetworkObject.Spawn();
                yield return new WaitForSeconds(.4f);
                b.favoriteSpot=nodes.OrderByDescending(n=>Vector3.Distance(n.transform.position,pos)).First().transform;
            }
            yield break;
        }
        var br=FindObjectsOfType<FlowermanAI>().First(b=>b.IsSpawned);
        if(c=="three-bracken-grab"){p.TeleportPlayer(br.transform.position-br.transform.forward);p.isInsideFactory=true;br.KillPlayerAnimationServerRpc((int)p.playerClientId);}
        if(c=="three-bracken-rescue"){br.SetEnemyStunned(true,1f,p);yield break;}
        float startT=Time.time;int who=c=="three-bracken-observe"?1:(int)p.playerClientId;
        var victim=round.allPlayerScripts[who]; var pos0=victim.transform.position;
        for(int i=0;i<24;i++){Log($"DRAG who={who} t={Time.time-startT:F2} gap={Vector3.Distance(br.transform.position,victim.transform.position):F2} moved={Vector3.Distance(pos0,victim.transform.position):F2} dead={victim.isPlayerDead} bound={victim.inAnimationWithEnemy==br}");yield return new WaitForSeconds(.5f);}
    }
}
