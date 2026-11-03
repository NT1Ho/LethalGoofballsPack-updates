using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using Dissonance;
using GameNetcodeStuff;
using HarmonyLib;
using LobbyControl.API;
using LobbyControl.Utils;
using TalkingHeads.VoiceChatPatch;
using Unity.Netcode;
using UnityEngine;

[BepInPlugin("NT1Ho.CrewCompatibility", "Crew Compatibility", "1.0.2")]
[BepInDependency("OpJosMod.ReviveCompany")]
[BepInDependency("TalkingHeads")]
[BepInDependency("mattymatty.LobbyControl")]
[BepInDependency("me.swipez.melonloader.morecompany")]
public sealed class CrewCompatibility : BaseUnityPlugin
{
    static CrewCompatibility instance;
    void Awake()
    {
        instance = this;
        var patcher = new Harmony("NT1Ho.CrewCompatibility");
        void Patch(string target, string prefix = null, string postfix = null, string il = null)
        {
            HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(CrewCompatibility), name);
            patcher.Patch(AccessTools.Method(target), Hook(prefix), Hook(postfix), Hook(il));
        }
        Patch("DeadBodyInfo:Start", il: nameof(BodyTags));
        Patch("PlayerPhysicsRegion:OnTriggerStay", il: nameof(PhysicsBodySlot));
        Patch("OPJosMod.ReviveCompany.GeneralUtil:RevivePlayer", nameof(BeginRevive), nameof(EndRevive), nameof(Revive));
        Patch("OPJosMod.ReviveCompany.GeneralUtil:GetClosestAlivePlayer", nameof(ClosestLiving));
        Patch("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch:canUse", nameof(FindReviveTarget));
        Patch("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch:setHoverTipAndCurrentInteractTriggerPatch", il: nameof(UseAimedBody));
        Patch("TalkingHeads.Util.UtilExtensions:FindComponentInChildren", nameof(HasTransform));
        Patch("TalkingHeads.VoiceChatPatch.VoicePatchPatch:OnPlayerStoppedSpeaking", nameof(StoppedSpeaking));
        Patch("TalkingHeads.VoiceChatPatch.VoicePatchPatch:LateUpdate", il: nameof(RemoveStale));
        Patch("MoreCompany.CosmeticSyncPatch:SV_ReceiveCosmetics", il: nameof(CosmeticDelivery));
        Patch("MoreCompany.CosmeticSyncPatch:SyncCosmeticsToOtherClients", il: nameof(CosmeticDelivery));
        Patch("LobbyControl.API.ConnectionEvents:get_HasCompletedAllCheckpoints", postfix: nameof(OptionalBadge));
        MaskedAnimation.Install(Logger);
        Logger.LogInfo("V81 crew compatibility ready");
    }

    static MethodInfo Method(string name) => AccessTools.Method(typeof(CrewCompatibility), name);
    static bool Calls(CodeInstruction op, string name) => op.operand is MethodInfo m && m.Name == name;
    static IEnumerable<CodeInstruction> BodyTags(IEnumerable<CodeInstruction> source)
    {
        foreach (var op in source)
        {
            if (Calls(op, "set_tag"))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, Method(nameof(SetBodyTag)));
            }
            else yield return op;
        }
    }
    static void SetBodyTag(GameObject part, string tag, DeadBodyInfo body)
    {
        // V81 defines four ragdoll tags. Extended slots keep the prefab tag and their real body ID.
        if (body.playerObjectId < 4) part.tag = tag;
    }
    static IEnumerable<CodeInstruction> PhysicsBodySlot(IEnumerable<CodeInstruction> source)
    {
        var ops = source.ToList();
        var slots = AccessTools.Field(typeof(StartOfRound), "allPlayerScripts");
        int index = ops.FindIndex(op => op.LoadsField(slots));
        if (index < 0 || !ops[index + 1].LoadsConstant(0) || ops[index + 2].opcode != OpCodes.Ldelem_Ref)
            throw new InvalidOperationException("Unrecognized V81 ragdoll slot lookup");
        // The prefab's generic tag also belongs to slot zero. Resolve the colliding corpse itself.
        ops[index + 1].opcode = OpCodes.Ldarg_1;
        ops.Insert(index + 2, new CodeInstruction(OpCodes.Call, Method(nameof(BodySlot))));
        return ops;
    }
    static int BodySlot(Collider bone)
    {
        var body = bone.GetComponentInParent<DeadBodyInfo>();
        return body ? body.playerObjectId : 0;
    }

    static bool BeginRevive(int playerId, out PlayerControllerB __state)
    {
        var slots = StartOfRound.Instance.allPlayerScripts;
        var player = playerId >= 0 && playerId < slots.Length ? slots[playerId] : null;
        __state = player && player.isPlayerDead ? player : null;
        return __state;
    }
    static RagdollGrabbableObject aimedBody;
    static bool FindReviveTarget(PlayerControllerB __0, int ___interactableObjectsMask, ref bool __result)
    {
        var eye = __0.gameplayCamera.transform;
        aimedBody = Physics.Raycast(eye.position, eye.forward, out var hit, __0.grabDistance, ___interactableObjectsMask)
            ? hit.collider.GetComponent<RagdollGrabbableObject>() : null;
        __result = !(aimedBody && aimedBody.ragdoll && aimedBody.ragdoll.playerScript &&
                     aimedBody.ragdoll.playerScript.isPlayerDead && aimedBody.ragdoll.playerScript.deadBody == aimedBody.ragdoll);
        return false;
    }
    static RagdollGrabbableObject AimedBody(Vector3 ignored) => aimedBody;
    static IEnumerable<CodeInstruction> UseAimedBody(IEnumerable<CodeInstruction> source)
    {
        foreach (var op in source)
        {
            if (Calls(op, "GetClosestDeadBody")) op.operand = Method(nameof(AimedBody));
            yield return op;
        }
    }
    static bool ClosestLiving(Vector3 position, ref PlayerControllerB __result)
    {
        float best = float.PositiveInfinity;
        foreach (var p in StartOfRound.Instance.allPlayerScripts)
        {
            if (!p || !p.isPlayerControlled || p.isPlayerDead) continue;
            float distance = (position - p.transform.position).sqrMagnitude;
            if (distance < best) { best = distance; __result = p; }
        }
        return false;
    }

    static IEnumerable<CodeInstruction> Revive(IEnumerable<CodeInstruction> source)
    {
        var ops = source.ToList();
        // The second lookup runs even if the server has already despawned the corpse.
        int lookup = ops.FindLastIndex(op => Calls(op, "GetClosestAlivePlayer"));
        if (lookup < 4 || !Equals(ops[lookup - 3].operand, AccessTools.Field(typeof(PlayerControllerB), "deadBody")))
            throw new InvalidOperationException("Unrecognized revive position lookup");
        ops[lookup - 4].opcode = OpCodes.Ldloc_1; // chosen revive position
        for (int i = lookup - 3; i < lookup; i++) ops[i] = new CodeInstruction(OpCodes.Nop);

        int voice = ops.FindIndex(op => Equals(op.operand, AccessTools.Field(typeof(PlayerControllerB), "currentVoiceChatIngameSettings")));
        int hud = ops.FindIndex(voice, op => op.Calls(AccessTools.PropertyGetter(typeof(GameNetworkManager), "Instance")));
        ops[voice].opcode = OpCodes.Call;
        ops[voice].operand = Method(nameof(RestoreVoiceFilter));
        for (int i = voice + 1; i < hud; i++) { ops[i].opcode = OpCodes.Nop; ops[i].operand = null; }
        int early = ops.FindIndex(op => Calls(op, "UpdatePlayerVoiceEffects"));
        ops[early].opcode = OpCodes.Pop;
        ops[early].operand = null;
        int cleanup = ops.FindIndex(op => Calls(op, "GetClosestDeadBody"));
        if (cleanup < 3 || ops[cleanup - 3].opcode != OpCodes.Ldloc_0) throw new InvalidOperationException("Unrecognized revive cleanup");
        ops[cleanup - 2] = new CodeInstruction(OpCodes.Call, Method(nameof(CleanOwnBody)));
        ops[cleanup - 1] = new CodeInstruction(OpCodes.Nop);
        ops[cleanup] = new CodeInstruction(OpCodes.Ldnull); // original proximity cleanup sees no object
        return ops;
    }

    static void RestoreVoiceFilter(PlayerControllerB p)
    {
        if (!p.currentVoiceChatIngameSettings) StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
        var settings = p.currentVoiceChatIngameSettings;
        if (!settings) return;
        if (!settings.voiceAudio) settings.InitializeComponents();
        if (settings.voiceAudio && settings.voiceAudio.TryGetComponent<OccludeAudio>(out var filter))
            filter.overridingLowPass = false;
    }

    static void CleanOwnBody(PlayerControllerB p)
    {
        var body = p.deadBody;
        if (!body || body.playerObjectId != (int)p.playerClientId) return;
        var handle = body.grabBodyObject as RagdollGrabbableObject;
        if (!handle || handle.ragdoll != body) return;
        if (handle.isHeld && handle.playerHeldBy && handle.playerHeldBy.IsOwner &&
            handle.playerHeldBy.currentlyHeldObjectServer == handle) handle.playerHeldBy.DiscardHeldObject();
        if (StartOfRound.Instance.IsServer) instance.StartCoroutine(Despawn(body, handle));
    }
    static IEnumerator Despawn(DeadBodyInfo body, RagdollGrabbableObject handle)
    {
        // The owner's normal discard RPC clears just the corpse slot before NGO removes it.
        while (handle && body && handle.isHeld && handle.playerHeldBy && handle.playerHeldBy.isPlayerControlled)
            yield return null;
        if (handle && body && handle.ragdoll == body && handle.IsSpawned) handle.NetworkObject.Despawn();
    }
    static void EndRevive(PlayerControllerB __state)
    {
        if (!__state || __state.isPlayerDead) return;
        __state.disableInteract = false;
        __state.enemyWaitingForBodyRagdoll = null;
        __state.overrideGameOverSpectatePivot = null;
        StartOfRound.Instance.UpdatePlayerVoiceEffects();
    }

    static bool HasTransform(Transform transform) => transform;
    static readonly AccessTools.FieldRef<TalkingPlayer, VoicePlayerState> Voice =
        AccessTools.FieldRefAccess<TalkingPlayer, VoicePlayerState>("_voice");
    static bool StoppedSpeaking(VoicePlayerState state, List<TalkingPlayer> ____talkingPlayers)
    {
        ____talkingPlayers.RemoveAll(p => Voice(p) == state);
        return false;
    }
    static IEnumerable<CodeInstruction> RemoveStale(IEnumerable<CodeInstruction> source)
    {
        foreach (var op in source)
        {
            if (op.Calls(AccessTools.Method(typeof(List<int>), "ForEach")))
                yield return new CodeInstruction(OpCodes.Call, Method(nameof(RemoveReverse)));
            else yield return op;
        }
    }
    static void RemoveReverse(List<int> indices, Action<int> remove)
    {
        for (int i = indices.Count - 1; i >= 0; --i) remove(indices[i]);
    }

    static IEnumerable<CodeInstruction> CosmeticDelivery(IEnumerable<CodeInstruction> source)
    {
        // These three low-frequency named messages may need the NGO header in addition to their payload.
        foreach (var op in source)
        {
            if (op.LoadsConstant(1300)) yield return new CodeInstruction(OpCodes.Ldc_I4_M1);
            else yield return op;
        }
    }
    static void OptionalBadge(ref bool __result)
    {
        if (__result || !ConnectionEvents.ConnectingClientId.HasValue) return;
        ulong peer = ConnectionEvents.ConnectingClientId.Value;
        var missing = ConnectionEvents.MissingCheckpoints;
        if (missing.Length != 1 || missing[0].Name != "SyncAllPlayerLevels" ||
            missing[0].Plugin.GUID != "mattymatty.LobbyControl") return;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(peer) ||
            !StartOfRound.Instance.allPlayerScripts.Any(p => p && p.actualClientId == peer && p.isPlayerControlled)) return;
        // V81 explicitly skips level sync when local badge data cannot load. No other checkpoint is waived.
        __result = true;
        instance.Logger.LogInfo("Connected player admitted after essential checkpoints; only optional HUD level sync pending");
    }
}
