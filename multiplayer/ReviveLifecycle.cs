using System.Linq;
using GameNetcodeStuff;
using OPJosMod.ReviveCompany;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed partial class CrewCompatibility
{
    const string ReviveMessage = "NT1Ho.CrewCompatibility.Revive.1";
    static RagdollGrabbableObject acceptedBody;

    static void RegisterReviveMessage()
    {
        NetworkManager.Singleton?.CustomMessagingManager?.RegisterNamedMessageHandler(ReviveMessage, ReceiveRevival);
    }

    static void BeginDeathWindow(int playerClientId)
    {
        var info = GlobalVariables.PlayerInfos.Find(p => p.PlayerId == playerClientId);
        if (info == null) GlobalVariables.PlayerInfos.Add(new PlayerInfo { PlayerId = playerClientId });
        else info.HasBeenTeleported = false;
    }

    static RagdollGrabbableObject CurrentCorpse(PlayerControllerB player) =>
        player && player.isPlayerDead && player.deadBody && player.deadBody.playerObjectId == (int)player.playerClientId
            ? player.deadBody.grabBodyObject as RagdollGrabbableObject : null;

    static PlayerControllerB CurrentRevival(int slot, ulong corpseId)
    {
        var players = StartOfRound.Instance?.allPlayerScripts;
        if (players == null || slot < 0 || slot >= players.Length) return null;
        var p = players[slot];
        var corpse = CurrentCorpse(p);
        return corpse && corpse.IsSpawned && corpse.NetworkObjectId == corpseId && corpse.ragdoll == p.deadBody ? p : null;
    }

    static void RequestRevival(int slot)
    {
        var net = NetworkManager.Singleton;
        if (!net || !aimedBody || !aimedBody.IsSpawned) return;
        ulong corpse = aimedBody.NetworkObjectId;
        if (net.IsServer) ConfirmRevival(net.LocalClientId, slot, corpse);
        else SendRevival(net, slot, corpse, broadcast: false);
    }

    static void ReceiveRevival(ulong sender, FastBufferReader reader)
    {
        if (!reader.TryBeginRead(sizeof(int) + sizeof(ulong))) return;
        reader.ReadValue(out int slot);
        reader.ReadValue(out ulong corpse);
        var net = NetworkManager.Singleton;
        if (net.IsServer && sender != net.LocalClientId) ConfirmRevival(sender, slot, corpse);
        else if (!net.IsServer && sender == NetworkManager.ServerClientId)
            ApplyRevival(CurrentRevival(slot, corpse), corpse);
    }

    static void ConfirmRevival(ulong sender, int slot, ulong corpse)
    {
        var victim = CurrentRevival(slot, corpse);
        var round = StartOfRound.Instance;
        if (!victim || !round || victim.actualClientId == sender || GlobalVariables.RemainingRevives <= 0 ||
            !round.allPlayerScripts.Any(p => p && p.actualClientId == sender && p.isPlayerControlled && !p.isPlayerDead) ||
            (!ConfigVariables.reviveTeleportedBodies && GeneralUtil.HasPlayerTeleported(slot)) ||
            (!ConfigVariables.InfiniteReviveTime && Time.time - GeneralUtil.GetPlayersDiedAtTime(slot) > ConfigVariables.TimeUnitlCantBeRevived))
        {
            instance.Logger.LogDebug($"Revive rejected player={slot} corpse={corpse}");
            return;
        }
        if (ApplyRevival(victim, corpse))
            SendRevival(NetworkManager.Singleton, slot, corpse, broadcast: true);
    }

    static bool ApplyRevival(PlayerControllerB player, ulong corpse)
    {
        if (!player) return false;
        acceptedBody = CurrentCorpse(player);
        try { GeneralUtil.RevivePlayer((int)player.playerClientId); }
        finally { acceptedBody = null; }
        if (player.isPlayerDead) return false;
        instance.Logger.LogDebug($"Revive applied player={player.playerClientId} corpse={corpse}");
        return true;
    }

    static void SendRevival(NetworkManager net, int slot, ulong corpse, bool broadcast)
    {
        using var writer = new FastBufferWriter(sizeof(int) + sizeof(ulong), Allocator.Temp);
        writer.WriteValueSafe(slot);
        writer.WriteValueSafe(corpse);
        // V81's reliable death RPCs use this same ordered transport.
        const NetworkDelivery delivery = NetworkDelivery.ReliableFragmentedSequenced;
        if (broadcast) net.CustomMessagingManager.SendNamedMessageToAll(ReviveMessage, writer, delivery);
        else net.CustomMessagingManager.SendNamedMessage(ReviveMessage, NetworkManager.ServerClientId, writer, delivery);
    }
}
