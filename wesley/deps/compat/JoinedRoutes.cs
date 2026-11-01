using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using LethalLevelLoader;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using WesleyMoonScripts;
using WesleyMoonScripts.ScriptableObjects;

namespace LethalGoofballsPack.Wesley
{
    // LLL applies saved flags while the host initializes. A peer joining later
    // has already rebuilt Wesley's default locks and missed that one broadcast.
    internal sealed class JoinedRoutes : MonoBehaviour
    {
        private const string Request = "NT1Ho.Wesley.JoinRoutes.v1";
        private const string Snapshot = "NT1Ho.Wesley.RouteFlags.v1";
        private CustomMessagingManager messages;
        private ManualLogSource log;
        private bool waiting;
        private bool loggedRequest;

        internal void Initialize(ManualLogSource logger)
        {
            log = logger;
            Plugin.onLobbyInitialized += OnLobbyReady;
        }

        private void OnDestroy() => Plugin.onLobbyInitialized -= OnLobbyReady;

        private void OnLobbyReady()
        {
            StopAllCoroutines();
            waiting = false;
            loggedRequest = false;
            if (!WesleyScripts.ProtectionEnabled()) return;
            var net = NetworkManager.Singleton;
            if (!net) return;
            if (net.IsServer)
            {
                messages = net.CustomMessagingManager;
                messages.RegisterNamedMessageHandler(Request, SendCurrentFlags);
                log.LogInfo("Host ready for Wesley campaign route requests");
            }
            else
            {
                waiting = true;
                StartCoroutine(RequestFlags(net));
            }
        }

        private IEnumerator RequestFlags(NetworkManager net)
        {
            // LLL notifies from a prefix on StartOfRound.Awake, before the game assigns its singleton.
            yield return null;
            var round = StartOfRound.Instance;
            // Wait until NGO finishes synchronizing the joining player.
            while (round && round == StartOfRound.Instance && net && !net.ShutdownInProgress && !net.IsConnectedClient)
                yield return null;
            if (!round || round != StartOfRound.Instance || !net || !net.IsConnectedClient) yield break;
            messages = net.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(Snapshot, ApplyHostFlags);
            log.LogInfo("Requesting host campaign route flags after local join");
            // An unusually fast peer can initialize before the host's handler.
            for (int i = 0; i < 6 && waiting && net && net.IsConnectedClient; i++)
            {
                using (var request = new FastBufferWriter(1, Allocator.Temp))
                {
                    request.WriteValueSafe((byte)1);
                    messages.SendNamedMessage(Request, NetworkManager.ServerClientId, request);
                }
                yield return new WaitForSecondsRealtime(5);
            }
            if (waiting && net && net.IsConnectedClient) log.LogWarning("No authoritative Wesley route snapshot received");
        }

        private static IEnumerable<ExtendedLevel> CampaignLevels()
        {
            var scenes = new HashSet<string>(WesleyProgressionIntegration.AllLevels.Where(l => l.forceLock).Select(l => l.sceneName));
            return PatchedContent.ExtendedLevels.Where(l => l && l.SelectableLevel && scenes.Contains(l.SelectableLevel.sceneName));
        }

        private void SendCurrentFlags(ulong sender, FastBufferReader request)
        {
            var net = NetworkManager.Singleton;
            if (!net || !net.IsServer || !net.ConnectedClients.ContainsKey(sender)) return;
            if (!loggedRequest)
            {
                loggedRequest = true;
                log.LogInfo($"Wesley route request from {sender}, length={request.Length}, position={request.Position}");
            }
            // NGO leaves the already-consumed named-message hash in Length; only remaining bytes are payload.
            if (request.Length - request.Position != 1) return;
            request.ReadValueSafe(out byte version);
            if (version != 1) return;
            var levels = CampaignLevels().ToArray();
            // Stable scene hashes convey no hidden names; no UI, save or progression events are invoked.
            using (var data = new FastBufferWriter(sizeof(int) + levels.Length * 5, Allocator.Temp))
            {
                data.WriteValueSafe(levels.Length);
                foreach (var level in levels)
                {
                    data.WriteValueSafe(Animator.StringToHash(level.SelectableLevel.sceneName));
                    data.WriteValueSafe((byte)((level.IsRouteLocked ? 1 : 0) | (level.IsRouteHidden ? 2 : 0)));
                }
                messages.SendNamedMessage(Snapshot, sender, data, NetworkDelivery.ReliableFragmentedSequenced);
                log.LogInfo($"Sent {levels.Length} authoritative Wesley route flags to {sender}");
            }
        }

        private void ApplyHostFlags(ulong sender, FastBufferReader data)
        {
            var net = NetworkManager.Singleton;
            if (!waiting || !net || net.IsServer) return;
            if (sender != NetworkManager.ServerClientId) { log.LogWarning("Rejected route snapshot from non-host"); return; }
            if (!data.TryBeginRead(4)) { log.LogWarning("Rejected truncated route header"); return; }
            data.ReadValueSafe(out int count);
            if (count < 0 || (long)count * 5 != data.Length - data.Position)
            {
                log.LogWarning($"Rejected route snapshot length={data.Length}, position={data.Position}, count={count}");
                return;
            }
            var local = CampaignLevels().ToDictionary(l => Animator.StringToHash(l.SelectableLevel.sceneName));
            var flags = new Dictionary<int, byte>();
            for (int i = 0; i < count; i++)
            {
                data.ReadValueSafe(out int key);
                data.ReadValueSafe(out byte state);
                if (state > 3 || !local.ContainsKey(key) || flags.ContainsKey(key))
                {
                    log.LogWarning($"Rejected unknown, duplicate, or invalid route at index {i}, state={state}");
                    return;
                }
                flags.Add(key, state);
            }
            if (flags.Count != local.Count)
            {
                log.LogWarning($"Rejected route roster mismatch host={flags.Count}, local={local.Count}");
                return;
            }
            foreach (var pair in flags)
            {
                local[pair.Key].IsRouteLocked = (pair.Value & 1) != 0;
                local[pair.Key].IsRouteHidden = (pair.Value & 2) != 0;
            }
            waiting = false;
            log.LogInfo($"Applied authoritative route flags for {count} Wesley campaign levels");
        }
    }
}
