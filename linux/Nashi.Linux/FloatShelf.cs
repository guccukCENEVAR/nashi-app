using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// A compact file stack that opens into a narrow, temporary shelf. It is also a drop
/// target: files dropped with a drag chord open the matching wheel, as anywhere on macOS.
internal sealed class FloatShelf:Window
{
    private readonly ListBox files=new(){SelectionMode=SelectionMode.Multiple,Background=Brushes.Transparent,Padding=new(8,4)};
    private readonly List<string> paths=[];
    private readonly Func<IReadOnlyList<string>,string,Task> action;
    private readonly Action<IReadOnlyList<string>> reuse;private readonly Toolchain tools;private readonly OfficeRuntime office;
    private readonly Grid root=new(){RowDefinitions=new RowDefinitions("136,*,0")},header=new(){Height=136};
    private readonly Border glass=new(){CornerRadius=new(24),BorderThickness=new(1)};
    private readonly Canvas stack=new(){Width=100,Height=100,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,8,0,0),Cursor=new Cursor(StandardCursorType.Hand)};
    private readonly Button count=new(){Height=24,Width=104,FontSize=11,Padding=new(0),Margin=new(0,110,0,0),Background=Brushes.Transparent,BorderThickness=new(0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly Border countPlate=new(){Width=104,Height=24,CornerRadius=new(11),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,110,0,0),IsHitTestVisible=false};
    private readonly TextBlock status=new(){FontSize=9,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button close,options;private readonly DispatcherTimer hover=new();
    private bool icons=true,interacting,hoverInside,pressed;private Point pressOrigin;private PointerPressedEventArgs? press;private string? edge;
    internal bool IsExpanded{get;private set;}
    internal IReadOnlyList<string> Paths=>paths;
    public FloatShelf(IEnumerable<string> initial,Func<IReadOnlyList<string>,string,Task> action,Action<IReadOnlyList<string>> reuse,Toolchain tools,OfficeRuntime office)
    {
        this.action=action;this.reuse=reuse;this.tools=tools;this.office=office;Title="Float · Nashi";Icon=Ui.Icon();
        Width=112;Height=136;Topmost=true;ShowInTaskbar=false;SystemDecorations=SystemDecorations.None;CanResize=false;Background=Brushes.Transparent;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];WindowStartupLocation=WindowStartupLocation.CenterScreen;
        DragDrop.SetAllowDrop(this,true);
        Content=new Grid{Children={glass,root}};root.Children.Add(header);header.Children.Add(stack);header.Children.Add(countPlate);header.Children.Add(count);
        close=Control("×","Close Float without deleting files",Close);close.HorizontalAlignment=HorizontalAlignment.Left;close.Margin=new(6,6,0,0);header.Children.Add(close);
        options=Control("···","Float options",()=>Menu().Open(options));options.HorizontalAlignment=HorizontalAlignment.Right;options.Margin=new(0,6,6,0);header.Children.Add(options);
        Grid.SetRow(files,1);root.Children.Add(files);Grid.SetRow(status,2);root.Children.Add(status);status.Res(TextBlock.ForegroundProperty,"Muted");
        count.Click+=(_,_)=>SetExpanded(!IsExpanded);
        header.PointerPressed+=(_,e)=>{if(e.Source==header&&e.GetCurrentPoint(header).Properties.IsLeftButtonPressed){MoveShelf(e);e.Handled=true;}};
        stack.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(stack).Properties.IsLeftButtonPressed)return;pressOrigin=e.GetPosition(stack);pressed=true;press=e;interacting=true;hover.Stop();e.Handled=true;};
        stack.PointerMoved+=async(_,e)=>
        {
            if(!pressed||press is null)return;var delta=e.GetPosition(stack)-pressOrigin;if(Math.Abs(delta.X)+Math.Abs(delta.Y)<4)return;pressed=false;
            // The wire clip is the move grip. The artwork carries the actual files.
            if(pressOrigin.X>62&&pressOrigin.Y<60)MoveShelf(press);else await Carry(press,paths.ToArray());e.Handled=true;
        };
        stack.PointerReleased+=(_,e)=>{if(!pressed)return;pressed=false;interacting=false;SetExpanded(!IsExpanded);e.Handled=true;};
        files.AddHandler(PointerPressedEvent,(_,e)=>{pressOrigin=e.GetPosition(files);press=e;},RoutingStrategies.Tunnel);
        files.PointerMoved+=async(_,e)=>{if(press is null||!e.GetCurrentPoint(files).Properties.IsLeftButtonPressed||files.SelectedItems?.Count is not >0)return;var delta=e.GetPosition(files)-pressOrigin;if(Math.Abs(delta.X)+Math.Abs(delta.Y)>8){var origin=press;press=null;await Carry(origin,Selection());}};
        files.SelectionChanged+=(_,_)=>UpdateLabels();
        files.DoubleTapped+=(_,_)=>{foreach(var p in Selection())FileActions.Open(p);};
        AddHandler(KeyDownEvent,OnKey,RoutingStrategies.Tunnel);
        PointerEntered+=(_,_)=>ScheduleHover(true);PointerExited+=(_,_)=>ScheduleHover(false);
        hover.Tick+=(_,_)=>{hover.Stop();if(interacting)return;if(hoverInside&&IsPointerOver)SetExpanded(true);else if(!hoverInside&&!IsPointerOver)SetExpanded(false);};
        AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,(_,e)=>{var content=TransferContent.From(e.DataTransfer);e.Handled=true;Drop(content,e.KeyModifiers);});
        files.ContextMenu=Menu();stack.ContextMenu=Menu();
        LanguageState.Shared.Changed+=LanguageChanged;Closed+=(_,_)=>{hover.Stop();LanguageState.Shared.Changed-=LanguageChanged;};
        Add(initial);Present();
    }
    /// A chord on drop opens its wheel for the dropped files; a plain drop just adds them.
    private void Drop(TransferContent content,KeyModifiers modifiers)
    {
        if(content.Files.Length==0)return;
        var kind=GlobalDragController.Kind(WheelPresentation.Preferences,modifiers);
        if(kind is null){Add(content.Files);return;}
        var choices=GlobalDragController.ChoicesFor(kind,content.Files);if(choices.Count==0){Add(content.Files);return;}
        WheelWindow.Present(this,choices,async choice=>await action(content.Files,MainWindow.WheelActionPrefix+choice.Id));
    }
    private void LanguageChanged(){Reload();files.ContextMenu=Menu();stack.ContextMenu=Menu();}
    private Button Control(string text,string help,Action click)
    {
        var b=new Button{Content=text,Width=24,Height=24,Padding=new(0),Margin=new(0),FontSize=16,VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,BorderThickness=new(0)};
        L10n.Tip(b,help);Avalonia.Automation.AutomationProperties.SetName(b,L10n.Text(help));b.Click+=(_,_)=>click();return b;
    }
    private void ScheduleHover(bool inside){hover.Stop();hoverInside=inside;hover.Interval=TimeSpan.FromMilliseconds(inside?200:650);hover.Start();}
    private void MoveShelf(PointerPressedEventArgs e){interacting=true;hover.Stop();BeginMoveDrag(e);DispatcherTimer.RunOnce(()=>{interacting=false;edge=SnapEdge();ScheduleHover(false);},TimeSpan.FromMilliseconds(400));}
    private async Task Carry(PointerEventArgs origin,string[] selected)
    {
        if(selected.Length==0){interacting=false;return;}interacting=true;hover.Stop();
        try{var transfer=await FileActions.Transfer(this,selected);if(transfer is not null)await DragDrop.DoDragDropAsync(origin,transfer,DragDropEffects.Copy|DragDropEffects.Move);}
        finally{interacting=false;Reload();ScheduleHover(false);}
    }
    internal void Add(IEnumerable<string> incoming)
    {
        foreach(var p in incoming.Where(p=>File.Exists(p)||Directory.Exists(p)).Take(1000)){var full=Path.GetFullPath(p);if(!paths.Contains(full,StringComparer.Ordinal))paths.Add(full);}Reload();
    }
    private string[] Selection()=>files.SelectedItems?.Count>0?files.SelectedItems.OfType<ListBoxItem>().Select(i=>(string)i.Tag!).ToArray():paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    private void Reload()
    {
        var selected=(files.SelectedItems?.OfType<ListBoxItem>().Select(i=>(string)i.Tag!)??[]).ToHashSet(StringComparer.Ordinal);var items=new List<ListBoxItem>();
        foreach(var path in paths)
        {
            var column=new StackPanel{Orientation=icons?Orientation.Vertical:Orientation.Horizontal};var art=FileArtwork.Create(path,icons?60:32);art.HorizontalAlignment=icons?HorizontalAlignment.Center:HorizontalAlignment.Left;column.Children.Add(art);
            column.Children.Add(new TextBlock{Text=Path.GetFileName(path),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=icons?TextAlignment.Center:TextAlignment.Left,FontSize=11,MaxHeight=32,Width=icons?112:204,Margin=icons?new(0,6,0,0):new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});
            var item=new ListBoxItem{Content=column,Tag=path,Padding=new(4),Margin=new(0,2),HorizontalContentAlignment=HorizontalAlignment.Center};ToolTip.SetTip(item,path);items.Add(item);
        }
        files.ItemsSource=items;foreach(var item in items.Where(i=>selected.Contains((string)i.Tag!)))files.SelectedItems?.Add(item);
        DrawStack();UpdateLabels();if(IsExpanded)SetExpanded(true,true);
    }
    private void DrawStack()
    {
        stack.Children.Clear();var top=paths.TakeLast(3).ToArray();
        for(var index=0;index<top.Length;index++)
        {
            var offset=top.Length-index-1;var image=FileArtwork.Create(top[index],72);image.RenderTransformOrigin=RelativePoint.Center;image.RenderTransform=new RotateTransform(offset*-7);image.IsHitTestVisible=false;Canvas.SetLeft(image,14-offset*2);Canvas.SetTop(image,17+offset*2);stack.Children.Add(image);
        }
        if(top.Length==0)stack.Children.Add(new TextBlock{Text="＋",FontSize=44,Width=100,TextAlignment=TextAlignment.Center,Margin=new(0,20,0,0),IsHitTestVisible=false});
        const string wire="M76,71 L57,28 C47,6 70,-1 78,19 L94,57 C102,74 87,78 81,63 L66,28";
        foreach(var (color,width) in new[]{("#55000000",4.5),("#C2CAC5",2.6),("#CCFFFFFF",.7)})stack.Children.Add(new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(wire),Stroke=new SolidColorBrush(Color.Parse(color)),StrokeThickness=width,StrokeLineCap=PenLineCap.Round,IsHitTestVisible=false});
    }
    private void UpdateLabels()
    {
        count.Content=(IsExpanded?"⌃  ":"⌄  ")+L10n.Text(paths.Count==1?"1 item":$"{paths.Count} items");L10n.Tip(count,IsExpanded?"Collapse Float":"Expand Float");
        var selected=files.SelectedItems?.Count??0;status.Text=L10n.Text(selected>0?$"{selected} selected":"Space · Preview");Title=$"Float · {paths.Count} · Nashi";
    }
    private void Present()
    {
        glass.IsVisible=IsExpanded;
        glass.Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(0,1,RelativeUnit.Relative),GradientStops={new GradientStop(NashiTheme.Dark?Color.FromArgb(248,42,49,34):Color.FromArgb(248,250,231,185),0),new GradientStop(NashiTheme.Dark?Color.FromArgb(248,26,39,30):Color.FromArgb(248,227,235,211),1)}};glass.Res(Border.BorderBrushProperty,"Line");
        if(IsExpanded)countPlate.Background=Brushes.Transparent;else countPlate.Res(Border.BackgroundProperty,"Surface");
        root.RowDefinitions[0].Height=new GridLength(IsExpanded?140:136);header.Height=IsExpanded?140:136;root.RowDefinitions[2].Height=new GridLength(IsExpanded?22:0);
        files.IsVisible=status.IsVisible=close.IsVisible=options.IsVisible=IsExpanded;UpdateLabels();
    }
    private PixelRect WorkArea()=>(Screens.ScreenFromWindow(this)??Screens.Primary)?.WorkingArea??new PixelRect(0,0,1920,1080);
    private double Scale=>(Screens.ScreenFromWindow(this)??Screens.Primary)?.Scaling??1;
    internal void SetExpanded(bool value,bool forceLayout=false)
    {
        if(IsExpanded==value&&!forceLayout)return;hover.Stop();var oldWidth=Bounds.Width>0?Bounds.Width:Width;
        IsExpanded=value;Present();var work=WorkArea();var scale=Scale;var width=value?(icons?156:284):112;var height=value?Math.Min(work.Height/scale-16,162+Math.Max(1,Math.Min(paths.Count,5))*(icons?112:52)):136;
        var x=Math.Clamp(Position.X+(int)((oldWidth-width)/2*scale),work.X,work.Right-(int)(width*scale));var y=Math.Clamp(Position.Y,work.Y,Math.Max(work.Y,work.Bottom-(int)(height*scale)));
        if(edge=="Right")x=work.Right-(int)(width*scale);else if(edge=="Left")x=work.X;
        if(edge=="Bottom")y=work.Bottom-(int)(height*scale);else if(edge=="Top")y=work.Y;
        Width=width;Height=height;Position=new PixelPoint(x,y);if(value)files.Focus();
    }
    /// Attaches to the nearest work-area edge when released close to it, like the other platforms.
    private string? SnapEdge(string? requested=null,bool automatic=true)
    {
        var work=WorkArea();var scale=Scale;var w=(int)(Bounds.Width*scale);var h=(int)(Bounds.Height*scale);var x=Position.X;var y=Position.Y;string? side=requested;
        if(automatic&&requested is null){const int threshold=24;side=x-work.X<threshold?"Left":work.Right-(x+w)<threshold?"Right":y-work.Y<threshold?"Top":work.Bottom-(y+h)<threshold?"Bottom":null;}
        if(side=="Left")x=work.X;else if(side=="Right")x=work.Right-w;else if(side=="Top")y=work.Y;else if(side=="Bottom")y=work.Bottom-h;
        Position=new PixelPoint(Math.Clamp(x,work.X,Math.Max(work.X,work.Right-w)),Math.Clamp(y,work.Y,Math.Max(work.Y,work.Bottom-h)));return side;
    }
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();menu.Opened+=(_,_)=>{interacting=true;hover.Stop();};menu.Closed+=(_,_)=>{interacting=false;ScheduleHover(false);};
        foreach(var (label,id) in new[]{("Open","open"),("Quick Preview","preview"),("Show in Files","reveal"),("Copy","copy"),("Copy Path","copyPath"),("Share…","share"),("Remove from Float","remove"),("Use in Nashi","reuse")})
        {
            var item=new MenuItem{Header=L10n.Text(label)};
            item.Click+=async(_,_)=>
            {
                try
                {
                    if(id=="open")foreach(var path in Selection())FileActions.Open(path);else if(id=="preview")Preview();else if(id=="reveal")foreach(var path in Selection())FileActions.Reveal(path);
                    else if(id=="remove")Remove();else if(id=="reuse")reuse(Selection());else await action(Selection(),id);
                }
                catch(Exception error)when(FileActions.IsActionError(error)){await Dialogs.Info(this,error.Message,"Float action unavailable");}
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach(var iconMode in new[]{true,false}){var item=new MenuItem{Header=L10n.Text(iconMode?"Icons":"List"),ToggleType=MenuItemToggleType.Radio,IsChecked=icons==iconMode};item.Click+=(_,_)=>{icons=iconMode;Reload();SetExpanded(false);SetExpanded(true);};menu.Items.Add(item);}
        var add=new MenuItem{Header=L10n.Text("Add Files…")};add.Click+=async(_,_)=>Add(await Dialogs.PickFiles(this,"Add Files…"));menu.Items.Add(add);
        var clear=new MenuItem{Header=L10n.Text("Clear Float")};clear.Click+=(_,_)=>{paths.Clear();Reload();};menu.Items.Add(clear);
        var pin=new MenuItem{Header=L10n.Text("Attach to Edge")};
        foreach(var side in new[]{"Top","Right","Bottom","Left","Detach"}){var item=new MenuItem{Header=L10n.Text(side),ToggleType=MenuItemToggleType.Radio,IsChecked=edge==(side=="Detach"?null:side)};item.Click+=(_,_)=>{edge=side=="Detach"?null:SnapEdge(side,false);};pin.Items.Add(item);}
        menu.Items.Add(pin);return menu;
    }
    private void Remove(){foreach(var p in (files.SelectedItems?.OfType<ListBoxItem>().Select(i=>(string)i.Tag!)??[]).ToArray())paths.Remove(p);Reload();}
    private async void OnKey(object? sender,KeyEventArgs e)
    {
        try
        {
            if(e.KeyModifiers==(KeyModifiers.Control|KeyModifiers.Shift)&&e.Key==Key.C){e.Handled=true;await action(Selection(),"copyPath");return;}
            if(e.Key==Key.Escape){if(files.SelectedItems?.Count>0)files.UnselectAll();else SetExpanded(false);e.Handled=true;}
            else if(e.Key==Key.Delete){Remove();e.Handled=true;}
            else if(e.Key==Key.Space){Preview();e.Handled=true;}
            else if(e.Key==Key.Enter){foreach(var p in Selection())FileActions.Open(p);e.Handled=true;}
            else if(e.KeyModifiers==KeyModifiers.Control)
            {
                if(e.Key==Key.A){SetExpanded(true);files.SelectAll();e.Handled=true;}
                else if(e.Key==Key.C){e.Handled=true;await action(Selection(),"copy");}
                else if(e.Key==Key.V){e.Handled=true;if(Clipboard is not null)Add((await TransferContent.FromAsync(Clipboard)).Files);}
                else if(e.Key==Key.R){foreach(var p in Selection())FileActions.Reveal(p);e.Handled=true;}
                else if(e.Key==Key.O){foreach(var p in Selection())FileActions.Open(p);e.Handled=true;}
            }
            UpdateLabels();
        }
        catch(Exception error)when(FileActions.IsActionError(error)){await Dialogs.Info(this,error.Message,"Float action unavailable");}
    }
    private void Preview(){var selected=Selection();if(selected.Length>0)new FilePreviewWindow(selected,tools,office).Show();}
}
