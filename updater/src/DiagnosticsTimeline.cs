using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LcPack {
public sealed class DiagnosticsTimeline : ScrollableControl {
    public DiagnosticReview Review;public string SelectedId;public event Action<string> Selected;
    readonly Dictionary<string,Rectangle> dots=new Dictionary<string,Rectangle>();
    readonly ToolTip tip=new ToolTip{OwnerDraw=true,InitialDelay=250,ReshowDelay=100,AutoPopDelay=15000};
    string hovering,caption;Image thumbnail;
    public DiagnosticsTimeline(){DoubleBuffered=true;AutoScroll=true;BackColor=Color.FromArgb(245,247,250);TabStop=true;AccessibleName="F8 故障时间轴，亦可使用下方列表选择";
        tip.Popup+=(s,e)=>e.ToolTipSize=new Size(360,300);
        tip.Draw+=(s,e)=>{e.DrawBackground();e.DrawBorder();if(thumbnail!=null)e.Graphics.DrawImage(thumbnail,new Rectangle(10,8,340,190));else TextRenderer.DrawText(e.Graphics,"未取得截图；现场状态仍可查看",Font,new Rectangle(10,8,340,190),Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);TextRenderer.DrawText(e.Graphics,caption??"",Font,new Rectangle(10,205,340,90),Color.Black,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);};
    }
    public void Rebuild(){
        dots.Clear();if(Review!=null){var lanes=new List<int>();int width=Math.Max(20,ClientSize.Width-52);double duration=Review.Duration;
            foreach(var m in Review.Marks.Where(m=>m.Manual&&(!Review.Deleted(m.Id)||!Review.SavedDeleted(m.Id)))){
                int x=24+(int)(Math.Max(0,Math.Min(1,m.Seconds/duration))*width),lane=0;while(lane<lanes.Count&&x-lanes[lane]<20)lane++;
                if(lane==lanes.Count)lanes.Add(x);else lanes[lane]=x;dots[m.Id]=new Rectangle(x-6,40+lane*24,12,12);
            }AutoScrollMinSize=new Size(0,Math.Max(90,70+lanes.Count*24));
        }else AutoScrollMinSize=Size.Empty;Invalidate();
    }
    public Rectangle NodeBounds(string id){Rectangle r;if(!dots.TryGetValue(id,out r))return Rectangle.Empty;r.Offset(AutoScrollPosition);return r;}
    public string Hit(Point p){foreach(var d in dots){var r=NodeBounds(d.Key);r.Inflate(3,3);if(r.Contains(p))return d.Key;}return null;}
    protected override void OnResize(EventArgs e){base.OnResize(e);Rebuild();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);
        TextRenderer.DrawText(g,Review==null?"请选择一局游戏":dots.Count==0?"本局没有保留的 F8 标记；可查看自动异常或最近删除":"F8 时间轴 · 悬停预览 · 点击查看 · 密集节点分行",Font,new Point(10,6),Color.DimGray);
        if(Review==null)return;using(var pen=new Pen(Color.LightSlateGray))g.DrawLine(pen,24,30,Math.Max(25,ClientSize.Width-28),30);
        foreach(var d in dots){var r=d.Value;using(var pen=new Pen(Color.LightGray))g.DrawLine(pen,r.X+6,30,r.X+6,r.Y);using(var brush=new SolidBrush(Review.Deleted(d.Key)?Color.IndianRed:Color.SteelBlue))g.FillEllipse(brush,r);if(d.Key==SelectedId){r.Inflate(3,3);using(var pen=new Pen(Color.DarkOrange,2))g.DrawEllipse(pen,r);}}
        TextRenderer.DrawText(g,"0:00",Font,new Point(10,AutoScrollMinSize.Height-22),Color.DimGray);TextRenderer.DrawText(g,TimeSpan.FromSeconds(Review.Duration).ToString(@"hh\:mm\:ss"),Font,new Point(Math.Max(60,ClientSize.Width-90),AutoScrollMinSize.Height-22),Color.DimGray);
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);string id=Hit(e.Location);if(id!=null&&Selected!=null)Selected(id);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);string id=Hit(e.Location);if(id==hovering)return;hovering=id;tip.Hide(this);if(thumbnail!=null){thumbnail.Dispose();thumbnail=null;}if(id==null)return;
        var m=Review.Marks.First(x=>x.Id==id);caption=m+"\n"+(Review.Deleted(id)?"待删除 · ":"")+Review.Note(id);string path=Path.Combine(Review.Path,"shot-"+id+".png");
        try{if(File.Exists(path))using(var image=Image.FromFile(path)){var bmp=new Bitmap(340,190);using(var g=Graphics.FromImage(bmp)){g.Clear(Color.FromArgb(30,30,30));float ratio=Math.Min(340f/image.Width,190f/image.Height);int w=(int)(image.Width*ratio),h=(int)(image.Height*ratio);g.DrawImage(image,(340-w)/2,(190-h)/2,w,h);}thumbnail=bmp;}}catch(IOException){}catch(ArgumentException){}
        tip.Show(caption,this,new Point(Math.Min(e.X,Math.Max(0,Width-360)),e.Y+20),15000);
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);tip.Hide(this);hovering=null;}
    protected override void Dispose(bool disposing){if(disposing){tip.Dispose();if(thumbnail!=null)thumbnail.Dispose();}base.Dispose(disposing);}
}
}
