using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LcPack {
public sealed class DiagnosticsTimeline : ScrollableControl {
    public DiagnosticReview Review;
    public int Mode;
    public string SelectedId;
    public event Action<string> Selected;
    sealed class Node {
        public DiagnosticMark Mark;
        public Rectangle Dot, Label, Hit;
    }
    readonly List<Node> nodes = new List<Node>();
    readonly ToolTip tip = new ToolTip { OwnerDraw=true, ShowAlways=true };
    string hovering, caption;
    Image thumbnail;
    double duration;
    bool arranging;
    public int NodeCount { get { return nodes.Count; } }
    public int ContentHeight { get; private set; }
    // Match the rendered text even during a per-monitor DPI transition.
    int U(int n) { return (int)Math.Round(n * Font.Height / 17.0); }

    public DiagnosticsTimeline() {
        DoubleBuffered=true;
        AutoScroll=true;
        BackColor=Color.White;
        TabStop=true;
        AccessibleName="故障时间轴，左右方向键选择，回车打开";
        tip.Popup+=(s,e)=>e.ToolTipSize=new Size(U(306),U(236));
        tip.Draw+=(s,e)=>{
            e.DrawBackground();
            e.DrawBorder();
            Rectangle preview=new Rectangle(U(8),U(8),U(290),U(163));
            if(thumbnail!=null) e.Graphics.DrawImage(thumbnail,preview);
            else TextRenderer.DrawText(e.Graphics,"此处没有现场截图",Font,preview,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,caption,Font,new Rectangle(U(8),U(178),U(290),U(52)),ForeColor,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
        };
    }
    public void Bind(DiagnosticReview review,int mode) {
        Review=review;
        Mode=mode;
        SelectedId=null;
        AutoScrollPosition=Point.Empty;
        Rebuild();
    }
    public void Rebuild() {
        duration=Review==null ? 1 : Review.Duration;
        ClearPreview();
        Arrange();
    }
    void Arrange() {
        if(arranging || nodes==null) return;
        arranging=true;
        try {
            nodes.Clear();
            var occupied=new List<List<Rectangle>>();
            int left=U(28), width=Math.Max(1,ClientSize.Width-2*left);
            if(Review!=null) foreach(var mark in Review.Marks) {
                bool deleted=Review.Deleted(mark.Id);
                bool show=Mode==1 ? !mark.Manual : Mode==2 ? mark.Manual&&deleted : mark.Manual&&(!deleted||!Review.SavedDeleted(mark.Id));
                if(!show) continue;
                int x=left+(int)Math.Round(width*Math.Max(0,Math.Min(1,mark.Seconds/duration)));
                int labelWidth=U(deleted||Mode==1?144:88);
                int labelLeft=x+U(11);
                if(labelLeft+labelWidth>ClientSize.Width-U(4)) labelLeft=x-U(11)-labelWidth;
                int lane=0;
                var span=Rectangle.FromLTRB(Math.Min(labelLeft,x-U(10)),0,Math.Max(x+U(10),labelLeft+labelWidth)+U(4),1);
                while(lane<occupied.Count && occupied[lane].Any(r=>r.IntersectsWith(span))) lane++;
                if(lane==occupied.Count) occupied.Add(new List<Rectangle>());
                occupied[lane].Add(span);
                int y=U(68+lane*34);
                var node=new Node { Mark=mark,Dot=new Rectangle(x-U(6),y-U(6),U(12),U(12)),Label=new Rectangle(labelLeft,y-U(12),labelWidth,U(24)) };
                node.Hit=Rectangle.Union(Rectangle.Inflate(node.Dot,U(5),U(5)),node.Label);
                nodes.Add(node);
            }
            ContentHeight=U(100+Math.Max(0,occupied.Count-1)*34);
            AutoScrollMinSize=new Size(0,ContentHeight);
            Invalidate();
        } finally { arranging=false; }
    }
    Rectangle ScreenRect(Rectangle r) { r.Offset(AutoScrollPosition);return r; }
    public Rectangle NodeBounds(string id) {
        var node=nodes.FirstOrDefault(n=>n.Mark.Id==id);
        return node==null?Rectangle.Empty:ScreenRect(node.Dot);
    }
    public string Hit(Point point) {
        var node=nodes.FirstOrDefault(n=>ScreenRect(n.Hit).Contains(point));
        return node==null?null:node.Mark.Id;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e);Arrange(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e);Arrange(); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        var g=e.Graphics;
        int dy=AutoScrollPosition.Y, left=U(28),right=Math.Max(left+1,ClientSize.Width-left),axis=U(36)+dy;
        if(nodes.Count==0) {
            string empty=Review==null?"选择游戏记录后可查看时间轴":Mode==1?"本局没有自动异常":Mode==2?"没有已删除的标记":"本局没有手动标记";
            TextRenderer.DrawText(g,empty,Font,ClientRectangle,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            return;
        }
        int ticks=right-left>U(390)?4:2;
        using(var pen=new Pen(Color.FromArgb(185,194,204))) {
            g.DrawLine(pen,left,axis,right,axis);
            for(int i=0;i<=ticks;i++){
                int x=left+(right-left)*i/ticks;
                g.DrawLine(pen,x,axis-U(3),x,axis+U(3));
                var label=new Rectangle(x-U(29),axis-U(29),U(58),U(24));
                TextRenderer.DrawText(g,PlayerText.Elapsed(duration*i/ticks),Font,label,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            foreach(var n in nodes) g.DrawLine(pen,n.Dot.X+n.Dot.Width/2,axis,n.Dot.X+n.Dot.Width/2,n.Dot.Top+dy);
        }
        g.SmoothingMode=SmoothingMode.AntiAlias;
        foreach(var n in nodes) {
            var dot=ScreenRect(n.Dot);
            var label=ScreenRect(n.Label);
            if(!ScreenRect(n.Hit).IntersectsWith(ClientRectangle)) continue;
            bool deleted=Review.Deleted(n.Mark.Id);
            var color=deleted?Color.IndianRed:n.Mark.Manual?Color.FromArgb(39,105,161):Color.DarkGoldenrod;
            using(var brush=new SolidBrush(color))g.FillEllipse(brush,dot);
            if(n.Mark.Id==SelectedId || n.Mark.Id==hovering) {
                dot.Inflate(U(3),U(3));
                using(var outline=new Pen(color,U(2)))g.DrawEllipse(outline,dot);
            }
            string name=PlayerText.Time(n.Mark);
            if(deleted)name+=Review.SavedDeleted(n.Mark.Id)?"  已删除":"  待删除";
            else if(!n.Mark.Manual)name+="  "+PlayerText.Kind(n.Mark);
            TextRenderer.DrawText(g,name,Font,label,ForeColor,BackColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
        }
    }
    void Open(string id) {
        if(id==null)return;
        ClearPreview();
        SelectedId=id;
        Invalidate();
        if(Selected!=null)Selected(id);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Left){Focus();Open(Hit(e.Location));}
    }
    protected override bool IsInputKey(Keys key) {
        return key==Keys.Left || key==Keys.Right || base.IsInputKey(key);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if(nodes.Count==0)return;
        if(e.KeyCode==Keys.Enter){Open(SelectedId??nodes[0].Mark.Id);e.Handled=true;return;}
        if(e.KeyCode!=Keys.Left&&e.KeyCode!=Keys.Right)return;
        int old=nodes.FindIndex(n=>n.Mark.Id==SelectedId);
        int next=Math.Max(0,Math.Min(nodes.Count-1,old+(e.KeyCode==Keys.Right?1:-1)));
        var node=nodes[next];SelectedId=node.Mark.Id;
        if(!ClientRectangle.Contains(ScreenRect(node.Hit)))AutoScrollPosition=new Point(0,Math.Max(0,node.Dot.Top-ClientSize.Height/2));
        Invalidate();e.Handled=true;
    }
    void ClearPreview() {
        hovering=null;
        if(IsHandleCreated)tip.Hide(this);
        if(thumbnail!=null){thumbnail.Dispose();thumbnail=null;}
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        string id=Hit(e.Location);
        if(id==hovering)return;
        ClearPreview();
        hovering=id;Cursor=id==null?Cursors.Default:Cursors.Hand;Invalidate();
        if(id==null)return;
        var mark=nodes.First(n=>n.Mark.Id==id).Mark;
        caption=PlayerText.Time(mark)+"   "+PlayerText.Kind(mark)+"\n"+(String.IsNullOrWhiteSpace(Review.Note(id))?"尚未填写备注":Review.Note(id));
        using(var source=PlayerText.Screenshot(Review.Path,id)){
            if(source!=null){
                thumbnail=new Bitmap(U(290),U(163));
                using(var g=Graphics.FromImage(thumbnail)){
                    g.Clear(SystemColors.Info);
                    double ratio=Math.Min((double)thumbnail.Width/source.Width,(double)thumbnail.Height/source.Height);
                    int w=Math.Max(1,(int)(source.Width*ratio)),h=Math.Max(1,(int)(source.Height*ratio));
                    g.DrawImage(source,(thumbnail.Width-w)/2,(thumbnail.Height-h)/2,w,h);
                }
            }
        }
        tip.Show(caption,this,new Point(Math.Min(e.X,Math.Max(0,Width-U(306))),e.Y+U(18)),15000);
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);ClearPreview();Invalidate();}
    protected override void Dispose(bool disposing){if(disposing){ClearPreview();tip.Dispose();}base.Dispose(disposing);}
}
}
