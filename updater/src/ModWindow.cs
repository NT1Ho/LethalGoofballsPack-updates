using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using D=System.Windows.Data;

namespace LcPack {
public class ModWindow:Form {
    static readonly M.Brush accent=new M.SolidColorBrush(M.Color.FromRgb(0xA8,0x65,0x2A));
    readonly Label summary=new Label();readonly CheckBox dependencies=new CheckBox();
    readonly ElementHost host=new ElementHost();readonly C.ListView list=new C.ListView();readonly C.GridView view=new C.GridView();
    List<InstalledMod> rows=new List<InstalledMod>();
    PageContent pages;
    double preferredName=180,preferredVersion=85;
    public ModWindow(string root,PageContent presentation=null,List<InstalledMod> preview=null) {
        pages=presentation;
        SuspendLayout();DoubleBuffered=true;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        Text="本地 MOD 清单";Font=new Font("Microsoft YaHei UI",9);ClientSize=new Size(700,500);MinimumSize=new Size(660,380);StartPosition=FormStartPosition.CenterParent;
        using(var icon=typeof(ModWindow).Assembly.GetManifestResourceStream("settings.ico"))if(icon!=null)Icon=new Icon(icon);
        summary.Dock=DockStyle.Top;summary.Height=40;summary.Padding=new Padding(12,10,0,0);summary.Text="正在扫描本地 MOD……";
        list.View=view;view.AllowsColumnReorder=false;list.SelectionMode=C.SelectionMode.Single;
        list.Background=M.Brushes.White;list.Foreground=M.Brushes.Black;list.BorderBrush=M.Brushes.Gray;list.BorderThickness=new W.Thickness(1);
        list.FontFamily=new M.FontFamily("Microsoft YaHei UI");list.FontSize=12;list.UseLayoutRounding=true;list.SnapsToDevicePixels=true;
        C.VirtualizingPanel.SetIsVirtualizing(list,true);C.VirtualizingPanel.SetVirtualizationMode(list,C.VirtualizationMode.Recycling);
        C.VirtualizingPanel.SetScrollUnit(list,C.ScrollUnit.Pixel);
        C.ScrollViewer.SetHorizontalScrollBarVisibility(list,C.ScrollBarVisibility.Disabled);
        M.TextOptions.SetTextFormattingMode(list,M.TextFormattingMode.Display);
        var rowStyle=new W.Style(typeof(C.ListViewItem));rowStyle.Setters.Add(new W.Setter(C.Control.PaddingProperty,new W.Thickness(0)));rowStyle.Setters.Add(new W.Setter(W.FrameworkElement.MinHeightProperty,23.0));rowStyle.Setters.Add(new W.Setter(C.Control.HorizontalContentAlignmentProperty,W.HorizontalAlignment.Stretch));var rowBorder=new W.FrameworkElementFactory(typeof(C.Border));rowBorder.SetValue(C.Border.BackgroundProperty,new W.TemplateBindingExtension(C.Control.BackgroundProperty));
        rowBorder.AppendChild(RowPresenter());
        rowStyle.Setters.Add(new W.Setter(C.Control.TemplateProperty,new C.ControlTemplate(typeof(C.ListViewItem)){VisualTree=rowBorder}));
        var hao=new W.DataTrigger{Binding=new D.Binding("IsHao"),Value=true};hao.Setters.Add(new W.Setter(C.Control.BackgroundProperty,M.Brushes.WhiteSmoke));hao.Setters.Add(new W.Setter(C.Control.TemplateProperty,HaoRowTemplate()));rowStyle.Triggers.Add(hao);
        var selectedRow=new W.Trigger{Property=C.ListViewItem.IsSelectedProperty,Value=true};selectedRow.Setters.Add(new W.Setter(C.Control.BackgroundProperty,W.SystemColors.HighlightBrush));selectedRow.Setters.Add(new W.Setter(C.Control.ForegroundProperty,W.SystemColors.HighlightTextBrush));rowStyle.Triggers.Add(selectedRow);list.ItemContainerStyle=rowStyle;
        var header=new W.Style(typeof(C.GridViewColumnHeader));header.Setters.Add(new W.Setter(C.Control.BackgroundProperty,M.Brushes.White));header.Setters.Add(new W.Setter(C.Control.PaddingProperty,new W.Thickness(6,4,6,4)));header.Setters.Add(new W.Setter(C.Control.HorizontalContentAlignmentProperty,W.HorizontalAlignment.Left));view.ColumnHeaderContainerStyle=header;
        var nameColumn=AddColumn("MOD","Name",200);nameColumn.CellTemplate=NameTemplate();var badge=AddColumn("","New",48);AddColumn("版本","Version",80);var purpose=AddColumn("用途","Purpose",230);purpose.CellTemplate=FeatureTemplate();
        var text=new W.FrameworkElementFactory(typeof(C.TextBlock));text.SetBinding(C.TextBlock.TextProperty,new D.Binding("New"));
        var green=new W.Style(typeof(C.TextBlock));green.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,M.Brushes.SeaGreen));
        var selected=new W.DataTrigger{Binding=new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},Value=true};selected.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,W.SystemColors.HighlightTextBrush));green.Triggers.Add(selected);text.SetValue(W.FrameworkElement.StyleProperty,green);
        badge.DisplayMemberBinding=null;badge.CellTemplate=new W.DataTemplate{VisualTree=text};
        list.SizeChanged+=(s,e)=>ResizeColumns();
        host.Dock=DockStyle.Fill;host.BackColor=SystemColors.Window;host.Child=list;
        dependencies.Text="显示依赖组件";dependencies.Dock=DockStyle.Bottom;dependencies.Height=32;dependencies.Padding=new Padding(12,0,0,0);dependencies.CheckedChanged+=(s,e)=>RefreshRows();
        Controls.Add(host);Controls.Add(summary);Controls.Add(dependencies);ResumeLayout(true);
        Shown+=async(s,e)=>{
            try{pages=pages??PageContent.ForRoot(root);list.FontSize=13;rows=preview??await Task.Run(()=>ModInventory.Scan(root,false));if(!IsDisposed)RefreshRows();}
            catch(Exception ex){if(!IsDisposed)summary.Text="扫描失败："+ex.Message;}
        };
    }
    C.GridViewColumn AddColumn(string title,string property,double width){
        var text=new W.FrameworkElementFactory(typeof(C.TextBlock));text.SetBinding(C.TextBlock.TextProperty,new D.Binding(property));text.SetValue(C.TextBlock.TextWrappingProperty,W.TextWrapping.Wrap);text.SetBinding(W.FrameworkElement.ToolTipProperty,new D.Binding(property=="Version"?"VersionHint":property));
        if(property=="Version"){
            var style=new W.Style(typeof(C.TextBlock));style.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,new D.Binding("VersionBrush")));
            var selected=new W.DataTrigger{Binding=new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},Value=true};selected.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,W.SystemColors.HighlightTextBrush));style.Triggers.Add(selected);text.SetValue(W.FrameworkElement.StyleProperty,style);
        }
        var column=new C.GridViewColumn{Header=title,CellTemplate=new W.DataTemplate{VisualTree=text},Width=width};view.Columns.Add(column);return column;
    }
    static W.FrameworkElementFactory RowPresenter(){
        var presenter=new W.FrameworkElementFactory(typeof(C.GridViewRowPresenter));presenter.SetBinding(C.GridViewRowPresenter.ContentProperty,new D.Binding("Content"){RelativeSource=D.RelativeSource.TemplatedParent});presenter.SetBinding(C.GridViewRowPresenter.ColumnsProperty,new D.Binding("View.Columns"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListView),1)});presenter.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);return presenter;
    }
    static C.ControlTemplate HaoRowTemplate(){
        var root=new W.FrameworkElementFactory(typeof(C.StackPanel));var main=new W.FrameworkElementFactory(typeof(C.Border));main.SetValue(C.Border.BackgroundProperty,new W.TemplateBindingExtension(C.Control.BackgroundProperty));
        var presenter=RowPresenter();presenter.SetValue(W.FrameworkElement.MinHeightProperty,23.0);main.AppendChild(presenter);root.AppendChild(main);
        var detail=new W.FrameworkElementFactory(typeof(C.Border));detail.SetValue(C.Border.BackgroundProperty,M.Brushes.WhiteSmoke);detail.SetValue(C.Border.PaddingProperty,new W.Thickness(12,4,12,6));detail.SetBinding(W.UIElement.VisibilityProperty,new D.Binding("IsExpanded"){Converter=new C.BooleanToVisibilityConverter()});
        var content=new W.FrameworkElementFactory(typeof(C.ContentPresenter));content.SetBinding(C.ContentPresenter.ContentProperty,new D.Binding("Content"){RelativeSource=D.RelativeSource.TemplatedParent});content.SetValue(C.ContentPresenter.ContentTemplateProperty,FeatureTemplate(true));detail.AppendChild(content);root.AppendChild(detail);
        return new C.ControlTemplate(typeof(C.ListViewItem)){VisualTree=root};
    }
    static W.DataTemplate NameTemplate(){
        var line=new W.FrameworkElementFactory(typeof(C.DockPanel));
        var arrow=new W.FrameworkElementFactory(typeof(C.Primitives.ToggleButton));arrow.SetValue(C.DockPanel.DockProperty,C.Dock.Left);arrow.SetValue(W.FrameworkElement.FocusVisualStyleProperty,null);arrow.SetBinding(C.Control.ForegroundProperty,new D.Binding("Foreground"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)});arrow.SetValue(W.FrameworkElement.WidthProperty,18.0);arrow.SetValue(W.FrameworkElement.HeightProperty,23.0);arrow.SetValue(W.FrameworkElement.ToolTipProperty,"展开/收起功能");arrow.SetBinding(W.UIElement.VisibilityProperty,new D.Binding("IsHao"){Converter=new C.BooleanToVisibilityConverter()});arrow.SetBinding(C.Primitives.ToggleButton.IsCheckedProperty,new D.Binding("IsExpanded"){Mode=D.BindingMode.TwoWay});
        var glyph=new W.FrameworkElementFactory(typeof(C.TextBlock));glyph.SetBinding(C.TextBlock.TextProperty,new D.Binding("Arrow"));glyph.SetValue(W.FrameworkElement.HorizontalAlignmentProperty,W.HorizontalAlignment.Center);glyph.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);arrow.SetValue(C.Control.TemplateProperty,new C.ControlTemplate(typeof(C.Primitives.ToggleButton)){VisualTree=glyph});line.AppendChild(arrow);
        var name=new W.FrameworkElementFactory(typeof(C.TextBlock));name.SetBinding(C.TextBlock.TextProperty,new D.Binding("Name"));name.SetBinding(W.FrameworkElement.ToolTipProperty,new D.Binding("Name"));name.SetValue(C.TextBlock.TextWrappingProperty,W.TextWrapping.Wrap);name.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);
        var style=new W.Style(typeof(C.TextBlock));var hao=new W.DataTrigger{Binding=new D.Binding("IsOwn"),Value=true};hao.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,accent));style.Triggers.Add(hao);
        var selected=new W.DataTrigger{Binding=new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},Value=true};selected.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,W.SystemColors.HighlightTextBrush));style.Triggers.Add(selected);name.SetValue(W.FrameworkElement.StyleProperty,style);line.AppendChild(name);return new W.DataTemplate{VisualTree=line};
    }
    static W.DataTemplate FeatureTemplate(bool detail=false){
        var features=new W.FrameworkElementFactory(typeof(C.ItemsControl));features.SetBinding(C.ItemsControl.ItemsSourceProperty,new D.Binding(detail?"DetailFeatures":"Features"));
        var line=new W.FrameworkElementFactory(typeof(C.DockPanel));line.SetValue(W.FrameworkElement.MinHeightProperty,23.0);
        var tag=new W.FrameworkElementFactory(typeof(C.TextBlock));tag.SetValue(C.DockPanel.DockProperty,C.Dock.Right);tag.SetValue(W.FrameworkElement.MarginProperty,new W.Thickness(8,0,8,0));tag.SetBinding(C.TextBlock.TextProperty,new D.Binding("Tag"));tag.SetBinding(W.UIElement.VisibilityProperty,new D.Binding("SelfDeveloped"){Converter=new C.BooleanToVisibilityConverter()});tag.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);line.AppendChild(tag);
        var text=new W.FrameworkElementFactory(typeof(C.TextBlock));text.SetBinding(C.TextBlock.TextProperty,new D.Binding("Text"));text.SetBinding(W.FrameworkElement.ToolTipProperty,new D.Binding("Text"));text.SetValue(C.TextBlock.TextWrappingProperty,W.TextWrapping.Wrap);text.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);
        var style=new W.Style(typeof(C.TextBlock));if(detail)style.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,M.Brushes.Black));var own=new W.DataTrigger{Binding=new D.Binding("SelfDeveloped"),Value=true};own.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,accent));style.Triggers.Add(own);
        if(!detail){
        var selected=new W.DataTrigger{Binding=new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},Value=true};selected.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,W.SystemColors.HighlightTextBrush));style.Triggers.Add(selected);
        var selectedOwn=new W.MultiDataTrigger();selectedOwn.Conditions.Add(new W.Condition(new D.Binding("SelfDeveloped"),true));selectedOwn.Conditions.Add(new W.Condition(new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},true));selectedOwn.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,accent));style.Triggers.Add(selectedOwn);
        }
        var tagStyle=new W.Style(typeof(C.TextBlock));tagStyle.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,accent));if(!detail){var selectedTag=new W.DataTrigger{Binding=new D.Binding("IsSelected"){RelativeSource=new D.RelativeSource(D.RelativeSourceMode.FindAncestor,typeof(C.ListViewItem),1)},Value=true};selectedTag.Setters.Add(new W.Setter(C.TextBlock.ForegroundProperty,accent));tagStyle.Triggers.Add(selectedTag);}tag.SetValue(W.FrameworkElement.StyleProperty,tagStyle);
        text.SetValue(W.FrameworkElement.StyleProperty,style);line.AppendChild(text);
        features.SetValue(C.ItemsControl.ItemTemplateProperty,new W.DataTemplate{VisualTree=line});return new W.DataTemplate{VisualTree=features};
    }
    void ResizeColumns(){
        double available=Math.Max(300,list.ActualWidth-48-W.SystemParameters.VerticalScrollBarWidth-6);
        double name=Math.Min(available*.42,Math.Max(145,preferredName));
        double version=Math.Min(available*.22,Math.Max(75,preferredVersion));
        double purpose=Math.Max(1,available-name-version);
        if(view.Columns[0].Width!=name)view.Columns[0].Width=name;
        if(view.Columns[2].Width!=version)view.Columns[2].Width=version;
        if(view.Columns[3].Width!=purpose)view.Columns[3].Width=purpose;
    }
    void RefreshRows(){
        if(pages==null)return;
        var visible=rows.Where(r=>dependencies.Checked||r.kind=="MOD"||r.kind=="额外插件"||r.kind=="未识别 DLL"||r.kind=="安装记录"||r.kind=="目录").ToList();
        visible=pages.DisplayOrder(visible,r=>r.name,r=>pages.Find(r.name),r=>r.isNew).ToList();
        var display=visible.Select(r=>new ModRow{Name=(ModNames.OwnRank(r.name)==4?"Universal Wrench":r.name=="MoreCounterplay"?"H增强版 MoreCounterplay":r.name)+(r.status=="未找到运行文件"?"（未找到）":""),New=r.isNew?"NEW":"",Version=r.version.StartsWith("未确认")?"待核对":r.version,VersionHint=r.version+"\n"+r.status,Purpose=ModNames.Description(r.name),IsOwn=ModNames.OwnRank(r.name)>0,IsHao=r.name=="MoreCounterplay",DetailFeatures=r.name=="MoreCounterplay"?ModNames.Features(r.name):null,Features=r.name=="MoreCounterplay"?new[]{new ModFeature{Text=ModNames.Description(r.name)}}:ModNames.Features(r.name)}).ToList();
        if(pages!=null){
            for(int i=0;i<visible.Count;i++){
                var edit=pages.Find(visible[i].name);if(edit==null)continue;var row=display[i];row.Name=edit.Name+(visible[i].status=="未找到运行文件"?"（未找到）":"");if(edit.New.HasValue)row.New=edit.New.Value?"NEW":"";row.Purpose=edit.Purpose;row.IsOwn=edit.Own;
                if(!String.IsNullOrEmpty(edit.VersionColor))row.VersionBrush=new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(edit.VersionColor));
                row.DetailFeatures=edit.Features();row.IsHao=row.DetailFeatures.Length>0;row.Features=new[]{new ModFeature{Text=edit.Purpose,SelfDeveloped=edit.Own}};
            }
        }
        var measure=new C.TextBlock{FontSize=list.FontSize,FontFamily=list.FontFamily};
        Func<string,double> width=text=>{measure.Text=text??"";measure.Measure(new W.Size(Double.PositiveInfinity,Double.PositiveInfinity));return measure.DesiredSize.Width;};
        var names=display.Select(r=>width(r.Name)+32).OrderBy(v=>v).ToArray();
        preferredName=names.Length==0?180:names[(names.Length-1)*3/4];
        preferredVersion=display.Select(r=>width(r.Version)+18).DefaultIfEmpty(85).Max();
        list.ItemsSource=display;ResizeColumns();
        summary.Text="本地清单："+visible.Count+" 项"+(dependencies.Checked?"（含依赖）":"  依赖已收起");
    }
    public class ModRow:INotifyPropertyChanged {
        public M.Brush VersionBrush{get;set;}
        public ModRow(){VersionBrush=M.Brushes.Black;}
        public string Name{get;set;}public string New{get;set;}public string Version{get;set;}public string VersionHint{get;set;}public string Purpose{get;set;}public ModFeature[] Features{get;set;}public ModFeature[] DetailFeatures{get;set;}public bool IsHao{get;set;}public bool IsOwn{get;set;}
        bool expanded;public bool IsExpanded{get{return expanded;}set{if(expanded==value)return;expanded=value;if(PropertyChanged!=null){PropertyChanged(this,new PropertyChangedEventArgs("IsExpanded"));PropertyChanged(this,new PropertyChangedEventArgs("Arrow"));}}}
        public string Arrow{get{return IsExpanded?"▼":"▶";}}public event PropertyChangedEventHandler PropertyChanged;
    }
}
}
