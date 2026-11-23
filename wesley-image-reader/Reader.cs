using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WesleyImageReader
{
    [Serializable] internal class Catalog { public Entry[] entries; }
    [Serializable] internal class Entry { public string id, texture, title; public int width, height, format; public bool native, inspect; public Rule[] rules; }
    [Serializable] internal class Rule { public string kind, material, slot, path, scene; }

    [BepInPlugin("NT1Ho.WesleyImageReader","Wesley 图片中文阅读","0.1.4")]
    [BepInDependency("butterystancakes.lethalcompany.butteryfixes", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ReaderPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            string location = Info.Location, guid = Info.Metadata.GUID;
            var logger = Logger;
            Reader runtime = null;
            void EnsureRuntime(Scene scene, LoadSceneMode mode)
            {
                if (runtime) return;
                var host = new GameObject("Wesley Reader Runtime");
                DontDestroyOnLoad(host);
                runtime = host.AddComponent<Reader>();
                runtime.Initialize(location, guid, logger);
            }
            SceneManager.sceneLoaded += EnsureRuntime;
            Application.quitting += () => SceneManager.sceneLoaded -= EnsureRuntime;
            EnsureRuntime(default, default);
        }
    }

    internal sealed class Reader : MonoBehaviour
    {
        internal static Reader Active;
        internal readonly Dictionary<string,List<Entry>> ByTexture = new Dictionary<string,List<Entry>>(StringComparer.Ordinal);
        internal readonly Dictionary<string,HashSet<Entry>> ByPrefab=new Dictionary<string,HashSet<Entry>>(StringComparer.Ordinal);
        internal ZipArchive Data;
        internal NativeArt Art;
        private Harmony patch;
        private ReadPanel panel;
        private ManualLogSource logger;

        internal void Initialize(string location, string guid, ManualLogSource log)
        {
            logger=log;
            try
            {
                Art=new NativeArt(this);
                Data=ZipFile.OpenRead(Path.Combine(Path.GetDirectoryName(location),"Reader.wri"));
                using (var file=Data.GetEntry("index.json").Open())
                using (var sr=new StreamReader(file))
                {
                    foreach(var e in JsonUtility.FromJson<Catalog>(sr.ReadToEnd()).entries)
                    {
                        if (Data.GetEntry(e.id+(e.native?".zh.png":".txt"))==null)
                            throw new InvalidDataException(e.id);
                        if(e.native)Art.Register(e);
                        if (!ByTexture.TryGetValue(e.texture,out var list)) ByTexture[e.texture]=list=new List<Entry>();
                        list.Add(e);
                        foreach(var r in e.rules){
                            if(r.kind!="mesh" || r.scene!="")continue;
                            var root=r.path.Split('/')[0];if(!e.inspect&&!root.EndsWith("Prefab",StringComparison.Ordinal))continue;
                            if(!ByPrefab.TryGetValue(root,out var items))ByPrefab[root]=items=new HashSet<Entry>();
                            items.Add(e);
                        }
                    }
                }
                panel=gameObject.AddComponent<ReadPanel>(); panel.enabled=false;
                patch=new Harmony(guid);
                patch.PatchAll(typeof(Reader).Assembly);
                Active=this;Art.Start();
            }
            catch(Exception e){logger.LogError(e);Release();}
        }
        internal static PlayerControllerB Player => GameNetworkManager.Instance?.localPlayerController;
        internal static bool Usable(PlayerControllerB p) => p && p.IsOwner && p.isPlayerControlled && !p.isPlayerDead &&
            !p.isTypingChat && !p.inTerminalMenu && !p.inSpecialInteractAnimation && !p.isGrabbingObjectAnimation &&
            p.quickMenuManager && !p.quickMenuManager.isMenuOpen && !p.inAnimationWithEnemy && !p.isClimbingLadder &&
            !(ShipBuildModeManager.Instance && ShipBuildModeManager.Instance.InBuildMode);

        internal bool TryRead(PlayerControllerB player)
        {
            try
            {
                if (panel.enabled && !panel.FollowsNativeInspect)
                {
                    panel.Close();
                    return true;
                }
                if (!Usable(player) || player.IsInspectingItem || player.activatingItem)
                    return false;
                var held = player.currentlyHeldObjectServer;
                var entry = held ? Targets.Held(held) : null;
                if (entry != null)
                {
                    if (entry.native || entry.inspect || Targets.NativeBook(held)) return false;
                    panel.Show(entry, held, held.transform);
                    return true;
                }
                if (held && held.itemProperties.canBeInspected) return false;
                entry = Targets.Aimed(player, out var target);
                if (entry == null || entry.native || entry.inspect) return false;
                panel.Show(entry, null, target);
                return true;
            }
            catch (Exception ex)
            {
                panel.Close();
                logger.LogWarning(ex.Message);
                return false;
            }
        }

        internal void InspectChanged(PlayerControllerB player)
        {
            if (!player.IsInspectingItem)
            {
                if (panel.FollowsNativeInspect) panel.Close();
                return;
            }
            if (!Usable(player) || !player.currentlyHeldObjectServer) return;
            try
            {
                var held = player.currentlyHeldObjectServer;
                var entry = Targets.Held(held);
                if (entry != null && entry.inspect) panel.Show(entry, held, held.transform);
            }
            catch (Exception ex)
            {
                panel.Close();
                logger.LogWarning(ex.Message);
            }
        }
        private void OnDisable(){Art?.Stop();if(panel)panel.Close();}
        private void OnEnable(){if(Active==this)Art.Start();}
        private void OnDestroy()=>Release();
        private void Release()
        {
            Art?.Stop();if(Active==this)Active=null;
            patch?.UnpatchSelf();patch=null;
            if(panel){panel.Close();Destroy(panel);}Data?.Dispose();Data=null;
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB),"InspectItem_performed")]
    internal static class InspectKey
    {
        private static bool Handles(PlayerControllerB player, InputAction.CallbackContext context) =>
            Reader.Active && Reader.Active.isActiveAndEnabled && player == Reader.Player &&
            context.performed && context.control?.device is Keyboard;

        private static bool Prefix(PlayerControllerB __instance, InputAction.CallbackContext context,
            bool ___throwingObject, out bool __state)
        {
            __state = __instance.IsInspectingItem;
            return ___throwingObject || !Handles(__instance, context) ||
                !Reader.Active.TryRead(__instance);
        }

        private static void Postfix(PlayerControllerB __instance, InputAction.CallbackContext context, bool __state, bool __runOriginal)
        {
            if (__runOriginal && __state != __instance.IsInspectingItem && Handles(__instance, context))
                Reader.Active.InspectChanged(__instance);
        }
    }
}
