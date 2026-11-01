using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLevelLoader;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class WesleyProbe
{
    internal static ExtendedDungeonFlow ForcedFlow;
    [HarmonyPatch(typeof(DungeonManager),nameof(DungeonManager.GetValidExtendedDungeonFlows))]
    private static class FlowFixture
    {
        private static void Postfix(bool debugResults, ref List<ExtendedDungeonFlowWithRarity> __result){
            // Only LLL's server lottery invokes with debugResults=true. Its own RPC distributes this selection.
            if(debugResults&&ForcedFlow&&NetworkManager.Singleton.IsServer)
                __result=new List<ExtendedDungeonFlowWithRarity>{new ExtendedDungeonFlowWithRarity(ForcedFlow,100)};
        }
    }
    private IEnumerator TestApparatus(Request req,Result r){
        var round=StartOfRound.Instance;
        var p=GameNetworkManager.Instance.localPlayerController;
        Demand(round.IsServer&&round.shipHasLanded&&!p.isPlayerDead,"landed host required");
        var helper=Chainloader.PluginInfos["000.NT1Ho.UniversalWrench.GameHarness"].Instance;
        var type=helper.GetType();
        GrabbableObject Spawn(Item i)=>(GrabbableObject)AccessTools.Method(type,"DoraSpawn").Invoke(helper,new object[]{i,p.transform.position+Vector3.up});
        var native=(Item)AccessTools.Property(AccessTools.TypeByName("UniversalWrench.SpecialFlashlight"),"NativeApparatus").GetValue(null);
        var positive=req.arg=="native";
        var item=positive?native:round.allItemsList.itemsList.First(i=>i&&i.spawnPrefab&&i.spawnPrefab.name==req.arg&&i.spawnPrefab.GetComponent<LungProp>());
        Demand(positive||item!=native,"negative fixture must have distinct Item identity");
        var wrenchItem=round.allItemsList.itemsList.First(i=>i.name=="NT1Ho_UniversalWrench");
        var gear=Spawn(wrenchItem);var source=Spawn(item);var netId=source.NetworkObjectId;
        var aim=AccessTools.Field(type,"aim");
        void KeyState(params Key[] keys)=>AccessTools.Method(type,"KeyState").Invoke(helper,new object[]{keys});
        int Charges(){var ch=AccessTools.Property(gear.GetType(),"Charges").GetValue(gear);return (int)AccessTools.Property(ch.GetType(),"Remaining").GetValue(ch);}
        int Hearts()=>FindObjectsOfType<FlashlightItem>().Count(x=>x.IsSpawned&&x.itemProperties.name=="NT1Ho_SpecialFlashlight");
        var fixtureHearts=Hearts();
        try{
            yield return helper.StartCoroutine((IEnumerator)AccessTools.Method(type,"NativeEquip").Invoke(helper,new object[]{p,gear}));
            Demand(p.currentlyHeldObjectServer==gear,"native wrench equip failed");
            p.Crouch(true);p.thisPlayerBody.rotation=Quaternion.identity;
            AccessTools.Method(type,"Position").Invoke(null,new object[]{p,source});
            aim.SetValue(helper,source);
            yield return new WaitForSecondsRealtime(.8f);
            var effective=(GrabbableObject)AccessTools.Method(AccessTools.TypeByName("UniversalWrench.GameRepairWorld"),"AimedTarget").Invoke(null,new object[]{p});
            var hud=p.cursorTip.text;
            Demand(effective==source,"fixture aim was not actually on the apparatus: "+hud);
            Demand(hud.Contains("升级为电源之心")==positive,"unexpected prompt on "+item.name+": "+hud);
            int before=Charges();
            KeyState(Key.E);
            yield return new WaitForSecondsRealtime(3.7f);
            KeyState();
            yield return new WaitForSecondsRealtime(.5f);
            int after=Charges(),afterHearts=Hearts();
            bool remains=NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(netId);
            r.message=$"item={item.name} prefab={item.spawnPrefab.name} target={netId} prompt={hud} charges={before}->{after} hearts={fixtureHearts}->{afterHearts} sourceStillSpawned={remains}";
            Demand(positive ? !remains&&after==before-1&&afterHearts==fixtureHearts+1 : remains&&before==after&&afterHearts==fixtureHearts,r.message);
        }finally{
            KeyState();aim.SetValue(helper,null);p.Crouch(false);
        }
    }
}
