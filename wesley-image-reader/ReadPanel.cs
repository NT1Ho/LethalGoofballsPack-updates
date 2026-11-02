using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WesleyImageReader
{
    public sealed class ReadPanel : MonoBehaviour
    {
        internal Entry entry;
        public string CurrentId;
        public bool English;
        GrabbableObject item,initialSlot;
        Transform anchor;
        Vector3 origin;
        int page;
        Texture2D image;
        float scroll,height,viewport;
        Font font;
        GUIStyle text;
        internal void Show(Entry e,GrabbableObject held,Transform target)
        {
            origin=Reader.Player.transform.position;item=held;initialSlot=Reader.Player.currentlyHeldObjectServer;anchor=target;page=Targets.Page(held);
            Load(e,false);enabled=true;
        }
        void Load(Entry e,bool english)
        {
            var file=Reader.Active.Data.GetEntry(e.id+(english?".en.png":".zh.png"));
            if(file==null || file.Length>32*1024*1024)throw new InvalidDataException(e.id);
            byte[] bytes;
            using(var stream=file.Open())using(var buffer=new MemoryStream((int)file.Length)){stream.CopyTo(buffer);bytes=buffer.ToArray();}
            var next=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{if(!ImageConversion.LoadImage(next,bytes,true))throw new InvalidDataException(e.id);}
            catch{Destroy(next);throw;}
            if(image)Destroy(image);
            image=next;entry=e;CurrentId=e.id;English=english;scroll=0;
        }
        void Update()
        {
            try{
                var p=Reader.Player;
                if(!Reader.Usable(p) || !anchor || !anchor.gameObject.activeInHierarchy || initialSlot!=p.currentlyHeldObjectServer ||
                   (!item && (origin-p.transform.position).sqrMagnitude>16f)){Close();return;}
                if(item && Targets.Page(item)!=page){
                    var current=Targets.Held(item);if(current==null){Close();return;}
                    page=Targets.Page(item);Load(current,English);
                }
                var k=Keyboard.current;if(k==null)return;
                if(k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame)Load(entry,!English);
                float step=Time.unscaledDeltaTime*viewport;
                if(k.upArrowKey.isPressed)scroll-=step;if(k.downArrowKey.isPressed)scroll+=step;
                if(k.pageDownKey.wasPressedThisFrame)scroll+=viewport*.85f;
                if(k.pageUpKey.wasPressedThisFrame)scroll-=viewport*.85f;
                scroll=Mathf.Clamp(scroll,0,Mathf.Max(0,height-viewport));
            }catch(Exception ex){Debug.LogWarning("[WesleyImageReader] "+ex.Message);Close();}
        }
        internal void Close()
        {
            enabled=false;if(image)Destroy(image);image=null;entry=null;CurrentId=null;anchor=null;item=null;initialSlot=null;English=false;
        }
        void OnDestroy(){Close();if(font)Destroy(font);}
        void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint || !image)return;
            if(text==null){
                font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Noto Sans CJK SC","Arial"},20);
                text=new GUIStyle(GUI.skin.label){font=font,alignment=TextAnchor.MiddleLeft,clipping=TextClipping.Clip};
                text.normal.textColor=Color.white;
            }
            float scale=Mathf.Clamp(Screen.height/800f,.65f,1.8f);
            float w=Mathf.Min(Screen.width*.68f,820*scale),pad=12*scale,line=28*scale;
            float drawWidth=Mathf.Min(w-2*pad,image.width*scale);
            height=drawWidth*image.height/image.width;
            viewport=Mathf.Min(height,Screen.height*.70f);
            float h=viewport+2*pad+2*line,x=(Screen.width-w)/2,y=(Screen.height-h)/2;
            var old=GUI.color;
            try{
                GUI.color=new Color(.035f,.045f,.055f,.86f);GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);
                GUI.color=Color.white;text.fontSize=Mathf.RoundToInt(18*scale);
                GUI.Label(new Rect(x+pad,y+pad,w-2*pad,line),entry.title+(English?"  ·  英文原图":"  ·  简体中文"),text);
                GUI.BeginGroup(new Rect(x+(w-drawWidth)/2,y+pad+line,drawWidth,viewport));
                try{GUI.DrawTexture(new Rect(0,-scroll,drawWidth,height),image,ScaleMode.StretchToFill,true);}finally{GUI.EndGroup();}
                text.fontSize=Mathf.RoundToInt(15*scale);
                GUI.Label(new Rect(x+pad,y+pad+line+viewport,w-2*pad,line),"Z 关闭    ← → 中英切换"+(height>viewport+1?"    ↑ ↓ / PgUp PgDn 滚动":""),text);
            }finally{GUI.color=old;}
        }
    }
}
