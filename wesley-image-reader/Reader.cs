using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WesleyImageReader
{
    [Serializable] internal class Catalog { public Entry[] entries; }
    [Serializable] internal class Entry { public string id, texture, title; public int width, height; public Rule[] rules; }
    [Serializable] internal class Rule { public string kind, material, slot, path, scene; }

    [BepInPlugin("NT1Ho.WesleyImageReader","Wesley 图片中文阅读","0.1.0")]
    public sealed class Reader : BaseUnityPlugin
    {
        internal static Reader Active;
        internal readonly Dictionary<string,List<Entry>> ByTexture = new Dictionary<string,List<Entry>>(StringComparer.Ordinal);
        internal readonly Dictionary<string,HashSet<Entry>> ByPrefab=new Dictionary<string,HashSet<Entry>>(StringComparer.Ordinal);
        internal ZipArchive Data;
        private Harmony patch;
        private InputAction read;
        private ReadPanel panel;

        private void Awake()
        {
            try
            {
                Data=ZipFile.OpenRead(Path.Combine(Path.GetDirectoryName(Info.Location),"Reader.wri"));
                using (var file=Data.GetEntry("index.json").Open())
                using (var sr=new StreamReader(file))
                {
                    foreach(var e in JsonUtility.FromJson<Catalog>(sr.ReadToEnd()).entries)
                    {
                        if (Data.GetEntry(e.id+".zh.png")==null || Data.GetEntry(e.id+".en.png")==null)
                            throw new InvalidDataException(e.id);
                        if (!ByTexture.TryGetValue(e.texture,out var list)) ByTexture[e.texture]=list=new List<Entry>();
                        list.Add(e);
                        foreach(var r in e.rules){
                            if(r.kind!="mesh" || r.scene!="")continue;
                            var root=r.path.Split('/')[0];if(!root.EndsWith("Prefab",StringComparison.Ordinal))continue;
                            if(!ByPrefab.TryGetValue(root,out var items))ByPrefab[root]=items=new HashSet<Entry>();
                            items.Add(e);
                        }
                    }
                }
                panel=gameObject.AddComponent<ReadPanel>(); panel.enabled=false;
                read=new InputAction("WesleyRead",InputActionType.Button,"<Keyboard>/z");
                read.performed+=Toggle;
                patch=new Harmony(Info.Metadata.GUID);
                patch.PatchAll(typeof(Reader).Assembly);
                Active=this;read.Enable();
            }
            catch(Exception e){Logger.LogError(e);Release();}
        }
        internal static PlayerControllerB Player => GameNetworkManager.Instance?.localPlayerController;
        internal static bool Usable(PlayerControllerB p) => p && p.IsOwner && p.isPlayerControlled && !p.isPlayerDead &&
            !p.isTypingChat && !p.inTerminalMenu && !p.inSpecialInteractAnimation && !p.isGrabbingObjectAnimation &&
            !p.quickMenuManager.isMenuOpen && !p.inAnimationWithEnemy && !p.isClimbingLadder &&
            !(ShipBuildModeManager.Instance && ShipBuildModeManager.Instance.InBuildMode);
        private void Toggle(InputAction.CallbackContext context)
        {
            try {
                if(panel.enabled){panel.Close();return;}
                var p=Player;
                if(!Usable(p))return;
                var held=p.currentlyHeldObjectServer;
                var entry=held?Targets.Held(held):null;
                if(entry!=null)panel.Show(entry,held,held.transform);
                else {
                    entry=Targets.Aimed(p,out var target);
                    if(entry!=null)panel.Show(entry,null,target);
                }
            } catch(Exception e){panel.Close();Logger.LogWarning(e.Message);}
        }
        private void OnDisable(){if(read!=null)read.Disable();if(panel)panel.Close();}
        private void OnEnable(){if(Active==this)read?.Enable();}
        private void OnDestroy()=>Release();
        private void Release()
        {
            if(Active==this)Active=null;
            read?.Dispose();read=null;patch?.UnpatchSelf();patch=null;
            if(panel){panel.Close();Destroy(panel);}Data?.Dispose();Data=null;
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB),"InspectItem_performed")]
    internal static class InspectKey
    {
        private static bool Prefix(InputAction.CallbackContext context) =>
            !Reader.Active || !Reader.Active.isActiveAndEnabled || !(context.control?.device is Keyboard k) || context.control != k.zKey;
    }
}
