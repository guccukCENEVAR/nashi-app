using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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

/// One editor for every tool: preview on the left, settings on the right, Save new copy.
internal sealed partial class ToolEditor:Window
{
    public MediaOptions MediaOptions{get;private set;}=new();public ImageOptions ImageOptions{get;private set;}=new();public string Format{get;private set;}="png";public int[] Pages{get;private set;}=[];
    private readonly string tool;private readonly ImageFrame? bitmap;private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> defaults=[];
    private readonly ComboBox format=new();private readonly ComboBox drawingTool=new();
    private readonly Canvas drawing=new(){Background=Brushes.Transparent,Focusable=true,ClipToBounds=true};private readonly Image picture=new(){Stretch=Stretch.Fill};
    private Rectangle? selection,activeRectangle;private Point origin;private bool dragging;
    private MediaPlayer? player;
    private readonly Slider scrubber=new(){Minimum=0,Margin=new(4,12,4,8)};private bool scrubbing,syncingScrubber;
    private readonly StackPanel previewStack=new();
    private readonly RangeTimeline timeline=new();private readonly TextBlock time=new(){Margin=new(4,6,4,10)};private double duration=1;
    internal double PreviewSeconds=>player?.Position??0;
    internal bool Playing=>player?.Playing==true;
    internal RangeTimeline Timeline=>timeline;
    internal TextBox FieldForTest(string name)=>fields[name];
    private readonly string input;private readonly MediaInfo? media;private readonly StackPanel options=new(){Margin=new(18,0,0,0)};
    private KeyModifiers modifiers;
    public ToolEditor(string tool,string title,string input,MediaInfo? media=null,ImageFrame? preview=null,IReadOnlyList<string>? inputs=null,string[]? outputFormats=null)
    {
        this.tool=tool;this.input=input;this.media=media;bitmap=preview;Width=1060;Height=780;MinWidth=800;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();
        Bind(TitleProperty,LanguageState.Shared.Translate(title,format:t=>t+" · Nashi"));
        var grid=new Grid{Margin=new(28),RowDefinitions=new RowDefinitions("Auto,*,Auto")};Content=grid;
        var heading=new StackPanel();heading.Children.Add(Ui.Heading(title,30));
        var subtitle=new TextBlock{Margin=new(0,5,0,22)};subtitle.Bind(TextBlock.TextProperty,LanguageState.Shared.Translate("Original stays safe",format:t=>Path.GetFileName(input)+" · "+t));heading.Children.Add(subtitle.Res(TextBlock.ForegroundProperty,"Muted"));grid.Children.Add(heading);
        var body=new Grid{ColumnDefinitions=new ColumnDefinitions("1.6*,*")};Grid.SetRow(body,1);grid.Children.Add(body);
        previewStack.Children.Add(Ui.Text("PREVIEW",11,"Muted").Also(t=>{t.FontWeight=FontWeight.SemiBold;t.Margin=new(4,0,0,16);}));
        options.Children.Add(Ui.Text("MAKE IT YOURS",11,"Muted").Also(t=>{t.FontWeight=FontWeight.SemiBold;t.Margin=new(4,0,0,16);}));
        body.Children.Add(new ScrollViewer{Content=previewStack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var optionScroll=new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(optionScroll,1);body.Children.Add(optionScroll);
        duration=media?.Duration??1;
        if(bitmap is not null)
        {
            drawing.Width=bitmap.PixelWidth;drawing.Height=bitmap.PixelHeight;picture.Width=bitmap.PixelWidth;picture.Height=bitmap.PixelHeight;picture.Source=bitmap.ToAvalonia();drawing.Children.Add(picture);
            Control visual=drawing;
            if(media is not null){var layered=new Grid{Width=bitmap.PixelWidth,Height=bitmap.PixelHeight};layered.Children.Add(PlayerSurface(bitmap.PixelWidth,bitmap.PixelHeight));layered.Children.Add(drawing);visual=layered;picture.Opacity=0;}
            previewStack.Children.Add(ZoomablePreview(visual));
            drawing.PointerPressed+=BeginDraw;drawing.PointerMoved+=MoveDraw;drawing.PointerReleased+=EndDraw;drawing.PointerCaptureLost+=(_,_)=>FinishGesture();
        }
        else if(media is not null)previewStack.Children.Add(PlayerSurface(0,260));
        else previewStack.Children.Add(Ui.Text("Your files will be saved as new copies.\nChoose options, then save.",21).Also(t=>t.Margin=new(0,50,20,0)));
        if(media is not null)
        {
            if(tool is "trimVideo" or "trimAudio" or "redactAudio")
            {
                timeline.Set(duration,0,duration);previewStack.Children.Add(timeline);
                timeline.Changed+=(a,b)=>{var previousStart=Value("Start");fields["Start"].Text=Number(a);fields["End"].Text=Number(b);Seek(Math.Abs(a-previousStart)>.0001?a:b);};
            }
            var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(Ui.Button("−1s",()=>Seek(PreviewSeconds-1)));controls.Children.Add(Ui.Button("▶ / Ⅱ",TogglePlayback));controls.Children.Add(Ui.Button("+1s",()=>Seek(PreviewSeconds+1)));previewStack.Children.Add(controls);previewStack.Children.Add(time);
            if((media.Video is null&&tool is "normalizeAudio" or "audioChannels" or "redactAudio")||tool is "redactVideo" or "splitVideo" or "audioToVideo")
            {
                var comparison=new StackPanel{Orientation=Orientation.Horizontal};comparison.Children.Add(Ui.Button("Original",PlayOriginalPreview));
                processedButton=Ui.Button(ProcessedLabel,async()=>await PreviewProcessed());comparison.Children.Add(processedButton);previewStack.Children.Add(comparison);
            }
            scrubber.Maximum=duration;L10n.Tip(scrubber,"Preview position");
            scrubber.AddHandler(PointerPressedEvent,(_,_)=>scrubbing=true,RoutingStrategies.Tunnel);scrubber.AddHandler(PointerReleasedEvent,(_,_)=>{scrubbing=false;Seek(scrubber.Value);},RoutingStrategies.Tunnel);
            scrubber.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!syncingScrubber&&scrubbing)Seek(scrubber.Value);};previewStack.Children.Add(scrubber);
            if(tool is "trimAudio" or "trimVideo" or "redactAudio")previewStack.Children.Add(Ui.Button("Play selected range",()=>Guard(PlayRange)));
            previewStack.Children.Add(Ui.Text("Space play/pause · ←/→ 1s · Shift+←/→ 1 frame (video), 0.1s (audio) · I/O set range · Enter save",12,"Muted"));
        }
        if(tool=="convert")
        {
            var formats=outputFormats?.ToHashSet()??(media is not null?(media.Video is null?MediaEngine.AudioFormats:MediaEngine.VideoFormats.Concat(MediaEngine.AudioFormats).ToHashSet()):ImageEngine.Formats.ToHashSet());
            options.Children.Add(Ui.Text("Output format",13).Also(t=>t.FontWeight=FontWeight.SemiBold));L10n.SetChoices(format,formats.Order(StringComparer.Ordinal));options.Children.Add(format);
        }
        if(tool is "trimVideo" or "trimAudio" or "redactAudio"){Field("Start",0);Field("End",duration);}
        if(tool=="resize"){Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);}
        if(tool=="compress")Field("Quality (%)",80);
        if(tool=="changeVideoSpeed"){Field("Speed",1);fields["Speed"].TextChanged+=(_,_)=>{try{if(player is not null)player.Speed=Math.Clamp(Value("Speed",1),.05,32);}catch(ArgumentException){}};}
        if(tool=="splitVideo")Field("Number of clips",2);
        if(tool=="videoSnapshots")TextField("Times (seconds)","0");
        if(tool=="normalizeAudio"){Field("Target LUFS",-16);Field("True peak dB",-1.5);}
        if(tool=="audioChannels"){Field("Channels",2);Field("Left volume",1);Field("Right volume",1);}
        if(tool=="audioToVideo"){Field("Width",1280);Field("Height",720);}
        if(tool=="organizePDF")TextField("Page numbers","1");
        if(tool=="rotate")Field("Degrees",90);
        if(tool is "crop" or "cropVideo" or "redactVideo" or "redactImage")
        {
            options.Children.Add(Ui.Text(tool is "redactVideo" or "redactImage"?"Drag a region to cover with a solid block.":"Drag to select a crop. Hold Shift for a square; existing selections keep their ratio. Press Enter to save.",13).Also(t=>t.Margin=new(4,4,4,16)));
            var w=bitmap?.PixelWidth??media?.Video?.Width??1280;var h=bitmap?.PixelHeight??media?.Video?.Height??720;
            Field("X",0);Field("Y",0);Field("Width",w);Field("Height",h);
            if(tool=="redactVideo"){Field("Start",0);Field("End",duration);}
        }
        if(tool=="annotate")
        {
            L10n.SetChoices(drawingTool,AnnotationTools,"Marker");
            options.Children.Add(Ui.Text("Drawing tool",13));options.Children.Add(drawingTool);TextField("Annotation text","Note");TextField("Ink color","#FF5200");Field("Stroke width",3);Field("Font size",24);
            options.Children.Add(Ui.Text("Drag to draw. One active tool per stroke.",13).Also(t=>t.Margin=new(4,12)));
            options.Children.Add(Ui.Button("Undo last mark",UndoAnnotation));
        }
        BuildPhotoOptions(inputs??[input]);BuildInputOrder(inputs??[input]);BuildCropOptions();BuildCompressionOptions();BuildDocumentOptions();BuildMediaOptions();
        if(options.Children.Count==1)options.Children.Add(Ui.Text("Ready to make a fresh copy. No extra settings needed.",13).Also(t=>t.Margin=new(4,20,4,0)));
        var footer=new DockPanel{Margin=new(0,20,0,0),LastChildFill=false};Grid.SetRow(footer,2);grid.Children.Add(footer);
        var save=Ui.Primary("Save new copy  ↗",Save);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        var cancel=Ui.Button("Cancel",()=>Close(false));DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);
        var reset=Ui.Button("↶ Reset",Reset);DockPanel.SetDock(reset,Dock.Left);footer.Children.Add(reset);
        var safe=Ui.Text("Original stays safe",12,"Muted");safe.Margin=new(0,0,16,0);DockPanel.SetDock(safe,Dock.Right);footer.Children.Add(safe);
        var clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        clock.Tick+=(_,_)=>
        {
            if(player is null)return;var position=player.Position;
            if(!scrubbing){syncingScrubber=true;scrubber.Value=Math.Clamp(position,0,duration);syncingScrubber=false;}
            if(player.Playing){UpdateTime();if(playingRange&&position>=Value("End",duration)){player.Pause();playingRange=false;}}
        };
        clock.Start();
        AddHandler(KeyDownEvent,OnKey,RoutingStrategies.Tunnel);AddHandler(KeyUpEvent,(_,e)=>modifiers=e.KeyModifiers,RoutingStrategies.Tunnel);
        Closed+=(_,_)=>{clock.Stop();player?.Dispose();player=null;previewLifetime.Cancel();DeletePreviewFiles();bitmap?.Dispose();};
    }
    private Control PlayerSurface(double width,double height)
    {
        var host=new Border{Background=Brushes.Black,CornerRadius=new(12),ClipToBounds=true};
        if(width>0){host.Width=width;host.Height=height;host.CornerRadius=new(0);}else host.Height=height;
        mediaHost=host;return host;
    }
    private Border? mediaHost;
    private string ProcessedLabel=>tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed";
    private static string Number(double n)=>n.ToString("0.###",CultureInfo.CurrentCulture);
    private void Field(string name,double value)=>TextField(name,Number(value));
    private void TextField(string name,string value)
    {
        options.Children.Add(Ui.Text(name,13).Also(t=>t.Margin=new(4,12,4,0)));var box=new TextBox{Text=value};AutomationProperties.SetAutomationId(box,name);
        if(name is "Start" or "End")box.LostFocus+=(_,_)=>{try{SyncTimeline();}catch(ArgumentException){}};
        fields.Add(name,box);defaults.Add(name,value);options.Children.Add(box);
    }
    private void Reset()
    {
        foreach(var pair in defaults)fields[pair.Key].Text=pair.Value;while(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);
        selection=null;activeRectangle=null;activeStroke=null;photoRegions.Clear();videoRegions.Clear();bleepRanges.Clear();ReloadRanges();markNumber=1;flipHorizontal.IsChecked=false;flipVertical.IsChecked=false;QueuePhotoPreview();
        timeline.Set(duration,0,duration);if(media is not null){if(processedPlayback)PlayOriginalPreview();Seek(0);}
    }
    private double Value(string key,double fallback=0)
    {
        if(!fields.TryGetValue(key,out var box))return fallback;
        if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))throw new ArgumentException("Enter a valid number for "+key+".");return value;
    }
    private async void Guard(Action action){try{action();}catch(ArgumentException error){await Dialogs.Info(this,error.Message,"Check selected range");}}
    private Point Clamp(Point p)=>new(Math.Clamp(p.X,0,drawing.Width),Math.Clamp(p.Y,0,drawing.Height));
    private async void BeginDraw(object? sender,PointerPressedEventArgs e)
    {
        if(tool is not ("crop" or "cropVideo" or "redactVideo" or "redactImage" or "annotate")||!e.GetCurrentPoint(drawing).Properties.IsLeftButtonPressed)return;
        modifiers=e.KeyModifiers;drawing.Focus();origin=Clamp(e.GetPosition(drawing));e.Pointer.Capture(drawing);dragging=true;e.Handled=true;
        if(BeginCropGesture(origin))return;
        if(tool=="annotate"){try{AnnotationStart();}catch(ArgumentException error){dragging=false;e.Pointer.Capture(null);await Dialogs.Info(this,error.Message,"Check drawing settings");}return;}
        if(tool is not ("redactImage" or "redactVideo")&&selection is not null)drawing.Children.Remove(selection);
        activeRectangle=new Rectangle{StrokeThickness=Math.Max(2,drawing.Width/350),Fill=tool is "redactVideo" or "redactImage"?Brushes.Black:new SolidColorBrush(Color.FromArgb(35,224,189,82)),Stroke=Brushes.Gold,IsHitTestVisible=false};
        drawing.Children.Add(activeRectangle);Canvas.SetLeft(activeRectangle,origin.X);Canvas.SetTop(activeRectangle,origin.Y);selection=activeRectangle;
    }
    private void MoveDraw(object? sender,PointerEventArgs e)
    {
        if(!dragging)return;modifiers=e.KeyModifiers;var p=Clamp(e.GetPosition(drawing));
        if(MoveCropGesture(p))return;
        if(tool=="annotate"){AnnotationMove(p);return;}
        if(activeRectangle is null)return;var dx=p.X-origin.X;var dy=p.Y-origin.Y;
        if(tool is "crop" or "cropVideo"&&(modifiers.HasFlag(KeyModifiers.Shift)||CropRatio() is not null))
        {
            var ratio=CropRatio()??1d;var width=Math.Min(Math.Abs(dx),Math.Abs(dy)*ratio);var height=width/ratio;p=new(origin.X+Math.CopySign(width,dx),origin.Y+Math.CopySign(height,dy));
        }
        var x=Math.Min(origin.X,p.X);var y=Math.Min(origin.Y,p.Y);var w=Math.Abs(p.X-origin.X);var h=Math.Abs(p.Y-origin.Y);
        Canvas.SetLeft(activeRectangle,x);Canvas.SetTop(activeRectangle,y);activeRectangle.Width=w;activeRectangle.Height=h;
        fields["X"].Text=Number(Math.Floor(x));fields["Y"].Text=Number(Math.Floor(y));fields["Width"].Text=Number(Math.Max(1,Math.Floor(w)));fields["Height"].Text=Number(Math.Max(1,Math.Floor(h)));
    }
    private void EndDraw(object? sender,PointerReleasedEventArgs e){if(!dragging)return;e.Pointer.Capture(null);FinishGesture();e.Handled=true;}
    private void FinishGesture()
    {
        if(!dragging)return;
        if(tool=="redactImage"&&activeRectangle is {Width:>=1,Height:>=1} r)photoRegions.Add(new Rect(Canvas.GetLeft(r),Canvas.GetTop(r),r.Width,r.Height));
        if(tool=="redactVideo"&&activeRectangle is {Width:>=2,Height:>=2} v){try{videoRegions.Add(new(Canvas.GetLeft(v),Canvas.GetTop(v),v.Width,v.Height,Value("Start"),Value("End",duration)));}catch(ArgumentException){drawing.Children.Remove(v);}ReloadRanges();}
        dragging=false;cropGesture=null;activeStroke=null;activeRectangle=null;annotationShape=null;
    }
    private void Seek(double seconds){if(player is null)return;player.Seek(Math.Clamp(seconds,0,duration));UpdateTime();}
    private void UpdateTime()=>time.Text=$"{TimeSpan.FromSeconds(PreviewSeconds):mm\\:ss\\.fff} / {TimeSpan.FromSeconds(duration):mm\\:ss\\.fff}";
    private void TogglePlayback(){if(player is null)return;playingRange=false;player.Toggle();}
    private async void OnKey(object? sender,KeyEventArgs e)
    {
        modifiers=e.KeyModifiers;var focused=FocusManager?.GetFocusedElement();
        if(e.Key==Key.Escape){Close(false);e.Handled=true;return;}
        if(focused is Button&&e.KeyModifiers==KeyModifiers.None&&e.Key is Key.Space or Key.Enter)return;
        if(e.Key==Key.Enter&&e.KeyModifiers==KeyModifiers.None){Save();e.Handled=true;return;}
        if(ClipboardImport.IsEditing(focused))return;
        if(tool=="annotate"&&e.Key==Key.Z&&e.KeyModifiers==KeyModifiers.Control){UndoAnnotation();e.Handled=true;return;}
        if(tool=="annotate"&&e.Key==Key.Delete&&selectedAnnotation is not null){drawing.Children.Remove(selectedAnnotation);selectedAnnotation=null;e.Handled=true;return;}
        if(media is null||player is null||e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))return;
        var shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        try
        {
            switch(e.Key)
            {
                case Key.Space:if(shift){PlayRange();break;}TogglePlayback();break;
                case Key.Left:Seek(PreviewSeconds-(shift?FineSeekStep:1));break;
                case Key.Right:Seek(PreviewSeconds+(shift?FineSeekStep:1));break;
                case Key.I:if(fields.TryGetValue("Start",out var start)&&PreviewSeconds<Value("End",duration))start.Text=Number(PreviewSeconds);SyncTimeline();break;
                case Key.O:if(fields.TryGetValue("End",out var end)&&PreviewSeconds>Value("Start"))end.Text=Number(PreviewSeconds);SyncTimeline();break;
                default:return;
            }
        }
        catch(ArgumentException error){await Dialogs.Info(this,error.Message,"Check selected range");}
        e.Handled=true;
    }
    private void SyncTimeline(){if(fields.ContainsKey("Start")&&fields.ContainsKey("End"))timeline.Set(duration,Value("Start"),Value("End",duration));}
    private double FineSeekStep=>media?.Video is {} video?1/Math.Max(1,video.FrameRate):.1;
    private void Save()=>_ = ReadSettings(true);
    private async Task<bool> ReadSettings(bool accept)
    {
        try
        {
            if(tool=="compress"&&(Value("Target size (KB; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")<0||Value("Reduction (%; 0 = auto)")>=100))throw new ArgumentException("Choose a positive size or a reduction below 100%. Zero uses automatic compression.");
            if(tool is "trimVideo" or "trimAudio" or "redactAudio"&&(Value("Start")<0||Value("End")>duration||Value("Start")>=Value("End")))throw new ArgumentException("Start must be before End, within the media duration.");
            var x=Value("X");var y=Value("Y");var w=Value("Width");var h=Value("Height");
            if(tool is "crop" or "cropVideo" or "redactVideo"&&(w<1||h<1))throw new ArgumentException("Drag an area first.");
            MediaOptions=new(){Start=Value("Start"),End=fields.ContainsKey("End")?Value("End"):null,Quality=Value("Quality (%)",80)/100,Speed=Value("Speed",1),Width=fields.ContainsKey("Width")?(int)w:null,Height=fields.ContainsKey("Height")?(int)h:null,Channels=(int)Value("Channels",2),LeftVolume=Value("Left volume",1),RightVolume=Value("Right volume",1),EqualClips=(int)Value("Number of clips",2),TargetLoudness=Value("Target LUFS",-16),TruePeak=Value("True peak dB",-1.5),
                Regions=tool is "cropVideo" or "redactVideo"?[new(x,y,w,h,Value("Start"),fields.ContainsKey("End")?Value("End"):null)]:[],AudioRedactions=tool=="redactAudio"?[new(Value("Start"),Value("End"))]:[],
                SnapshotTimes=tool=="videoSnapshots"?(fields["Times (seconds)"].Text??"").Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.CurrentCulture)).ToArray():[0]};
            ImageFrame? annotated=tool=="annotate"?RenderAnnotation():null;
            ImageOptions=PhotoSettings(new(fields.ContainsKey("Width")?(int)w:null,fields.ContainsKey("Height")?(int)h:null,(int)Value("Degrees",tool=="rotate"?90:0),tool=="crop"?new ImageRect((int)x,(int)y,(int)w,(int)h):null,(int)Value("Quality (%)",85),true,annotated));
            MediaOptions=MoreMediaSettings(MediaOptions);
            Format=L10n.Selected(format)??"png";
            if(tool=="organizePDF")Pages=(fields["Page numbers"].Text??"").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(s=>int.Parse(s.Trim(),CultureInfo.InvariantCulture)).ToArray();
            DocumentOptions=new((int)Value("DPI",144),L10n.Selected(documentLayout)??"editable",L10n.Selected(epubLayout)??"reflowable",archivePassword.Text??"");if(accept)Close(true);return true;
        }
        catch(Exception error)when(error is ArgumentException or FormatException or OverflowException){await Dialogs.Info(this,error.Message,"Check your settings");return false;}
    }
}
