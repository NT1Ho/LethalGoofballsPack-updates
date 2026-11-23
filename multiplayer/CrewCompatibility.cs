using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using Dissonance;
using GameNetcodeStuff;
using HarmonyLib;
using LobbyControl.API;
using LobbyControl.Utils;
using OPJosMod.ReviveCompany;
using TalkingHeads.VoiceChatPatch;
using Unity.Netcode;
using UnityEngine;

[BepInPlugin("NT1Ho.CrewCompatibility", "Crew Compatibility", "1.0.4")]
[BepInDependency("OpJosMod.ReviveCompany")]
[BepInDependency("TalkingHeads")]
[BepInDependency("mattymatty.LobbyControl")]
[BepInDependency("me.swipez.melonloader.morecompany")]
[BepInDependency(HotbarPlusId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class CrewCompatibility : BaseUnityPlugin
{
    const string HotbarPlusId = "FlipMods.HotbarPlus";
    static Action<PlayerControllerB, int> sendAbsoluteSlot;
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
        Patch("OPJosMod.ReviveCompany.GeneralUtil:SetPlayerDiedAt", nameof(BeginDeathWindow));
        Patch("OPJosMod.ReviveCompany.GeneralUtil:GetClosestAlivePlayer", nameof(ClosestLiving));
        Patch("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch:canUse", nameof(FindReviveTarget));
        Patch("OPJosMod.ReviveCompany.Patches.PlayerControllerBPatch:setHoverTipAndCurrentInteractTriggerPatch", il: nameof(ReviveInteraction));
        Patch("StartOfRound:Start", postfix: nameof(RegisterReviveMessage));
        Patch("TalkingHeads.Util.UtilExtensions:FindComponentInChildren", nameof(HasTransform));
        Patch("TalkingHeads.VoiceChatPatch.VoicePatchPatch:OnPlayerStoppedSpeaking", nameof(StoppedSpeaking));
        Patch("TalkingHeads.VoiceChatPatch.VoicePatchPatch:LateUpdate", il: nameof(RemoveStale));
        Patch("MoreCompany.CosmeticSyncPatch:SV_ReceiveCosmetics", il: nameof(CosmeticDelivery));
        Patch("MoreCompany.CosmeticSyncPatch:SyncCosmeticsToOtherClients", il: nameof(CosmeticDelivery));
        Patch("LobbyControl.API.ConnectionEvents:get_HasCompletedAllCheckpoints", postfix: nameof(OptionalBadge));
        InstallHotbarSlotSync(patcher);
        MaskedAnimation.Install(Logger);
        Logger.LogInfo("V81 crew compatibility ready");
    }

    static void InstallHotbarSlotSync(Harmony patcher)
    {
        if (!Chainloader.PluginInfos.TryGetValue(HotbarPlusId, out var hotbar)) return;
        var version = hotbar.Metadata.Version;
        if (version != new Version(1, 8, 5) && version != new Version(1, 8, 6)) return;
        var send = AccessTools.Method(typeof(PlayerControllerB), "SwitchToSlotServerRpc", new[] { typeof(int) });
        var relative = AccessTools.Method("HotbarPlus.Networking.SyncManager:SendHotbarSlotChange", new[] { typeof(int) });
        if (send == null || relative == null) return;
        sendAbsoluteSlot = (Action<PlayerControllerB, int>)send.CreateDelegate(typeof(Action<PlayerControllerB, int>));
        patcher.Patch(relative, prefix: new HarmonyMethod(typeof(CrewCompatibility), nameof(SendHotbarSlot)));
        instance.Logger.LogInfo("HotbarPlus uses V81's native absolute slot sync");
    }

    static bool SendHotbarSlot(int __0)
    {
        var player = StartOfRound.Instance?.localPlayerController;
        // The native utility slot is 50 and is outside the normal wheel sequence.
        if (player && player.IsOwner && NetworkManager.Singleton?.IsClient == true &&
            __0 >= 0 && __0 < player.ItemSlots.Length && __0 != player.currentItemSlot)
            sendAbsoluteSlot(player, __0);
        return false;
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
        __state = acceptedBody ? CurrentRevival(playerId, acceptedBody.NetworkObjectId) : null;
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
    static IEnumerable<CodeInstruction> ReviveInteraction(IEnumerable<CodeInstruction> source)
    {
        bool sentLegacyRevive = false;
        foreach (var op in source)
        {
            if (Calls(op, "GetClosestDeadBody")) op.operand = Method(nameof(AimedBody));
            if (Calls(op, "RevivePlayer")) op.operand = Method(nameof(RequestRevival));
            if (op.operand is MethodInfo check && check.DeclaringType == typeof(string) && check.Name == "op_Inequality")
                op.operand = Method(nameof(CanHoldRevive));
            if (Calls(op, "SentMessageNeedResponses") || (!sentLegacyRevive && Calls(op, "SendRpcMessage")))
            {
                sentLegacyRevive = true;
                op.opcode = OpCodes.Pop;
                op.operand = null;
            }
            yield return op;
        }
    }
    static bool CanHoldRevive(string ignoredTip, string ignoredEmpty)
    {
        var p = StartOfRound.Instance?.localPlayerController;
        return p && p.IsOwner && !p.isPlayerDead && !p.isGrabbingObjectAnimation && !p.inTerminalMenu &&
            !p.inSpecialMenu && !p.isTypingChat && !p.quickMenuManager.isMenuOpen;
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
        int health = ops.FindIndex(op => Equals(op.operand, AccessTools.Field(typeof(ConfigVariables), nameof(ConfigVariables.ExtraHealthLostPerRevive))));
        if (health < 0 || ops[health + 1].opcode != OpCodes.Mul || ops[health + 2].opcode != OpCodes.Sub)
            throw new InvalidOperationException("Unrecognized revive health calculation");
        ops.Insert(health + 3, new CodeInstruction(OpCodes.Call, Method(nameof(ClampReviveHealth))));
        return ops;
    }

    static int ClampReviveHealth(int health) => Math.Max(1, health);

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
        p.deadBody = null;
        if (handle.isHeld && handle.playerHeldBy && handle.playerHeldBy.IsOwner &&
            handle.playerHeldBy.currentlyHeldObjectServer == handle) handle.playerHeldBy.DiscardHeldObject();
        if (StartOfRound.Instance.IsServer) instance.StartCoroutine(Despawn(body, handle));
    }
    static IEnumerator Despawn(DeadBodyInfo body, RagdollGrabbableObject handle)
    {
        // Send the accepted revival before Netcode removes that corpse on the other peers.
        yield return null;
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
        if (__state == StartOfRound.Instance.localPlayerController) HUDManager.Instance.HideHUD(false);
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
