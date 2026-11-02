using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WesleyImageReader
{
    public sealed class ReadPanel : MonoBehaviour
    {
        public string CurrentId;
        GrabbableObject item,initialSlot;
        Transform anchor;
        Vector3 origin;
        int page;
        GUIContent body;
        float scroll,height,viewport;
        Font font;
        GUIStyle style;

        internal void Show(Entry e,GrabbableObject held,Transform target)
        {
            item=held;initialSlot=Reader.Player.currentlyHeldObjectServer;anchor=target;
            origin=Reader.Player.transform.position;page=Targets.Page(held);
            Load(e);enabled=true;
        }
        void Load(Entry e)
        {
            using(var file=Reader.Active.Data.GetEntry(e.id+".txt").Open())
            using(var reader=new StreamReader(file))body=new GUIContent(reader.ReadToEnd());
            CurrentId=e.id;scroll=0;
        }
        void Update()
        {
            try{
                var p=Reader.Player;
                if(!Reader.Usable(p) || !anchor || !anchor.gameObject.activeInHierarchy || initialSlot!=p.currentlyHeldObjectServer ||
                   (!item && (origin-p.transform.position).sqrMagnitude>16f)){Close();return;}
                if(item && Targets.Page(item)!=page){
                    var current=Targets.Held(item);if(current==null || current.native){Close();return;}
                    page=Targets.Page(item);Load(current);
                }
                var k=Keyboard.current;if(k==null)return;
                float step=Time.unscaledDeltaTime*viewport;
                if(k.upArrowKey.isPressed)scroll-=step;if(k.downArrowKey.isPressed)scroll+=step;
                if(k.pageDownKey.wasPressedThisFrame)scroll+=viewport*.85f;
                if(k.pageUpKey.wasPressedThisFrame)scroll-=viewport*.85f;
                scroll=Mathf.Clamp(scroll,0,Mathf.Max(0,height-viewport));
            }catch(Exception ex){Debug.LogWarning("[WesleyImageReader] "+ex.Message);Close();}
        }
        internal void Close(){enabled=false;CurrentId=null;body=null;anchor=null;item=null;initialSlot=null;}
        void OnDestroy(){if(font)Destroy(font);}
        void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint || body==null)return;
            if(style==null){
                font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Noto Sans CJK SC","Arial"},22);
                style=new GUIStyle(GUI.skin.label){font=font,wordWrap=true,richText=false,alignment=TextAnchor.UpperLeft};
                style.normal.textColor=Color.white;
            }
            float scale=Mathf.Clamp(Screen.height/800f,.65f,1.8f),pad=14*scale;
            style.fontSize=Mathf.RoundToInt(22*scale);
            float width=Mathf.Min(Mathf.Min(Screen.width*.59f,650*scale),Mathf.Ceil(style.CalcSize(body).x)+2);
            height=Mathf.Ceil(style.CalcHeight(body,width))+2;viewport=Mathf.Min(height,Screen.height*.61f);
            float x=(Screen.width-width)/2,y=(Screen.height-viewport)/2;
            var old=GUI.color;
            try{
                GUI.color=new Color(.035f,.045f,.055f,.80f);
                GUI.DrawTexture(new Rect(x-pad,y-pad,width+2*pad,viewport+2*pad),Texture2D.whiteTexture);
                GUI.color=Color.white;
                GUI.BeginGroup(new Rect(x,y,width,viewport));
                try{GUI.Label(new Rect(0,-scroll,width,height),body,style);}finally{GUI.EndGroup();}
            }finally{GUI.color=old;}
        }
    }
}
