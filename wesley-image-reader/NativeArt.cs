using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace WesleyImageReader
{
    // Change local display materials only. Original textures stay intact for puzzle logic and removal.
    internal sealed class NativeArt
    {
        readonly Reader owner;
        readonly Dictionary<string,Dictionary<string,List<Entry>>> materials=new Dictionary<string,Dictionary<string,List<Entry>>>();
        readonly Dictionary<Texture2D,Texture2D> translated=new Dictionary<Texture2D,Texture2D>();
        Coroutine pending;
        bool running,refreshAgain;
        internal NativeArt(Reader owner){this.owner=owner;}
        internal void Register(Entry e)
        {
            foreach(var rule in e.rules){
                string material=rule.material, slot=rule.slot;
                if(rule.kind!="mesh")continue;
                if(!materials.TryGetValue(material,out var slots))materials[material]=slots=new Dictionary<string,List<Entry>>();
                if(!slots.TryGetValue(slot,out var entries))slots[slot]=entries=new List<Entry>();
                if(!entries.Contains(e))entries.Add(e);
            }
        }
        internal void Start()
        {
            if(running)return;
            running=true;SceneManager.sceneLoaded+=Loaded;Schedule();
        }
        void Loaded(Scene scene,LoadSceneMode mode)=>Schedule();
        void Schedule(){
            if(!running)return;
            if(pending!=null){refreshAgain=true;return;}
            pending=owner.StartCoroutine(Refresh());
        }
        IEnumerator Refresh()
        {
            yield return null;
            var all=Resources.FindObjectsOfTypeAll<Material>();
            // Drop cached copies no longer used by any loaded material (e.g. after leaving a moon).
            if(translated.Count>0){
                var used=new HashSet<Texture>();
                foreach(var mat in all){
                    if(!mat || !materials.TryGetValue(MaterialName(mat),out var slots))continue;
                    foreach(var slot in slots.Keys)if(mat.HasProperty(slot))used.Add(mat.GetTexture(slot));
                }
                foreach(var src in new List<Texture2D>(translated.Keys)){
                    var zh=translated[src];if(used.Contains(zh))continue;
                    translated.Remove(src);if(zh)Object.Destroy(zh);
                }
            }
            foreach(var mat in all){
                if(!mat)continue;
                int count=translated.Count;
                Apply(mat);
                if(translated.Count!=count)yield return null;
            }
            pending=null;
            if(refreshAgain){refreshAgain=false;Schedule();}
        }
        static string MaterialName(Material mat){
            var name=mat.name;
            return name.EndsWith(" (Instance)",StringComparison.Ordinal)?name.Substring(0,name.Length-11):name;
        }
        internal void Apply(Material mat)
        {
            if(!running || !mat)return;
            if(!materials.TryGetValue(MaterialName(mat),out var slots))return;
            foreach(var pair in slots){
                if(!mat.HasProperty(pair.Key) || !(mat.GetTexture(pair.Key) is Texture2D src))continue;
                foreach(var e in pair.Value){
                    if(src.name!=e.texture || src.width!=e.width || src.height!=e.height || (int)src.format!=e.format)continue;
                    try{
                        if(!translated.TryGetValue(src,out var zh)){
                            zh=Decode(e,src);translated.Add(src,zh);
                        }
                        mat.SetTexture(pair.Key,zh);
                    }catch(Exception ex){Debug.LogWarning("[WesleyImageReader] "+e.id+" "+ex.Message);}
                    break;
                }
            }
        }
        Texture2D Decode(Entry e,Texture2D src)
        {
            var tex=new Texture2D(2,2,TextureFormat.RGBA32,src.mipmapCount>1,!src.isDataSRGB);
            try{
                using(var stream=owner.Data.GetEntry(e.id+".zh.png").Open())
                using(var ms=new MemoryStream()){
                    stream.CopyTo(ms);if(!ImageConversion.LoadImage(tex,ms.ToArray(),true))throw new InvalidDataException(e.id);
                }
                tex.name="WesleyImageReader/"+e.id;
                tex.filterMode=src.filterMode;tex.wrapModeU=src.wrapModeU;tex.wrapModeV=src.wrapModeV;
                tex.anisoLevel=src.anisoLevel;tex.mipMapBias=src.mipMapBias;
                return tex;
            }catch{Object.Destroy(tex);throw;}
        }
        internal void Stop()
        {
            if(!running)return;
            running=false;refreshAgain=false;SceneManager.sceneLoaded-=Loaded;
            if(pending!=null){owner.StopCoroutine(pending);pending=null;}
            if(translated.Count==0)return;
            var originals=new Dictionary<Texture,Texture>();
            foreach(var kv in translated)if(kv.Value)originals[kv.Value]=kv.Key;
            foreach(var mat in Resources.FindObjectsOfTypeAll<Material>()){
                if(!mat || !materials.TryGetValue(MaterialName(mat),out var slots))continue;
                foreach(string slot in slots.Keys){
                    if(!mat.HasProperty(slot))continue;
                    var current=mat.GetTexture(slot);
                    if(current && originals.TryGetValue(current,out var src))mat.SetTexture(slot,src);
                }
            }
            foreach(var texture in translated.Values)if(texture)Object.Destroy(texture);
            translated.Clear();
        }
    }
}
