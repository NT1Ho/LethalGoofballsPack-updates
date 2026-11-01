using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LcPack {
internal sealed class AdminItem {
    public string[] Cells,Paths;
    public AdminItem(string[] cells,params string[] paths){Cells=cells;Paths=paths;}
}
internal sealed class AdminGrid:DataGridView {
    List<AdminItem> rows=new List<AdminItem>();
    public AdminGrid(){
        DoubleBuffered=true;VirtualMode=true;ReadOnly=true;RowHeadersVisible=false;
        Dock=DockStyle.Fill;BackgroundColor=Color.White;BorderStyle=BorderStyle.FixedSingle;
        AllowUserToAddRows=false;AllowUserToDeleteRows=false;AllowUserToResizeRows=false;
        SelectionMode=DataGridViewSelectionMode.FullRowSelect;AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;GridColor=Color.FromArgb(230,234,238);
        ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        DefaultCellStyle.Padding=new Padding(4,2,3,2);
        CellValueNeeded+=(s,e)=>{if(e.RowIndex>=0&&e.RowIndex<rows.Count&&e.ColumnIndex<rows[e.RowIndex].Cells.Length)e.Value=rows[e.RowIndex].Cells[e.ColumnIndex];};
    }
    public void Headers(string[] names,int[] widths){
        RowCount=0;Columns.Clear();rows.Clear();
        for(int i=0;i<names.Length;i++)Columns.Add(new DataGridViewTextBoxColumn{HeaderText=names[i],FillWeight=widths[i],MinimumWidth=45,SortMode=DataGridViewColumnSortMode.NotSortable});
        RowTemplate.Height=Math.Max(26,Font.Height+10);
    }
    public void ShowRows(IEnumerable<AdminItem> data){rows=data.ToList();RowCount=rows.Count;ClearSelection();Invalidate();}
    public string[] SelectedPaths(){return SelectedRows.Cast<DataGridViewRow>().Where(r=>r.Index>=0&&r.Index<rows.Count).SelectMany(r=>rows[r.Index].Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
}
internal static class AdminControls {
    public static FlowLayoutPanel Bar(){return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty,Padding=new Padding(0,3,0,3)};}
    public static Label Label(string text){return new Label{Text=text,AutoSize=true,Margin=new Padding(4,8,8,3)};}
    public static Button Button(string text,Action click){
        var b=new Button{Text=text,AutoSize=true,Padding=new Padding(5,1,5,1),Margin=new Padding(2,2,4,2)};
        b.Click+=(s,e)=>click();return b;
    }
    public static TableLayoutPanel Page(params Control[] controls){
        var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=controls.Length,Padding=new Padding(8)};
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<controls.Length;i++){bool fill=controls[i] is AdminGrid||controls[i] is DataGridView;
            t.RowStyles.Add(fill?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));t.Controls.Add(controls[i],0,i);}
        return t;
    }
    public static string Prompt(Form parent,string title,string label,string value){
        using(var f=new Form{Text=title,ClientSize=new Size(510,112),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=parent.Font,StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi}){
            var box=new TextBox{Dock=DockStyle.Fill,Text=value};var bar=Bar();var ok=Button("确定",()=>{f.DialogResult=DialogResult.OK;});var cancel=Button("取消",()=>{f.DialogResult=DialogResult.Cancel;});
            bar.Controls.Add(ok);bar.Controls.Add(cancel);f.Controls.Add(Page(Label(label),box,bar));f.AcceptButton=ok;f.CancelButton=cancel;
            return f.ShowDialog(parent)==DialogResult.OK?box.Text:null;
        }
    }
}
}
