using System;
using System.Drawing;
using System.Windows.Forms;

namespace LcPack {
internal static class DiagnosticDialogs {
    static Form Dialog(string title,Size size) {
        var form=new Form { Text=title, Font=new Font("Microsoft YaHei UI",9), ClientSize=size,
            MinimumSize=new Size(size.Width*3/4,size.Height*3/4),StartPosition=FormStartPosition.CenterParent,
            ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96) };
        return form;
    }
    static Button ActionButton(string text,DialogResult result) {
        return new Button { Text=text,AutoSize=true,DialogResult=result,Padding=new Padding(4,1,4,1) };
    }
    static Label Heading(string text) {
        return new Label { Text=text,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,5,0,5) };
    }
    static TableLayoutPanel Layout(Form form) {
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        form.Controls.Add(layout);return layout;
    }
    static FlowLayoutPanel Actions() {
        return new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false };
    }
    public static void EditMark(Form owner,DiagnosticReview review,DiagnosticMark mark) {
        bool editable=!DiagnosticsStore.Active(review.Path);
        using(var image=PlayerText.Screenshot(review.Path,mark.Id))
        using(var form=Dialog(PlayerText.Kind(mark)+" · "+PlayerText.Time(mark),new Size(580,image==null?300:510))) {
            var layout=Layout(form);
            layout.RowCount=5;
            layout.RowStyles.Add(new RowStyle(image==null?SizeType.Absolute:SizeType.Percent,image==null?28:64));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,image==null?100:36));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if(image==null)layout.Controls.Add(Heading(mark.Manual?"现场截图未取得":"自动异常现场"),0,0);
            else {
                var picture=new PictureBox { Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image,Cursor=Cursors.Hand,AccessibleName="打开现场大图" };
                picture.Click+=(s,e)=>LargePhoto(form,image);
                layout.Controls.Add(picture,0,0);
            }
            var facts=new TextBox { Multiline=true,TabStop=false,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=form.BackColor,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=PlayerText.Summary(mark),Margin=new Padding(5,8,0,3) };
            layout.Controls.Add(facts,0,1);
            layout.Controls.Add(Heading(editable?"玩家备注":"玩家备注（本局记录中）"),0,2);
            var note=new TextBox { Name="note",Dock=DockStyle.Fill,Multiline=true,MaxLength=8000,ScrollBars=ScrollBars.Vertical,ReadOnly=!editable,Text=review.Note(mark.Id) };
            layout.Controls.Add(note,0,3);
            var actions=Actions();
            var apply=ActionButton(editable?"应用到时间轴":"关闭",DialogResult.OK);
            actions.Controls.Add(apply);
            if(editable)actions.Controls.Add(ActionButton("取消",DialogResult.Cancel));
            if(image!=null){
                var photo=ActionButton("查看大图",DialogResult.None);
                photo.Click+=(s,e)=>LargePhoto(form,image);actions.Controls.Add(photo);
            }
            if(editable&&mark.Manual){
                string label=review.Deleted(mark.Id)?(review.SavedDeleted(mark.Id)?"恢复标记":"撤销删除"):"标为待删除";
                actions.Controls.Add(ActionButton(label,DialogResult.Retry));
            }
            layout.Controls.Add(actions,0,4);
            form.CancelButton=actions.Controls.Count>1&&editable ? (Button)actions.Controls[1] : apply;
            form.ActiveControl=apply;
            var result=form.ShowDialog(owner);
            if(!editable||(result!=DialogResult.OK&&result!=DialogResult.Retry))return;
            if(DiagnosticsStore.Active(review.Path))throw new System.IO.IOException("本局正在记录，暂时只能查看。");
            review.SetNote(mark.Id,note.Text);
            if(result==DialogResult.Retry)review.SetDeleted(mark.Id,!review.Deleted(mark.Id));
        }
    }
    static void LargePhoto(Form owner,Image image) {
        using(var form=Dialog("现场截图",new Size(960,580))) {
            form.Controls.Add(new PictureBox { Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image,BackColor=Color.FromArgb(34,34,34) });
            form.KeyPreview=true;form.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)form.Close();};
            form.ShowDialog(owner);
        }
    }
    public static void SessionOptions(Form owner,DiagnosticReview review) {
        bool live=DiagnosticsStore.Active(review.Path);
        using(var form=Dialog("游戏记录设置",new Size(460,245))) {
            var layout=Layout(form);
            var group=new TextBox { Text=review.Note("group"),Dock=DockStyle.Top,MaxLength=100,ReadOnly=live };
            var note=new TextBox { Text=review.Note("sessionNote"),Dock=DockStyle.Fill,Multiline=true,MaxLength=8000,ReadOnly=live };
            var keep=new CheckBox { Text="保留这局记录",AutoSize=true,Enabled=!live,Checked=Convert.ToBoolean(DiagnosticsStore.Value(review.Notes,"pinned")??false) };
            layout.RowCount=6;
            for(int i=0;i<6;i++)layout.RowStyles.Add(i==3?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Heading("共同联机编号（与朋友填写相同的编号）"));
            layout.Controls.Add(group);layout.Controls.Add(Heading("整局备注"));layout.Controls.Add(note);layout.Controls.Add(keep);
            var actions=Actions();actions.Controls.Add(ActionButton(live?"关闭":"应用到时间轴",DialogResult.OK));
            if(!live)actions.Controls.Add(ActionButton("取消",DialogResult.Cancel));
            layout.Controls.Add(actions);
            if(form.ShowDialog(owner)!=DialogResult.OK||live)return;
            review.SetNote("group",group.Text);review.SetNote("sessionNote",note.Text);
            if(keep.Checked!=Convert.ToBoolean(DiagnosticsStore.Value(review.Notes,"pinned")??false))review.Notes["pinned"]=keep.Checked;
        }
    }
}
}
