using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed record ToolChoice(string Id,string Title,string Symbol);

internal static class WheelPresentation
{
    internal const double Diameter=320,Canvas=540,Center=270,Inner=108,Outer=250;
    internal static double StartAngle(int count)=>count==3?-180:-90;
    internal static Preferences Preferences{get;set;}=new();
    internal static IBrush Backdrop{get{var color=NashiTheme.Color("Surface");return new SolidColorBrush(color,Preferences.WheelFinish=="glass"?.95:1);}}
    internal static double LabelRadius(int count,double half)=>count<=2?180:4*Math.Sin(half)*(Math.Pow(Outer,3)-Math.Pow(Inner,3))/(6*half*(Outer*Outer-Inner*Inner));
    internal static Geometry Petal(double angle,double half)
    {
        Point P(double r,double v)=>new(Center+r*Math.Cos(v),Center+r*Math.Sin(v));var a=angle-half;var b=angle+half;
        var geometry=new StreamGeometry();
        using(var g=geometry.Open())
        {
            g.BeginFigure(P(Inner,a),true);g.LineTo(P(Outer,a));g.ArcTo(P(Outer,b),new Size(Outer,Outer),0,b-a>Math.PI,SweepDirection.Clockwise);
            g.LineTo(P(Inner,b));g.ArcTo(P(Inner,a),new Size(Inner,Inner),0,b-a>Math.PI,SweepDirection.CounterClockwise);g.EndFigure(true);
        }
        return geometry;
    }
    /// The largest caption (symbol + uppercase title) that fits fully inside its petal, as on macOS.
    internal static (StackPanel Label,Point Center) Caption(ToolChoice choice,Geometry petal,double angle,double half,int count)
    {
        var preferred=LabelRadius(count,half);
        for(var font=16d;font>=10;font-=.5)for(var width=142d;width>=72;width-=7)
        {
            var label=Label(choice,font,width);label.Measure(new Size(width,double.PositiveInfinity));var h=label.DesiredSize.Height;
            for(var step=0;step<=20;step++)
            {
                var radial=preferred+(step==0?0:(step%2==1?1:-1)*((step+1)/2)*5);var center=new Point(Center+radial*Math.Cos(angle),Center+radial*Math.Sin(angle));var r=new Rect(center.X-width/2-3,center.Y-h/2-3,width+6,h+6);
                if(new[]{r.TopLeft,r.TopRight,r.BottomLeft,r.BottomRight,new Point(r.Left,r.Center.Y),new Point(r.Right,r.Center.Y)}.All(petal.FillContains))return(label,center);
            }
        }
        var fallback=Label(choice,10,72,symbol:false);fallback.Measure(new Size(72,double.PositiveInfinity));return(fallback,new Point(Center+preferred*Math.Cos(angle),Center+preferred*Math.Sin(angle)));
    }
    private static StackPanel Label(ToolChoice choice,double font,double width,bool symbol=true)
    {
        var label=new StackPanel{Width=width,IsHitTestVisible=false};
        if(symbol)label.Children.Add(new TextBlock{Text=choice.Symbol,FontSize=32,TextAlignment=TextAlignment.Center,Margin=new(0,0,0,6)}.Res(TextBlock.ForegroundProperty,"Ink"));
        label.Children.Add(new TextBlock{Text=L10n.Uppercase(choice.Title),FontSize=font,FontWeight=FontWeight.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap}.Res(TextBlock.ForegroundProperty,"Ink"));
        return label;
    }
    internal static string CenterTitle(IReadOnlyList<ToolChoice> choices)=>choices.Count>0&&choices.All(t=>t.Id.StartsWith("download:",StringComparison.Ordinal))?"Web media":choices.All(t=>t.Id.StartsWith("convert:",StringComparison.Ordinal))?"Convert":choices.Any(t=>t.Id=="float")?"File actions":"Tools";
}

/// Plays the wheel selection tick through the sound server, rate-limited like the other platforms.
internal static class Feedback
{
    internal static bool Enabled{get;set;}=true;
    private static DateTime last;private static byte[]? sound;
    internal static void Stop(){}
    public static void Play()
    {
        if(!Enabled||(DateTime.UtcNow-last).TotalMilliseconds<70)return;last=DateTime.UtcNow;
        try{sound??=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"WheelSelection.wav"));PulseAudio.PlayWave(sound,.65);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
    }
}

