using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace WesleyImageReader
{
    internal static class Targets
    {
        const float reach=3.5f;
        static readonly string[] slots={"_BaseColorMap","_UnlitColorMap","_MainTex","_EmissiveColorMap"};
        static readonly MaterialPropertyBlock props=new MaterialPropertyBlock();
        static string Clean(string name)=>name.Replace("(Clone)","").Replace(" (Instance)","").Trim();
        static bool PathMatches(Transform t,string expected)
        {
            var parts=expected.Split('/');
            for(int i=parts.Length-1;i>=0;i--, t=t.parent)
                if(!t || Clean(t.name)!=parts[i])return false;
            return true;
        }
        static bool FromField(Rule rule,Transform t,Texture texture)
        {
            // Only the displayed texture is considered. An item merely owning an array of possible pages isn't sufficient.
            for(var parent=t;parent;parent=parent.parent){
                if(Clean(parent.name)!=rule.path)continue;
                var script=parent.GetComponent(rule.material);
                if(!script || script.GetType().Assembly.GetName().Name!="WesleysInteriorTools")continue;
                var bracket=rule.slot.IndexOf('[');
                var field=AccessTools.Field(script.GetType(),bracket<0?rule.slot:rule.slot.Substring(0,bracket));
                var value=field?.GetValue(script);
                if(bracket>=0){
                    if(!(value is IList list) || !int.TryParse(rule.slot.Substring(bracket+1).TrimEnd(']'),out var index) || index>=list.Count)continue;
                    value=list[index];
                }
                if(ReferenceEquals(value,texture))return true;
            }
            return false;
        }
        static bool Playing(Transform t,string clip)
        {
            var animator=t.GetComponentInParent<Animator>();
            if(!animator || !animator.isActiveAndEnabled || !animator.runtimeAnimatorController)return false;
            for(int i=0;i<animator.layerCount;i++){
                foreach(var info in animator.GetCurrentAnimatorClipInfo(i))if(info.clip && info.clip.name==clip)return true;
                if(animator.IsInTransition(i))foreach(var info in animator.GetNextAnimatorClipInfo(i))if(info.clip && info.clip.name==clip)return true;
            }
            return false;
        }
        static void MaterialEntries(Material m,Transform t,MaterialPropertyBlock overrides, bool decal,HashSet<Entry> result)
        {
            if(!m)return;
            foreach(string slot in slots){
                if(!m.HasProperty(slot))continue;
                var texture=overrides?.GetTexture(slot) ?? m.GetTexture(slot);
                if(!texture || !Reader.Active.ByTexture.TryGetValue(texture.name,out var entries))continue;
                foreach(var e in entries){
                    if(e.width!=texture.width || e.height!=texture.height)continue;
                    foreach(var rule in e.rules){
                        if(rule.kind=="field"){
                            if(!decal && slot!="_EmissiveColorMap" && FromField(rule,t,texture)){result.Add(e);break;}
                        }else if(rule.kind=="anim" && !decal && rule.material==Clean(m.name) && rule.slot==slot && Playing(t,rule.path)){
                            result.Add(e);break;
                        }else if(rule.kind==(decal?"decal":"mesh") && rule.material==Clean(m.name) && rule.slot==slot &&
                            (rule.scene=="" || rule.scene==t.gameObject.scene.name) && PathMatches(t,rule.path)){
                            result.Add(e);break;
                        }
                    }
                }
            }
        }
        static void RendererEntries(Renderer r,HashSet<Entry> result,int materialIndex=-1)
        {
            var mats=r.sharedMaterials;
            bool overridden=r.HasPropertyBlock();
            for(int i=0;i<mats.Length;i++){
                if(materialIndex>=0 && i!=materialIndex)continue;
                if(overridden){r.GetPropertyBlock(props,i);if(props.isEmpty)r.GetPropertyBlock(props);}
                MaterialEntries(mats[i],r.transform,overridden?props:null,false,result);
            }
        }
        internal static int Page(GrabbableObject held) =>
            held && held.GetType().FullName=="WesleysInteriorTools.Features.ClipboardItemButCool" ?
            (int)(AccessTools.Field(held.GetType(),"currentPage")?.GetValue(held) ?? 0):0;
        internal static Entry Held(GrabbableObject held)
        {
            var hits=new HashSet<Entry>();
            int page=Page(held);
            string prefab=held.itemProperties?.spawnPrefab?.name;
            if(prefab!=null && prefab==Clean(held.name) && Reader.Active.ByPrefab.TryGetValue(prefab,out var authored)){
                string expected=page>0 && prefab=="BattleGuidePrefab"?"BattleManual"+page:
                    page>0 && prefab=="AmbersDiaryPrefab"?"AmberNote"+page:null;
                if(expected!=null){foreach(var e in authored)if(e.texture==expected)hits.Add(e);return Only(hits);}
            }
            foreach(var r in held.GetComponentsInChildren<Renderer>(page!=0)){
                if(!(r is MeshRenderer || r is SkinnedMeshRenderer))continue;
                if(page==0 && (!r.enabled || r.forceRenderingOff))continue;
                RendererEntries(r,hits);
            }
            if(page!=0){
                var root=Clean(held.name);
                string texture=root=="BattleGuidePrefab"?"BattleManual"+page:root=="AmbersDiaryPrefab"?"AmberNote"+page:null;
                if(texture==null)return null;
                hits.RemoveWhere(x=>x.texture!=texture);
            }
            if(hits.Count==0 && page==0 && prefab==Clean(held.name) && prefab!=null &&
                Reader.Active.ByPrefab.TryGetValue(prefab,out var fallback) && fallback.Count==1)return Only(fallback);
            return Only(hits);
        }
        static Entry Only(HashSet<Entry> hits){if(hits.Count==1)foreach(var item in hits)return item;return null;}
        static Ray LocalRay(Ray ray,Transform t)=>new Ray(t.InverseTransformPoint(ray.origin),t.worldToLocalMatrix.MultiplyVector(ray.direction));
        internal static Entry Aimed(PlayerControllerB p,out Transform target)
        {
            target=null;var cam=p.gameplayCamera;var ray=new Ray(cam.transform.position,cam.transform.forward);
            var limit=reach;Collider surface=null;Vector3 point=default;
            foreach(var h in Physics.RaycastAll(ray,reach,cam.cullingMask,QueryTriggerInteraction.Ignore)){
                if(h.distance>=limit || h.collider.transform.IsChildOf(p.transform))continue;
                surface=h.collider;limit=h.distance;point=h.point;
            }
            var result=new HashSet<Entry>();float nearest=reach+.03f;
            Transform matched=null;
            void Consider(HashSet<Entry> found,float distance,Transform obj){
                if(found.Count==0 || distance>nearest+.008f)return;
                if(distance<nearest-.008f){result.Clear();matched=null;}
                nearest=Math.Min(nearest,distance);result.UnionWith(found);matched=obj;
            }
            // One spatial query on Z. Never keep a scene-wide Update, renderer cache or per-frame watcher.
            foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){
                if(!r.enabled || r.forceRenderingOff || r.shadowCastingMode==ShadowCastingMode.ShadowsOnly ||
                    (cam.cullingMask & (1<<r.gameObject.layer))==0 || !r.bounds.IntersectRay(ray,out float entry) ||
                    entry>limit+.025f || entry>nearest+.025f || r.transform.IsChildOf(p.transform))continue;
                var found=new HashSet<Entry>();
                var b=r.localBounds;
                var local=LocalRay(ray,r.transform);
                if(!b.IntersectRay(local,out float t))continue;
                var world=r.transform.TransformPoint(local.GetPoint(t));
                float dist=Vector3.Dot(world-ray.origin,ray.direction);
                if(dist<.04f || dist>limit+.025f)continue;
                var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
                // A readable grabbable is identified as a whole on a direct collider hit.
                // Rectangular Unity/Wesley planes can be selected even when only the backing wall has a collider.
                bool flat=mesh && (mesh.name.StartsWith("Plane",StringComparison.OrdinalIgnoreCase)||mesh.name.StartsWith("Quad",StringComparison.OrdinalIgnoreCase)) &&
                    Mathf.Min(b.size.x,Mathf.Min(b.size.y,b.size.z)) < .005f;
                var held=surface?surface.GetComponentInParent<GrabbableObject>():null;
                if(held && r.transform.IsChildOf(held.transform)){
                    var item=Held(held);if(item!=null)found.Add(item);
                    Consider(found,limit,held.transform);continue;
                }
                if(r.sharedMaterials.Length!=1)continue; // refuse to guess which material owns an unreadable submesh
                if(!flat && (!surface || surface.transform!=r.transform || !r.bounds.Contains(point)))continue;
                RendererEntries(r,found);Consider(found,flat?dist:limit,r.transform);
            }
            // A decal applies only to a visible hit surface inside its projection volume.
            if(surface)foreach(var d in Object.FindObjectsByType<DecalProjector>(FindObjectsSortMode.None)){
                if(!d.isActiveAndEnabled || d.fadeFactor<=0 || (cam.cullingMask&(1<<d.gameObject.layer))==0)continue;
                Vector3 q=d.scaleMode==DecalScaleMode.InheritFromHierarchy?d.transform.InverseTransformPoint(point):
                    Quaternion.Inverse(d.transform.rotation)*(point-d.transform.position);
                if(!(new Bounds(d.pivot,d.size)).Contains(q))continue;
                var found=new HashSet<Entry>();MaterialEntries(d.material,d.transform,null,true,found);Consider(found,limit,d.transform);
            }
            var itemFound=Only(result);if(itemFound!=null)target=matched;return itemFound;
        }
    }
}
