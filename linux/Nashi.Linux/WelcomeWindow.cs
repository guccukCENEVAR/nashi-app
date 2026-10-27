using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux;

/// Remembers that the tour was shown, beside the user's Library.
internal static class WelcomeState
{
    private static string Marker=>Path.Combine(MainWindow.DataDirectory,"welcome-seen");
    internal static bool Seen=>File.Exists(Marker);
    internal static void MarkSeen(){try{Directory.CreateDirectory(MainWindow.DataDirectory);File.WriteAllText(Marker,"1");}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}}
}

/// The macOS six-step tour: hello, the three drag chords, folders, and readiness.
internal sealed class WelcomeWindow:Window
{
    private static readonly string[] Names=["Hello","Convert","Fine-tune","Move & share","Folders","Ready"];
    private readonly bool toolsReady;private readonly Action done;private int page;private bool finished;
    private readonly Grid art=new(){Width=310,Height=330};private readonly StackPanel copy=new(){Width=310,Spacing=17,VerticalAlignment=VerticalAlignment.Center};
    private readonly StackPanel dots=new(){Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center};private readonly TextBlock counter=new(){FontSize=10,Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Button back,next;
    private string[] Titles=>["Nice files.\nNo fuss.","A new form.\nIn a tap.","Little tweaks.\nBig difference.","Keep things\nmoving.","Right where\nyou want it.",toolsReady?"You’re all set.\nMake something good.":"One last step.\nSet up your tools."];
    internal WelcomeWindow(bool toolsReady,Action done)
    {
        this.toolsReady=toolsReady;this.done=done;Width=760;Height=520;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Welcome to Nashi");
        var root=new DockPanel();Content=root;
        var top=new DockPanel{Margin=new(22,15,22,0)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var skip=Ui.Quiet("Skip tour",Finish);DockPanel.SetDock(skip,Dock.Right);top.Children.Add(skip);var eyebrow=Ui.Eyebrow("A FRESH START");eyebrow.HorizontalAlignment=HorizontalAlignment.Center;eyebrow.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(eyebrow);
        var bottom=new DockPanel{Margin=new(32,0,32,26)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        next=Ui.Primary("Continue",Next);back=Ui.Quiet("Back",()=>Show(page-1));var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(back);buttons.Children.Add(next);DockPanel.SetDock(buttons,Dock.Right);bottom.Children.Add(buttons);
        var progress=new StackPanel{Orientation=Orientation.Horizontal};progress.Children.Add(dots);progress.Children.Add(counter.Res(TextBlock.ForegroundProperty,"Muted"));bottom.Children.Add(progress);
        var body=new StackPanel{Orientation=Orientation.Horizontal,Spacing=28,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};body.Children.Add(art);body.Children.Add(copy);root.Children.Add(body);
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Finish();e.Handled=true;}else if(e.Key==Key.Enter){Next();e.Handled=true;}else if(e.Key==Key.Right){Next();e.Handled=true;}else if(e.Key==Key.Left&&page>0){Show(page-1);e.Handled=true;}},RoutingStrategies.Tunnel);
        Closed+=(_,_)=>{if(!finished){finished=true;done();}};
        Show(0);
    }
    private void Next(){if(page==5)Finish();else Show(page+1);}
    private void Finish(){if(finished)return;finished=true;done();Close();}
    private void Show(int value)
    {
        page=Math.Clamp(value,0,5);art.Children.Clear();copy.Children.Clear();dots.Children.Clear();
        art.Children.Add(new Ellipse{Width=280,Height=280}.Res(Shape.FillProperty,page==4?"Sage":"Sand"));
        if(page is 0 or 5)art.Children.Add(new Image{Source=Ui.Asset(page==5?"NashiMarkHappy":"NashiMark"),Width=170,Height=170,RenderTransform=new RotateTransform(page==5?-6:0)});
        else if(page==4)art.Children.Add(Ui.Text("▰",112,"Forest",localize:false).Also(t=>{t.HorizontalAlignment=HorizontalAlignment.Center;t.TextAlignment=TextAlignment.Center;}));
        else art.Children.Add(Preview(page));
        var pill=Ui.Pill(Names[page].ToUpperInvariant(),page==5?"Forest":"GoldInk");copy.Children.Add(pill);
        var title=Ui.Text(Titles[page],33);title.FontWeight=FontWeight.Bold;title.LetterSpacing=-1;copy.Children.Add(title);
        foreach(var control in Detail())copy.Children.Add(control);
        for(var i=0;i<6;i++){var index=i;var dot=new Border{Width=page==i?23:6,Height=6,CornerRadius=new(3),Cursor=new Cursor(StandardCursorType.Hand)}.Res(Border.BackgroundProperty,page==i?"Gold":"Line");dot.PointerPressed+=(_,_)=>Show(index);dots.Children.Add(dot);}
        counter.Text=$"{page+1} / 6";back.IsVisible=page>0;L10n.Bind(next,ContentControl.ContentProperty,page==5?(toolsReady?"Let’s go":"Set up tools"):"Continue");
        NashiTheme.Entrance(copy);
    }
    private IEnumerable<Control> Detail()
    {
        TextBlock Body(string text){var t=Ui.Text(text,13,"Muted");return t;}
        switch(page)
        {
            case 0:
                yield return Body("A happier home for all the little file jobs. Convert, edit, and share — right on your computer.");
                yield return Ui.Text("Your files stay local",11,"Forest");break;
            case 1:case 2:case 3:
                yield return Body(page==1?"Drag a file in your file manager, hold Shift, and drop onto its next format.":page==2?"Hold Ctrl while dragging to crop, compress, annotate, and give your file a little love.":"Hold Alt while dragging to copy, move, share, or carry files in a floating shelf.");
                var chord=new StackPanel{Orientation=Orientation.Horizontal,Spacing=9};chord.Children.Add(Ui.Text(page==1?"⇧":page==2?"Ctrl":"Alt",page==1?22:16,localize:false));chord.Children.Add(Ui.Text(page==1?"Shift to convert":page==2?"Ctrl for tools":"Alt for actions",12));
                var key=Ui.Card(chord);key.Padding=new(13);key.CornerRadius=new(14);key.HorizontalAlignment=HorizontalAlignment.Left;yield return key;
                yield return Ui.Text(GlobalDragController.GlobalGesturesAvailable?"You can also choose files from the main window.":"On Wayland, drop onto the Nashi window or a Float shelf while holding the key.",11,"Muted");break;
            case 4:
                yield return Body("Fresh copies are saved beside the originals by default. You can choose one folder for every new copy instead.");
                yield return Ui.Text("Optional. You can change this in Settings.",11,"Muted");break;
            default:
                yield return Body(toolsReady?"Your tools are ready. Let’s put that next file to good use.":"Next, install the tools for media and Office files. Tools already on your computer are kept.");
                var login=new CheckBox{IsChecked=Autostart.Enabled};L10n.Bind(login,ContentControl.ContentProperty,"Ready when you log in");
                login.IsCheckedChanged+=(_,_)=>{try{Autostart.Set(login.IsChecked==true);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){login.IsChecked=Autostart.Enabled;}};
                yield return login;break;
        }
    }
    private static Control Preview(int page)
    {
        var items=page==1?new[]{"PNG","WEBP","HEIC","TIFF","AVIF","BMP","PDF"}.Select(t=>new ToolChoice("convert:"+t,t,"↗")).ToArray():page==2?new[]{new ToolChoice("compress","Compress","↘"),new("removeMetadata","Metadata","◇"),new("crop","Crop","⌗"),new("annotate","Annotate","✎"),new("editImage","Edit","☷")}:MainWindow.FileActionChoices;
        var canvas=new Canvas{Width=WheelPresentation.Canvas,Height=WheelPresentation.Canvas};
        canvas.Children.Add(new Ellipse{Width=530,Height=530}.Res(Shape.FillProperty,"Surface").Also(e=>{Canvas.SetLeft(e,5);Canvas.SetTop(e,5);}));
        for(var i=0;i<items.Length;i++)
        {
            var angle=(WheelPresentation.StartAngle(items.Length)+360d*i/items.Length)*Math.PI/180;var half=(180d/items.Length-1)*Math.PI/180;var petal=WheelPresentation.Petal(angle,half);
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path{Data=petal,StrokeThickness=i==0?2:1}.Res(Shape.FillProperty,i==0?"Sage":"Inset").Res(Shape.StrokeProperty,i==0?"Gold":"Line"));
            var (label,center)=WheelPresentation.Caption(items[i],petal,angle,half,items.Length);Canvas.SetLeft(label,center.X-label.DesiredSize.Width/2);Canvas.SetTop(label,center.Y-label.DesiredSize.Height/2);canvas.Children.Add(label);
        }
        return new Viewbox{Width=300,Height=300,Child=canvas,IsHitTestVisible=false};
    }
}

internal static class ControlExtensions
{
    internal static T Also<T>(this T value,Action<T> configure){configure(value);return value;}
}