/// The radial picker. In drop mode its petals are drag targets, so files dragged
/// from any file manager can be released straight onto a conversion or tool.
internal sealed class WheelWindow:Window
{
    private readonly List<Avalonia.Controls.Shapes.Path> sectors=[];private readonly List<Button> buttons=[];
    private readonly Canvas surface=new(){Width=WheelPresentation.Canvas,Height=WheelPresentation.Canvas,Background=Brushes.Transparent};
    private IReadOnlyList<ToolChoice> choices;private readonly Action<ToolChoice,TransferContent?> select;private readonly Func<TransferContent,IReadOnlyList<ToolChoice>>? dropChoices;
    private int selected=-1;private bool closing,chosen;
    internal int SelectedIndex=>selected;
    internal IReadOnlyList<ToolChoice> Items=>choices;
    private WheelWindow(IReadOnlyList<ToolChoice> choices,Action<ToolChoice,TransferContent?> select,Func<TransferContent,IReadOnlyList<ToolChoice>>? dropChoices,string? centerTitle)
    {
        this.choices=choices;this.select=select;this.dropChoices=dropChoices;
        Title="Nashi tools";Width=Height=WheelPresentation.Diameter*WheelPresentation.Preferences.WheelScale;SystemDecorations=SystemDecorations.None;CanResize=false;ShowInTaskbar=false;Topmost=true;
        Background=Brushes.Transparent;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Icon=Ui.Icon();
        Content=new Viewbox{Child=surface,Stretch=Stretch.Uniform};
        Build(centerTitle);
        // The key that opened the wheel (Ctrl+Space, Enter) is released inside it. A release
        // without a press here must not reach the focused petal, or it would choose it at once.
        var pressed=new HashSet<Key>();
        AddHandler(KeyDownEvent,(_,e)=>{pressed.Add(e.Key);e.Handled=Navigate(e.Key);},RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,(_,e)=>{if(!pressed.Remove(e.Key))e.Handled=true;},RoutingStrategies.Tunnel);
        Opened+=(_,_)=>{_ = NashiTheme.FadeIn(this);if(dropChoices is null&&buttons.Count>0){Activate();buttons[0].Focus();}};
        Closing+=(_,_)=>closing=true;
        Deactivated+=(_,_)=>{if(dropChoices is null&&!closing&&IsVisible)Close();};
        if(dropChoices is not null)
        {
            DragDrop.SetAllowDrop(this,true);
            AddHandler(DragDrop.DragEnterEvent,(_,e)=>Track(e));AddHandler(DragDrop.DragOverEvent,(_,e)=>Track(e));
            AddHandler(DragDrop.DragLeaveEvent,(_,e)=>{Highlight(-1);});
            AddHandler(DragDrop.DropEvent,(_,e)=>{Track(e);var index=selected;var content=TransferContent.From(e.DataTransfer);e.Handled=true;if(index>=0&&index<choices.Count)Choose(choices[index],content);else Close();});
        }
    }
    /// Opens the click wheel centred on the owner (or on screen when Nashi is in the tray).
    internal static WheelWindow Present(Window owner,IReadOnlyList<ToolChoice> choices,Action<ToolChoice> select)
    {
        var wheel=new WheelWindow(choices.Take(12).ToArray(),(choice,_)=>select(choice),null,null);
        if(owner.IsVisible){wheel.WindowStartupLocation=WindowStartupLocation.CenterOwner;wheel.Show(owner);}else{wheel.WindowStartupLocation=WindowStartupLocation.CenterScreen;wheel.Show();}
        return wheel;
    }
    /// Opens a drop wheel at a screen point; the petals follow whatever is being dragged.
    internal static WheelWindow PresentForDrop(PixelPoint center,IReadOnlyList<ToolChoice> initial,Func<TransferContent,IReadOnlyList<ToolChoice>> choicesFor,Action<ToolChoice,TransferContent?> select)
    {
        var wheel=new WheelWindow(initial,select,choicesFor,initial.Count==0?"Keep dragging\nDrop a file or link":null){WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false};
        var size=(int)Math.Round(WheelPresentation.Diameter*WheelPresentation.Preferences.WheelScale);wheel.Position=new PixelPoint(center.X-size/2,center.Y-size/2);
        wheel.Show();return wheel;
    }
    private void Track(DragEventArgs e)
    {
        if(dropChoices is not null)
        {
            var updated=dropChoices(TransferContent.From(e.DataTransfer)).Take(12).ToArray();
            if(!updated.Select(c=>c.Id).SequenceEqual(choices.Select(c=>c.Id))){choices=updated;Build(null);}
        }
        var p=e.GetPosition(surface);var index=IndexAt(p);
        if(index!=selected&&index>=0)Feedback.Play();Highlight(index);
        e.DragEffects=index>=0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
    }
    private int IndexAt(Point p)
    {
        var dx=p.X-WheelPresentation.Center;var dy=p.Y-WheelPresentation.Center;var distance=Math.Sqrt(dx*dx+dy*dy);
        if(distance<WheelPresentation.Inner||distance>WheelPresentation.Outer||choices.Count==0)return -1;
        var angle=(Math.Atan2(dy,dx)*180/Math.PI-WheelPresentation.StartAngle(choices.Count)+360+180d/choices.Count)%360;return Math.Min(choices.Count-1,(int)(angle/(360d/choices.Count)));
    }
    internal bool Navigate(Key key)
    {
        if(key==Key.Escape){Close();return true;}
        if(buttons.Count==0)return false;
        if(key is Key.Right or Key.Down||key==Key.Tab){Highlight((selected+1)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Left or Key.Up){Highlight((selected-1+buttons.Count)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Enter or Key.Space){Choose(choices[Math.Max(0,selected)],null);return true;}
        return false;
    }
    private void Choose(ToolChoice choice,TransferContent? content){if(chosen)return;chosen=true;Close();select(choice,content);}
    private void Highlight(int index)
    {
        if(selected==index)return;selected=index;
        for(var i=0;i<sectors.Count;i++){sectors[i].Res(Shape.FillProperty,i==index?"Sage":"Inset").Res(Shape.StrokeProperty,i==index?"Gold":"Line");sectors[i].StrokeThickness=i==index?2:1;}
        if(index>=0&&dropChoices is null)Feedback.Play();
    }
    private void Build(string? centerTitle)
    {
        surface.Children.Clear();sectors.Clear();buttons.Clear();selected=-1;
        var disc=new Ellipse{Width=530,Height=530,Fill=WheelPresentation.Backdrop};Canvas.SetLeft(disc,5);Canvas.SetTop(disc,5);surface.Children.Add(disc);
        var count=Math.Min(12,choices.Count);
        for(var i=0;i<count;i++)
        {
            var choice=choices[i];var angle=(WheelPresentation.StartAngle(count)+360d*i/count)*Math.PI/180;var half=(180d/count-1)*Math.PI/180;var geometry=WheelPresentation.Petal(angle,half);
            var path=new Avalonia.Controls.Shapes.Path{Data=geometry,StrokeThickness=1}.Res(Shape.FillProperty,"Inset").Res(Shape.StrokeProperty,"Line");surface.Children.Add(path);sectors.Add(path);
            var (label,location)=WheelPresentation.Caption(choice,geometry,angle,half,count);var index=i;
            var button=new Button{Content=label,Width=label.DesiredSize.Width,Height=label.DesiredSize.Height,IsHitTestVisible=dropChoices is null};button.Classes.Add("wheel");AutomationProperties(button,choice.Title);
            Canvas.SetLeft(button,location.X-button.Width/2);Canvas.SetTop(button,location.Y-button.Height/2);surface.Children.Add(button);buttons.Add(button);
            if(dropChoices is null)
            {
                button.Click+=(_,_)=>Choose(choice,null);button.PointerEntered+=(_,_)=>Highlight(index);button.GotFocus+=(_,_)=>Highlight(index);
                path.PointerReleased+=(_,_)=>Choose(choice,null);path.PointerEntered+=(_,_)=>Highlight(index);
            }
        }
        var center=new StackPanel{Width=180,VerticalAlignment=VerticalAlignment.Center};
        var title=Ui.Text(centerTitle??WheelPresentation.CenterTitle(choices),count==0?15:20);title.TextAlignment=TextAlignment.Center;title.FontWeight=FontWeight.SemiBold;center.Children.Add(title);
        var hint=Ui.Text(dropChoices is null?"Esc to close":"Release on a petal · Esc cancels",11,"Muted");hint.TextAlignment=TextAlignment.Center;hint.Margin=new(0,6,0,0);center.Children.Add(hint);
        var middle=new Border{Width=180,Height=180,Child=center,Background=Brushes.Transparent};Canvas.SetLeft(middle,180);Canvas.SetTop(middle,180);surface.Children.Add(middle);
        if(dropChoices is null)middle.PointerReleased+=(_,_)=>Close();
    }
    private static void AutomationProperties(Control control,string title)=>Avalonia.Automation.AutomationProperties.SetName(control,L10n.Text(title));
}
