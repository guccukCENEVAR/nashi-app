using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
namespace Nashi.Linux;

/// Drop zones shown while files are dragged over the window. Wayland keeps the keyboard
/// with the app the drag started in, so drag chords may never reach Nashi there; the zones
/// open the same wheels without a modifier key (and work identically on X11).
internal sealed partial class MainWindow
{
    private static readonly (string Kind,string Title,string Symbol)[] DropZoneKinds=[("convert","Convert","⇄"),("tools","Tools","✦"),("actions","File actions","↗")];
    private readonly List<(string Kind,Border Zone)> dropZones=[];
    private Border? dropOverlay;private DispatcherTimer? dropHide;
    private Control WithDropZones(Control content)
    {
        var row=new UniformGrid{Rows=1,Columns=DropZoneKinds.Length,Margin=new(0,18,0,0)};
        foreach(var (kind,title,symbol) in DropZoneKinds)
        {
            var label=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            label.Children.Add(Ui.Text(symbol,30,"Gold",localize:false).Also(t=>t.HorizontalAlignment=HorizontalAlignment.Center));
            label.Children.Add(Ui.Text(title,17).Also(t=>{t.HorizontalAlignment=HorizontalAlignment.Center;t.Margin=new(0,8,0,0);}));
            var zone=new Border{Child=label,Height=170,Margin=new(8),CornerRadius=new(24),BorderThickness=new(2)};
            zone.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line");
            dropZones.Add((kind,zone));row.Children.Add(zone);
        }
        var panel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(36)};
        panel.Children.Add(Ui.Heading("Drop on a wheel",27).Also(t=>t.HorizontalAlignment=HorizontalAlignment.Center));
        panel.Children.Add(Ui.Text("Drop anywhere else to add the files.",14,"Muted").Also(t=>{t.HorizontalAlignment=HorizontalAlignment.Center;t.Margin=new(0,6,0,0);}));
        panel.Children.Add(row);
        dropOverlay=new Border{Child=panel,IsVisible=false,Opacity=.97};dropOverlay.Res(Border.BackgroundProperty,"Paper");
        var layers=new Grid();layers.Children.Add(content);layers.Children.Add(dropOverlay);return layers;
    }
    /// Shows the zones for file drags and highlights the one under the pointer.
    private void TrackDropZones(DragEventArgs e)
    {
        if(dropOverlay is null)return;dropHide?.Stop();
        if(busy||!e.DataTransfer.Contains(DataFormat.File)){dropOverlay.IsVisible=false;return;}
        dropOverlay.IsVisible=true;var hovered=DropZoneAt(e);
        foreach(var (kind,zone) in dropZones){var on=kind==hovered;zone.Res(Border.BackgroundProperty,on?"Sage":"Surface").Res(Border.BorderBrushProperty,on?"Gold":"Line");}
    }
    // Leave events also fire while moving between child controls (and XDND reports no position),
    // so hide only when no drag event follows shortly after.
    private void LeaveDropZones()
    {
        dropHide??=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
        dropHide.Tick-=HideDropZones;dropHide.Tick+=HideDropZones;dropHide.Stop();dropHide.Start();
    }
    private void HideDropZones(object? sender,EventArgs e){dropHide?.Stop();if(dropOverlay is not null)dropOverlay.IsVisible=false;}
    private string? DropZoneAt(DragEventArgs e)
    {
        foreach(var (kind,zone) in dropZones){var p=e.GetPosition(zone);if(p.X>=0&&p.Y>=0&&p.X<=zone.Bounds.Width&&p.Y<=zone.Bounds.Height)return kind;}
        return null;
    }
    internal bool DropZonesVisible=>dropOverlay is {IsVisible:true};
    internal Point? DropZoneCenter(string kind)=>dropZones.FirstOrDefault(z=>z.Kind==kind).Zone is {} zone?zone.TranslatePoint(new(zone.Bounds.Width/2,zone.Bounds.Height/2),this):null;
    /// A chord held while dropping wins; otherwise the zone under the pointer, if the zones were showing.
    private string? DropKind(DragEventArgs e)=>GlobalDragController.Kind(preferences.Value,e.KeyModifiers)??(dropOverlay is {IsVisible:true}?DropZoneAt(e):null);
}
