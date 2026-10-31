using System;
using System.IO;
using System.Linq;
using System.Collections;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using GameNetcodeStuff;

[BepInPlugin("NT1Ho.PackMaintenance.TestProbe", "Isolated pack maintenance probe", "1.0.0")]
public sealed partial class Probe : BaseUnityPlugin
{
    string root;
    float poll;
    void Awake()
    {
        if (!File.Exists(Path.Combine(Paths.GameRootPath,"UNIVERSAL_WRENCH_ISOLATED_TEST"))) throw new Exception("Isolated game required");
        root=Path.Combine(Paths.GameRootPath,"pack-test");Directory.CreateDirectory(root);
        new Harmony("NT1Ho.PackMaintenance.TestProbe").PatchAll();
    }
    void Log(string s){File.AppendAllText(Path.Combine(root,"results.txt"),DateTime.UtcNow.ToString("o")+" "+s+"\n");Logger.LogInfo(s);}
    void Update()
    {
        if(Time.unscaledTime<poll)return;poll=Time.unscaledTime+.5f;
        var file=Path.Combine(root,"command.txt");
        if(!File.Exists(file)||GameNetworkManager.Instance?.localPlayerController==null)return;
        var c=File.ReadAllText(file).Trim();File.Delete(file);
        try { if(c=="inspect")Inspect(); else StartCoroutine(Run(Tests(c))); }catch(Exception e){Log("FAIL "+e);}
    }
    static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
    void Check(string name,bool pass){Log((pass?"PASS ":"FAIL ")+name);if(!pass)throw new Exception(name);}
    IEnumerator Run(IEnumerator e){while(true){object v;try{if(!e.MoveNext())break;v=e.Current;}catch(Exception ex){Log("FAIL "+ex);break;}yield return v;}Log("DONE");}
    void Inspect()
    {
        Log("PEERS "+NetworkManager.Singleton.ConnectedClientsIds.Count+" save="+GameNetworkManager.Instance.currentSaveFileName);
        foreach(var x in Chainloader.PluginInfos.Values)Log("PLUGIN "+x.Metadata.GUID+" "+x.Metadata.Version);
        var mech=Resources.FindObjectsOfTypeAll<RadMechAI>().First();
        foreach(var c in mech.GetComponentsInChildren<Collider>(true))Log("BIRD "+PathOf(c.transform)+" type="+c.GetType().Name+" pos="+c.transform.localPosition+" size="+(c is BoxCollider b?b.size.ToString():c is CapsuleCollider ca?$"{ca.radius}/{ca.height}":"mesh")+" layer="+c.gameObject.layer+" trigger="+c.isTrigger+" hit="+(c.GetComponent<IHittable>()!=null)+" bounds="+c.bounds);
        foreach(var r in mech.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Log("MESH "+PathOf(r.transform)+" bounds="+r.localBounds+" readable="+r.sharedMesh.isReadable);
            foreach(var b in r.bones)Log("BONE "+b.name+" local="+b.localPosition+" scale="+b.lossyScale+" rootRelative="+mech.transform.InverseTransformPoint(b.position));
        }
        var s=StartOfRound.Instance;Log("SHIP bounds="+s.shipInnerRoomBounds.bounds);
        var term=FindObjectOfType<Terminal>();
        foreach(var x in term.buyableItemsList.Where(x=>x.itemName.ToLower().Contains("shell")))Log("SHELL "+x.itemName+" price="+x.creditsWorth);
        foreach(var t in FindObjectsOfType<Transform>().Where(t=>t.name.ToLower().Contains("magnet")||t.name.ToLower().Contains("window")||t.GetComponents<Component>().Any(x=>x!=null&&x.GetType().FullName.Contains("ScreenScript"))))Log("OBJECT "+PathOf(t)+" "+t.position);
    }
}

[HarmonyPatch(typeof(GameNetworkManager),"StartHost")]
static class FixtureName
{
    static void Prefix(GameNetworkManager __instance){__instance.currentSaveFileName="PackSixMaintenance";}
}

[HarmonyPatch(typeof(RadarBoosterItem),nameof(RadarBoosterItem.PlayPingAudio))]
static class PingEvidence
{
    static void Postfix(RadarBoosterItem __instance){if(__instance.IsSpawned)File.AppendAllText(Path.Combine(Paths.GameRootPath,"pack-test","radar-events.txt"),DateTime.UtcNow.ToString("o")+" native Ping id="+__instance.NetworkObjectId+" playing="+__instance.radarBoosterAudio.isPlaying+"\n");}
}
[HarmonyPatch(typeof(RadarBoosterItem),nameof(RadarBoosterItem.Flash))]
static class FlashEvidence
{
    static void Postfix(RadarBoosterItem __instance){if(__instance.IsSpawned)File.AppendAllText(Path.Combine(Paths.GameRootPath,"pack-test","radar-events.txt"),DateTime.UtcNow.ToString("o")+" native Flash id="+__instance.NetworkObjectId+" cooldown="+AccessTools.Field(typeof(RadarBoosterItem),"flashCooldown").GetValue(__instance)+"\n");}
}
