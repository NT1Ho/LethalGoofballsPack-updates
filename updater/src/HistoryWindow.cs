using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using W=System.Windows;
using C=System.Windows.Controls;
using D=System.Windows.Data;
using M=System.Windows.Media;

namespace LcPack {
public class HistoryEntry {
    public string Kind{get;set;}public string Mod{get;set;}public string Summary{get;set;}
}
public class HistoryRelease {
    public string Version{get;set;}public string PublishedUtc{get;set;}public List<HistoryEntry> Changes{get;set;}
}
public class HistoryWindow:Form {
    bool fitQueued,resizing,fitting,centerAfterFit;
    public static List<HistoryRelease> ReadHistory(){
        using(var stream=typeof(HistoryWindow).Assembly.GetManifestResourceStream("update-history.json"))
        using(var reader=new StreamReader(stream))return Data.Read<List<HistoryRelease>>(reader.ReadToEnd());
    }
    public HistoryWindow(List<HistoryRelease> supplied=null,PageContent presentation=null){
        Text="历史更新记录";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        ClientSize=new Size(880,280);MinimumSize=new Size(720,160);StartPosition=FormStartPosition.CenterParent;
        using(var icon=typeof(HistoryWindow).Assembly.GetManifestResourceStream("settings.ico"))if(icon!=null)Icon=new Icon(icon);
        var releases=supplied??ReadHistory();
        var panel=new C.Grid{Background=M.Brushes.White,UseLayoutRounding=true,SnapsToDevicePixels=true};
        panel.RowDefinitions.Add(new C.RowDefinition{Height=W.GridLength.Auto});
        panel.RowDefinitions.Add(new C.RowDefinition{Height=new W.GridLength(1,W.GridUnitType.Star)});
        panel.SetValue(C.Control.FontFamilyProperty,new M.FontFamily("Microsoft YaHei UI"));panel.SetValue(C.Control.FontSizeProperty,presentation==null?13.0:(double)presentation.FontSize);
        M.TextOptions.SetTextFormattingMode(panel,M.TextFormattingMode.Display);
        var top=new C.StackPanel{Orientation=C.Orientation.Horizontal,Margin=new W.Thickness(12,10,12,10)};
        var versions=new C.ComboBox{Width=120,ItemsSource=releases,DisplayMemberPath="Version"};
        var date=new C.TextBlock{VerticalAlignment=W.VerticalAlignment.Center,Margin=new W.Thickness(16,0,0,0)};
        top.Children.Add(versions);top.Children.Add(date);panel.Children.Add(top);
        var table=new C.DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserReorderColumns=false,CanUserSortColumns=false,
            HeadersVisibility=C.DataGridHeadersVisibility.Column,SelectionMode=C.DataGridSelectionMode.Single,SelectionUnit=C.DataGridSelectionUnit.FullRow,
            GridLinesVisibility=C.DataGridGridLinesVisibility.Horizontal,HorizontalGridLinesBrush=new M.SolidColorBrush(M.Color.FromRgb(232,232,232)),
            Background=M.Brushes.White,BorderThickness=new W.Thickness(0,1,0,1),BorderBrush=M.Brushes.LightGray,RowHeaderWidth=0,
            EnableRowVirtualization=false,EnableColumnVirtualization=true,AlternatingRowBackground=new M.SolidColorBrush(M.Color.FromRgb(248,249,250))};
        C.ScrollViewer.SetCanContentScroll(table,false);
        C.ScrollViewer.SetHorizontalScrollBarVisibility(table,C.ScrollBarVisibility.Disabled);
        var cells=new W.Style(typeof(C.TextBlock));cells.Setters.Add(new W.Setter(C.TextBlock.TextWrappingProperty,W.TextWrapping.Wrap));cells.Setters.Add(new W.Setter(W.FrameworkElement.MarginProperty,new W.Thickness(8,6,8,6)));
        var headers=new W.Style(typeof(C.Primitives.DataGridColumnHeader));headers.Setters.Add(new W.Setter(C.Control.PaddingProperty,new W.Thickness(8,7,8,7)));headers.Setters.Add(new W.Setter(C.Control.BackgroundProperty,M.Brushes.WhiteSmoke));table.ColumnHeaderStyle=headers;
        table.ColumnHeaderHeight=32;
        table.Columns.Add(new C.DataGridTextColumn{Header="类型",Binding=new D.Binding("Kind"),Width=new C.DataGridLength(8,C.DataGridLengthUnitType.Star),MinWidth=56,MaxWidth=96,ElementStyle=cells});
        table.Columns.Add(new C.DataGridTextColumn{Header="MOD",Binding=new D.Binding("Mod"),Width=new C.DataGridLength(presentation==null?32:presentation.HistoryModPercent,C.DataGridLengthUnitType.Star),MinWidth=200,ElementStyle=cells});
        table.Columns.Add(new C.DataGridTextColumn{Header="更新内容",Binding=new D.Binding("Summary"),Width=new C.DataGridLength(presentation==null?60:92-presentation.HistoryModPercent,C.DataGridLengthUnitType.Star),MinWidth=250,ElementStyle=cells});
        C.Grid.SetRow(table,1);panel.Children.Add(table);
        Action fit=()=>QueueFit(table,top);
        versions.SelectionChanged+=(s,e)=>{var item=versions.SelectedItem as HistoryRelease;if(item==null)return;
            centerAfterFit=true;
            date.Text="更新时间："+DateTimeOffset.Parse(item.PublishedUtc,System.Globalization.CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm")+"  ·  "+item.Changes.Count+" 项";
            table.ItemsSource=item.Changes;table.SelectedIndex=0;if(table.SelectedItem!=null)table.ScrollIntoView(table.SelectedItem);
            fit();
        };
        table.SizeChanged+=(s,e)=>{if(e.WidthChanged)fit();};
        table.ItemContainerGenerator.StatusChanged+=(s,e)=>{if(table.ItemContainerGenerator.Status==C.Primitives.GeneratorStatus.ContainersGenerated)fit();};
        Shown+=(s,e)=>fit();DpiChanged+=(s,e)=>fit();
        ResizeBegin+=(s,e)=>resizing=true;ResizeEnd+=(s,e)=>{resizing=false;fit();};
        var host=new ElementHost{Dock=DockStyle.Fill,Child=panel};Controls.Add(host);versions.SelectedIndex=0;
    }
    void QueueFit(C.DataGrid table,C.StackPanel top){
        if(fitQueued||resizing||fitting||IsDisposed)return;fitQueued=true;
        table.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{
            fitQueued=false;if(IsDisposed||!IsHandleCreated||resizing||WindowState!=FormWindowState.Normal)return;
            table.UpdateLayout();FitHeight(table,top);
        }));
    }
    double ContentHeight(C.DataGrid table,C.StackPanel top){
        double height=top.DesiredSize.Height+table.ColumnHeaderHeight+table.BorderThickness.Top+table.BorderThickness.Bottom;
        // History has only a few dozen rows; use their real layout instead of estimating wrapped text.
        for(int i=0;i<table.Items.Count;i++){
            var row=table.ItemContainerGenerator.ContainerFromIndex(i) as C.DataGridRow;
            if(row==null||row.ActualHeight<=0)return Double.NaN;
            height+=row.ActualHeight;
        }
        return height;
    }
    void FitHeight(C.DataGrid table,C.StackPanel top){
        if(table.ActualWidth<=0)return;
        double content=ContentHeight(table,top);if(Double.IsNaN(content))return;
        var area=Screen.FromControl(this).WorkingArea;int frame=Height-ClientSize.Height;
        int maximum=Math.Max(1,area.Height-frame-32);
        int desired=Math.Min(maximum,Math.Max(MinimumSize.Height-frame,(int)Math.Ceiling(content*DeviceDpi/96.0)));
        fitting=true;try{if(ClientSize.Height!=desired)ClientSize=new Size(ClientSize.Width,desired);
            if(centerAfterFit){Location=new Point(area.Left+Math.Max(0,(area.Width-Width)/2),area.Top+Math.Max(0,(area.Height-Height)/2));centerAfterFit=false;}
            else if(Visible)Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));
        }finally{fitting=false;}
    }
}
}
