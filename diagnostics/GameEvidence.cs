using System;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;

namespace LcDiagnostics {
internal sealed class GameEvidence {
    readonly Harmony patches = new Harmony("nt1ho.diagnostics.native");
    internal GameEvidence() {
        Observe(nameof(PlayerControllerB.DamagePlayer),nameof(BeforeDamage),nameof(AfterDamage));
        Observe(nameof(PlayerControllerB.KillPlayer),nameof(BeforeDeath),nameof(AfterDeath));
    }
    void Observe(string target,string before,string after) {
        var method=AccessTools.DeclaredMethod(typeof(PlayerControllerB),target);
        if(method!=null)patches.Patch(method,new HarmonyMethod(typeof(GameEvidence),before),new HarmonyMethod(typeof(GameEvidence),after));
    }
    struct DamageState { public int Hp; public bool Dead; }
    static void BeforeDamage(PlayerControllerB __instance,out DamageState __state) {
        __state=new DamageState{Hp=__instance.health,Dead=__instance.isPlayerDead};
    }
    static void AfterDamage(PlayerControllerB __instance,DamageState __state,int damageNumber,CauseOfDeath causeOfDeath) {
        if(!__instance.IsOwner||__state.Dead||(__instance.health>=__state.Hp&&!__instance.isPlayerDead))return;
        Plugin.Native("damage","native_damage","applied","player:"+__instance.NetworkObjectId,
            $"slot={__instance.playerClientId} requested={damageNumber} hp={__state.Hp}->{__instance.health} cause={causeOfDeath} dead={__instance.isPlayerDead}");
    }
    struct DeathState { public bool Dead; public ulong? Grabber; }
    static void BeforeDeath(PlayerControllerB __instance,out DeathState __state) {
        var enemy=__instance.inAnimationWithEnemy;
        __state=new DeathState{Dead=__instance.isPlayerDead,Grabber=enemy?(ulong?)enemy.NetworkObjectId:null};
    }
    static void AfterDeath(PlayerControllerB __instance,DeathState __state,CauseOfDeath causeOfDeath) {
        if(!__instance.IsOwner||__state.Dead||!__instance.isPlayerDead)return;
        Plugin.Native("death","native_death","confirmed","player:"+__instance.NetworkObjectId,
            $"slot={__instance.playerClientId} cause={causeOfDeath} corpse={__instance.deadBody!=null} grabber={__state.Grabber}");
    }
    internal void Stop()=>patches.UnpatchSelf();
}
}

