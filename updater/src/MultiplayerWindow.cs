using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LcPack {
internal static class MultiplayerWindow {
    internal static void Show(Form owner,DiagnosticReview review,string markId) {
        using(var form=new Form{ Text="跨端事件",Font=owner.Font,ClientSize=new Size(1060,620),MinimumSize=new Size(790,490),
            StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi,ShowInTaskbar=false }) {
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(10)};
            form.Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,28));
            var summary=Label("正在读取诊断记录…");layout.Controls.Add(summary,0,0);
            var strip=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };layout.Controls.Add(strip,0,1);
            var peer=Filter(strip,"玩家／角色",129);
            var mod=Filter(strip,"MOD",147);
            var category=Filter(strip,"类别",102);
            var status=Filter(strip,"状态",96);
            var mark=Filter(strip,"F8",130);
            var find=new TextBox {Width=142};strip.Controls.Add(Label("关联编号／搜索"));strip.Controls.Add(find);
            var from=new TextBox {Width=48};var to=new TextBox {Width=48};
            strip.Controls.Add(Label("联机秒数"));strip.Controls.Add(from);strip.Controls.Add(to);
            var grid=new DataGridView {Name="multiplayerRows",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
                RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,VirtualMode=true};
            string[] columns={"联机时间","来源","MOD","类别／事件","状态","关联编号"};
            int[] widths={90,135,180,222,102,220};
            for(int i=0;i<columns.Length;i++){grid.Columns.Add("event"+i,columns[i]);grid.Columns[i].Width=widths[i];}
            layout.Controls.Add(grid,0,2);
            var detail=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=true};
            layout.Controls.Add(detail,0,3);
            MultiplayerTimeline data=null;
            var shown=new List<MultiplayerEvent>();
            Action refresh=()=>{
                if(data==null)return;
                double lo,hi;
                if(!Double.TryParse(from.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out lo))lo=Double.NegativeInfinity;
                if(!Double.TryParse(to.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out hi))hi=Double.PositiveInfinity;
                string query=find.Text.Trim();
                shown=data.Rows.Where(r=>Matches(peer,r.ClientLabel)&&Matches(mod,r.Source)&&Matches(category,r.Category)&&Matches(status,r.Status)&&
                    (mark.SelectedIndex<=0||r.Marks.Contains((string)mark.SelectedItem))&&r.Time>=lo&&r.Time<=hi&&
                    (query.Length==0||r.Link.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||
                     r.Detail.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||r.Code.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
                grid.RowCount=shown.Count;grid.ClearSelection();grid.Invalidate();detail.Clear();
                summary.Text=(data.Available?data.Summary():"这局没有跨端事件。旧版现场仍可查看与导出。")+"\r\n当前筛选 "+shown.Count+" 条";
            };
            grid.CellValueNeeded+=(s,e)=>{
                if(e.RowIndex<0||e.RowIndex>=shown.Count)return;
                var r=shown[e.RowIndex];
                switch(e.ColumnIndex){
                    case 0:e.Value=r.Time<0?"未对齐":r.Time.ToString("F3");break;
                    case 1:e.Value=r.ClientLabel;break;
                    case 2:e.Value=r.Source;break;
                    case 3:e.Value=r.Category+" / "+r.Code;break;
                    case 4:e.Value=r.Status;break;
                    case 5:e.Value=r.Link;break;
                }
            };
            grid.SelectionChanged+=(s,e)=>{
                int i=grid.CurrentCell==null?-1:grid.CurrentCell.RowIndex;
                if(i<0||i>=shown.Count||grid.SelectedRows.Count==0)return;
                var r=shown[i];
                detail.Text=r.Detail+"\r\n原始 UTC "+r.Utc+"    房主接收秒数 "+(r.ReceivedTime<0?"—":r.ReceivedTime.ToString("F3"))+
                    "\r\n房间 "+r.Room+"    连接 "+r.Client+"    来源 "+r.Peer+
                    "\r\nF8 "+String.Join(", ",r.Marks)+"    关联 "+r.Link;
            };
            grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.RowIndex<shown.Count&&shown[e.RowIndex].Link.Length>0){
                string link=shown[e.RowIndex].Link;
                peer.SelectedIndex=mod.SelectedIndex=category.SelectedIndex=status.SelectedIndex=mark.SelectedIndex=0;
                from.Clear();to.Clear();find.Text=link;
            }};
            foreach(var box in new[]{peer,mod,category,status,mark})box.SelectedIndexChanged+=(s,e)=>refresh();
            foreach(var box in new[]{find,from,to})box.TextChanged+=(s,e)=>refresh();
            form.Shown+=async(s,e)=>{
                try{
                    var loaded=await Task.Run(()=>MultiplayerTimeline.Load(review));
                    if(form.IsDisposed)return;
                    Fill(peer,loaded.Rows.Select(r=>r.ClientLabel));Fill(mod,loaded.Rows.Select(r=>r.Source));
                    Fill(category,loaded.Rows.Select(r=>r.Category));Fill(status,loaded.Rows.Select(r=>r.Status));
                    Fill(mark,review.Marks.Where(m=>m.Manual&&!review.Deleted(m.Id)).Select(m=>m.Id));
                    if(markId!=null&&mark.Items.Contains(markId))mark.SelectedItem=markId;
                    data=loaded;refresh();
                }catch(Exception ex){if(!form.IsDisposed)summary.Text="无法读取跨端事件。"+ex.Message;}
            };
            form.ShowDialog(owner);
        }
    }
    static Label Label(string text){return new Label{Text=text,AutoSize=true,Margin=new Padding(3,6,3,4)};}
    static ComboBox Filter(FlowLayoutPanel strip,string label,int width) {
        var box=new ComboBox {Width=width,DropDownStyle=ComboBoxStyle.DropDownList,MaxDropDownItems=18};
        strip.Controls.Add(Label(label));strip.Controls.Add(box);return box;
    }
    static bool Matches(ComboBox box,string value){return box.SelectedIndex<=0||String.Equals((string)box.SelectedItem,value,StringComparison.Ordinal);}
    static void Fill(ComboBox box,IEnumerable<string> values){
        box.Items.Add("全部");
        foreach(string value in values.Where(x=>x.Length>0).Distinct().OrderBy(x=>x,StringComparer.CurrentCulture))box.Items.Add(value);
        box.SelectedIndex=0;
    }
}
}

