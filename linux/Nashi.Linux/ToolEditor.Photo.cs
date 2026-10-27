using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class ToolEditor
{
    private static readonly string[] AnnotationTools=["Marker","Rectangle","Arrow","Line","Ellipse","Text","Number","Callout","Magnifier","Image","Freehand","Select / Move"];
    private readonly List<Rect> photoRegions=[];
    private readonly ComboBox backdrop=L10n.Choices(["solid","gradient","image"]),collageLayout=L10n.Choices(["grid","row","column","featured"]),redactionStyle=L10n.Choices(["solid","blur","pixelate"]);
    private string? backgroundImage,annotationImage;private string[] collageInputs=[];private readonly ListBox imageOrder=new(){MaxHeight=140};
    private Control? annotationShape,selectedAnnotation;private Vector annotationOffset;private int strokeKind;private int markNumber=1;
    private Polyline? activeStroke;private readonly List<Point> strokePoints=[];
    private DispatcherTimer? photoPreview;private bool showingOriginal;private readonly SemaphoreSlim previewGate=new(1,1);private long photoGeneration;
    private readonly CheckBox fitCollage=new(){IsChecked=true},flipHorizontal=new(),flipVertical=new();
    private void BuildPhotoOptions(IReadOnlyList<string> inputs)
    {
        collageInputs=inputs.ToArray();L10n.Bind(fitCollage,ContentControl.ContentProperty,"Fill cells (crop to fit)");L10n.Bind(flipHorizontal,ContentControl.ContentProperty,"Flip horizontally");L10n.Bind(flipVertical,ContentControl.ContentProperty,"Flip vertically");
        if(tool=="editImage")
        {
            Field("Width",bitmap?.PixelWidth??1280);Field("Height",bitmap?.PixelHeight??720);Field("Degrees",0);options.Children.Add(flipHorizontal);options.Children.Add(flipVertical);
            flipHorizontal.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();flipVertical.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();
            var rotation=new WrapPanel();rotation.Children.Add(Ui.Button("Rotate Left",()=>RotatePhoto(-90)));rotation.Children.Add(Ui.Button("Rotate Right",()=>RotatePhoto(90)));options.Children.Add(rotation);
            PhotoGroup("Light",[("Exposure",-3,3),("Brightness",-1,1),("Contrast",0,2),("Highlights",-1,1),("Shadows",-1,1),("Whites",-1,1),("Blacks",-1,1),("Gamma",.2,3)]);
            PhotoGroup("Color",[("Temperature",-1,1),("Tint",-1,1),("Saturation",0,2),("Vibrance",-1,1),("Hue",-180,180)]);
            PhotoGroup("Detail",[("Clarity",0,1),("Texture",-1,1),("Sharpen",0,2),("Dehaze",0,1),("Grain",0,1),("NoiseReduction",0,1),("Vignette",0,2),("Fade",0,1),("Blur",0,30)]);
        }
        if(tool=="frameImage")
        {
            options.Children.Add(Ui.Text("Background",13));options.Children.Add(backdrop);TextField("Background color","#FFFFFF");TextField("Gradient end","#FFD85C");
            options.Children.Add(Ui.Button("Choose background image…",async()=>{var picked=await Dialogs.PickFiles(this,"Choose background image…",false,Dialogs.Images);if(picked.Length>0){backgroundImage=picked[0];L10n.Select(backdrop,"image");QueuePhotoPreview();}}));
            Field("Padding",80);Field("Corner radius",24);Field("Shadow radius",24);Field("Shadow opacity",.25);Field("Background blur",0);Field("Image scale",1);Field("Position X",.5);Field("Position Y",.5);Field("Canvas width",(bitmap?.PixelWidth??1000)+160);Field("Canvas height",(bitmap?.PixelHeight??1000)+160);
        }
        if(tool=="createCollage")
        {
            options.Children.Add(Ui.Text("Layout",13));options.Children.Add(collageLayout);Field("Columns",2);Field("Canvas width",2048);Field("Canvas height",2048);Field("Spacing",12);Field("Padding",16);Field("Corner radius",0);TextField("Background color","#FFFFFF");
            imageOrder.ItemsSource=inputs.Select((path,n)=>new ListBoxItem{Content=Path.GetFileName(path),Tag=n}).ToList();options.Children.Add(imageOrder);options.Children.Add(fitCollage);fitCollage.IsCheckedChanged+=(_,_)=>QueuePhotoPreview();
            var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Move up",()=>MoveImage(-1)));buttons.Children.Add(Ui.Button("Move down",()=>MoveImage(1)));options.Children.Add(buttons);
        }
        if(tool=="redactImage")
        {
            options.Children.Add(Ui.Text("Cover style",13));options.Children.Add(redactionStyle);
            options.Children.Add(Ui.Button("Detect text",async()=>await DetectPhoto(true,false)));options.Children.Add(Ui.Button("Detect faces",async()=>await DetectPhoto(false,true)));
            options.Children.Add(Ui.Button("Undo area",()=>{if(photoRegions.Count==0)return;photoRegions.RemoveAt(photoRegions.Count-1);if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);}));
        }
        if(tool is "editImage" or "frameImage" or "createCollage")
        {
            photoPreview=new(){Interval=TimeSpan.FromMilliseconds(300)};photoPreview.Tick+=async(_,_)=>{photoPreview.Stop();await PreviewPhoto();};
            foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueuePhotoPreview();backdrop.SelectionChanged+=(_,_)=>QueuePhotoPreview();collageLayout.SelectionChanged+=(_,_)=>QueuePhotoPreview();Closed+=(_,_)=>photoPreview.Stop();
            var original=new CheckBox{Margin=new(4,14,4,4)};L10n.Bind(original,ContentControl.ContentProperty,"Show original");
            original.IsCheckedChanged+=(_,_)=>{if(original.IsChecked==true){showingOriginal=true;photoPreview.Stop();photoGeneration++;if(bitmap is not null)SetPhotoPreview(bitmap.ToAvalonia(),bitmap.PixelWidth,bitmap.PixelHeight);}else{showingOriginal=false;QueuePhotoPreview();}};
            previewStack.Children.Add(original);Opened+=(_,_)=>QueuePhotoPreview();
        }
        if(tool=="annotate")options.Children.Add(Ui.Button("Choose annotation image…",async()=>{var picked=await Dialogs.PickFiles(this,"Choose annotation image…",false,Dialogs.Images);if(picked.Length>0){annotationImage=picked[0];L10n.Select(drawingTool,"Image");}}));
    }
    private void RotatePhoto(int degrees){fields["Degrees"].Text=Number(Value("Degrees")+degrees);var width=fields["Width"].Text;fields["Width"].Text=fields["Height"].Text;fields["Height"].Text=width;QueuePhotoPreview();}
    private void PhotoGroup(string title,(string Name,double Minimum,double Maximum)[] controls)
    {
        options.Children.Add(Ui.Text(title,15,"Muted").Also(t=>{t.FontWeight=FontWeight.SemiBold;t.Margin=new(4,16,4,4);}));
        foreach(var setting in controls)
        {
            var value=(double)typeof(PhotoAdjustments).GetProperty(setting.Name)!.GetValue(new PhotoAdjustments())!;Field(setting.Name,value);var field=fields[setting.Name];options.Children.Remove(field);options.Children.RemoveAt(options.Children.Count-1);
            var row=new DockPanel{Margin=new(4,8,4,0)};field.Width=72;field.Margin=new(4,0,0,0);field.Padding=new(7,3);DockPanel.SetDock(field,Dock.Right);row.Children.Add(field);row.Children.Add(Ui.Text(setting.Name=="NoiseReduction"?"Noise reduction":setting.Name,13));options.Children.Add(row);
            var slider=new Slider{Minimum=setting.Minimum,Maximum=setting.Maximum,Value=value,Margin=new(4,2)};L10n.Tip(slider,setting.Name);var syncing=false;
            slider.PropertyChanged+=(_,e)=>{if(e.Property!=RangeBase.ValueProperty||syncing)return;syncing=true;field.Text=Number(slider.Value);syncing=false;};
            field.TextChanged+=(_,_)=>{if(syncing||!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var number)||!double.IsFinite(number))return;syncing=true;slider.Value=number;syncing=false;};options.Children.Add(slider);
        }
    }
    private Control ZoomablePreview(Control visual)
    {
        var zoom=1d;var result=new StackPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal};var viewbox=new Viewbox{Child=visual,Stretch=Stretch.Uniform,Width=600,Height=420};
        var scroll=new ScrollViewer{Content=viewbox,Height=420,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var percent=new TextBlock{Text="100%",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0)}.Res(TextBlock.ForegroundProperty,"Muted");
        void SetZoom(double value)
        {
            zoom=Math.Clamp(value,.25,4);var height=Math.Clamp(Bounds.Height-360,180,420);scroll.Height=height;
            var viewport=scroll.Viewport.Width>0?scroll.Viewport:new Size(Math.Max(100,scroll.Bounds.Width),height);viewbox.Width=Math.Max(100,viewport.Width)*zoom;viewbox.Height=(viewport.Height>0?viewport.Height:height)*zoom;percent.Text=$"{zoom*100:0}%";
        }
        controls.Children.Add(Ui.Button("−",()=>SetZoom(zoom/1.25)));controls.Children.Add(Ui.Button("Fit",()=>SetZoom(1)));controls.Children.Add(Ui.Button("+",()=>SetZoom(zoom*1.25)));controls.Children.Add(percent);
        result.Children.Add(controls);result.Children.Add(scroll);Avalonia.Automation.AutomationProperties.SetAutomationId(scroll,"ImagePreviewViewport");
        scroll.SizeChanged+=(_,_)=>SetZoom(zoom);SizeChanged+=(_,_)=>SetZoom(zoom);return result;
    }
    private void MoveImage(int delta)
    {
        if(imageOrder.ItemsSource is not List<ListBoxItem> items)return;var index=imageOrder.SelectedIndex;var next=index+delta;if(index<0||next<0||next>=items.Count)return;
        var item=items[index];items.RemoveAt(index);items.Insert(next,item);imageOrder.ItemsSource=null;imageOrder.ItemsSource=items;imageOrder.SelectedIndex=next;QueuePhotoPreview();
    }
    private void QueuePhotoPreview(){if(photoPreview is null||showingOriginal)return;photoPreview.Stop();photoPreview.Start();}
    private ImageOptions PhotoSettings(ImageOptions original)
    {
        var changes=new Dictionary<string,double>();foreach(var property in typeof(PhotoAdjustments).GetProperties())changes[property.Name]=Value(property.Name,(double)property.GetValue(new PhotoAdjustments())!);
        var adjustments=System.Text.Json.JsonSerializer.Deserialize<PhotoAdjustments>(System.Text.Json.JsonSerializer.Serialize(changes))!;
        var order=imageOrder.ItemsSource is List<ListBoxItem> items&&items.Count>0?items.Select(i=>(int)i.Tag!).ToArray():null;
        return original with
        {
            TargetBytes=Value("Target size (KB; 0 = auto)")>0?(long)(Value("Target size (KB; 0 = auto)")*1024):null,ReductionPercent=Value("Reduction (%; 0 = auto)")>0?Value("Reduction (%; 0 = auto)"):null,
            FlipHorizontal=flipHorizontal.IsChecked==true,FlipVertical=flipVertical.IsChecked==true,Adjustments=adjustments,
            Frame=new((int)Value("Padding",80),Value("Corner radius",24),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",L10n.Selected(backdrop)??"solid",fields.GetValueOrDefault("Gradient end")?.Text??"#FFD85C",backgroundImage,Value("Background blur"),Value("Shadow radius",24),Value("Shadow opacity",.25),fields.ContainsKey("Canvas width")?(int)Value("Canvas width"):null,fields.ContainsKey("Canvas height")?(int)Value("Canvas height"):null,Value("Image scale",1),Value("Position X",.5),Value("Position Y",.5)),
            Collage=new(L10n.Selected(collageLayout)??"grid",(int)Value("Columns",2),(int)Value("Canvas width",2048),(int)Value("Canvas height",2048),(int)Value("Spacing",12),(int)Value("Padding",16),Value("Corner radius"),fields.GetValueOrDefault("Background color")?.Text??"#FFFFFF",fitCollage.IsChecked==true,order),
            Redactions=photoRegions.ToArray(),RedactionStyle=L10n.Selected(redactionStyle)??"solid"
        };
    }
    private async Task PreviewPhoto()
    {
        if(bitmap is null||!IsVisible||showingOriginal)return;var generation=++photoGeneration;
        if(!await previewGate.WaitAsync(0)){QueuePhotoPreview();return;}
        try
        {
            var settings=PhotoSettings(new((int)Value("Width",bitmap.PixelWidth),(int)Value("Height",bitmap.PixelHeight),(int)Value("Degrees",0)));var source=bitmap;var inputs=collageInputs;var currentTool=tool;
            var (image,width,height)=await Task.Run(()=>
            {
                ImageFrame result;
                if(currentTool=="editImage")
                {
                    var factor=Math.Min(1,1200d/Math.Max(source.PixelWidth,source.PixelHeight));
                    using var small=factor<1?ImageEngine.Resize(source,Math.Max(1,(int)(source.PixelWidth*factor)),Math.Max(1,(int)(source.PixelHeight*factor))):source.Copy();
                    using var adjusted=ImageFrame.FromPng(PhotoCodec.Adjust(small.ToPng(),settings.Adjustments));
                    result=ImageEngine.TransformPhoto(adjusted,settings with{Width=Math.Max(1,(int)Math.Round((settings.Width??source.PixelWidth)*factor)),Height=Math.Max(1,(int)Math.Round((settings.Height??source.PixelHeight)*factor))});
                }
                else if(currentTool=="frameImage")result=ImageEngine.Frame(source,settings.Frame);
                else result=ImageEngine.Collage(inputs,settings.Collage,CancellationToken.None);
                using(result)return(result.ToAvalonia(),result.PixelWidth,result.PixelHeight);
            });
            if(!IsVisible||showingOriginal||generation!=photoGeneration)return;SetPhotoPreview(image,width,height);ToolTip.SetTip(drawing,null);
        }
        catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or ImageMagick.MagickException or IOException or NotSupportedException){ToolTip.SetTip(drawing,error.Message);}
        finally{previewGate.Release();}
    }
    private void SetPhotoPreview(Bitmap result,int width,int height){drawing.Width=width;drawing.Height=height;picture.Source=result;picture.Width=width;picture.Height=height;}
    private async Task DetectPhoto(bool text,bool faces)
    {
        if(bitmap is null)return;
        try
        {
            var regions=await NativePdf.DetectAsync(bitmap,text,faces);
            foreach(var r in regions){photoRegions.Add(r);var shape=new Rectangle{Width=r.Width,Height=r.Height,Fill=Brushes.Black,Stroke=Brushes.Gold,StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(shape,r.X);Canvas.SetTop(shape,r.Y);drawing.Children.Add(shape);}
            if(regions.Length==0)await Dialogs.Info(this,"No matching regions were detected. You can still draw areas by hand.","Redact Photo");
        }
        catch(Exception error)when(error is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException){await Dialogs.Info(this,error.Message,"Detection unavailable");}
    }
    private int AnnotationKind=>Array.IndexOf(AnnotationTools,L10n.Selected(drawingTool)??"Marker");
    private void AnnotationStart()
    {
        strokeKind=AnnotationKind;
        if(strokeKind==11)
        {
            selectedAnnotation=drawing.Children.Skip(1).Reverse().FirstOrDefault(element=>{var bounds=element.Bounds;if(element.RenderTransform is TranslateTransform t)bounds=bounds.Translate(new Vector(t.X,t.Y));return bounds.Inflate(8).Contains(origin);});
            annotationShape=selectedAnnotation;annotationOffset=selectedAnnotation?.RenderTransform is TranslateTransform transform?new(transform.X,transform.Y):default;return;
        }
        var ink=new SolidColorBrush(ImageEngine.ParseColor(fields["Ink color"].Text??"").ToAvaloniaColor());var thickness=Math.Clamp(Value("Stroke width",3),1,100);
        if(strokeKind is 0 or 10){strokePoints.Clear();strokePoints.Add(origin);activeStroke=new Polyline{Stroke=ink,Opacity=strokeKind==0?.55:1,StrokeThickness=strokeKind==0?thickness*5:thickness,StrokeJoin=PenLineJoin.Round,StrokeLineCap=PenLineCap.Round,IsHitTestVisible=false,Points=strokePoints.ToList()};annotationShape=activeStroke;}
        else if(strokeKind is 2 or 3)annotationShape=new Polyline{Stroke=ink,StrokeThickness=thickness,StrokeJoin=PenLineJoin.Round,StrokeLineCap=PenLineCap.Round,IsHitTestVisible=false};
        else if(strokeKind==4)annotationShape=new Ellipse{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        else if(strokeKind is 5 or 6 or 7)
        {
            var text=strokeKind==6?(markNumber++).ToString(CultureInfo.InvariantCulture):fields["Annotation text"].Text??"";
            annotationShape=new Border{Background=strokeKind is 6 or 7?ink:Brushes.Transparent,CornerRadius=new(strokeKind==6?100:8),Padding=new(strokeKind==6?10:4),IsHitTestVisible=false,Child=new TextBlock{Text=text,Foreground=strokeKind is 6 or 7?Brushes.White:ink,FontSize=Math.Clamp(Value("Font size",24),6,500),TextWrapping=TextWrapping.Wrap}};
        }
        else if(strokeKind is 8 or 9)
        {
            if(strokeKind==9&&annotationImage is null){dragging=false;return;}
            Bitmap source;if(strokeKind==9){using var frame=ImageEngine.Load(annotationImage!);source=frame.ToAvalonia();}else source=(Bitmap)picture.Source!;
            annotationShape=new Image{Source=source,Stretch=Stretch.Fill,IsHitTestVisible=false};
        }
        else annotationShape=new Rectangle{Stroke=ink,StrokeThickness=thickness,IsHitTestVisible=false};
        drawing.Children.Add(annotationShape);Canvas.SetLeft(annotationShape,annotationShape is Polyline?0:origin.X);Canvas.SetTop(annotationShape,annotationShape is Polyline?0:origin.Y);
    }
    private void UndoAnnotation(){if(drawing.Children.Count>1)drawing.Children.RemoveAt(drawing.Children.Count-1);selectedAnnotation=null;}
    private void AnnotationMove(Point point)
    {
        if(annotationShape is null)return;
        if(strokeKind==11){var delta=point-origin;annotationShape.RenderTransform=new TranslateTransform(annotationOffset.X+delta.X,annotationOffset.Y+delta.Y);return;}
        if(activeStroke is not null){strokePoints.Add(point);activeStroke.Points=strokePoints.ToList();return;}
        if(annotationShape is Polyline line)
        {
            var points=new List<Point>{origin,point};
            if(strokeKind==2){var a=Math.Atan2(point.Y-origin.Y,point.X-origin.X);var size=Math.Max(12,line.StrokeThickness*4);points.Add(new(point.X-size*Math.Cos(a-.5),point.Y-size*Math.Sin(a-.5)));points.Add(point);points.Add(new(point.X-size*Math.Cos(a+.5),point.Y-size*Math.Sin(a+.5)));}
            line.Points=points;return;
        }
        var bounds=new Rect(origin,point).Normalize();if(modifiers.HasFlag(KeyModifiers.Shift)){var side=Math.Min(bounds.Width,bounds.Height);bounds=new Rect(bounds.X,bounds.Y,side,side);}
        Canvas.SetLeft(annotationShape,bounds.X);Canvas.SetTop(annotationShape,bounds.Y);annotationShape.Width=Math.Max(1,bounds.Width);annotationShape.Height=Math.Max(1,bounds.Height);
        if(strokeKind==8&&annotationShape is Image magnifier&&picture.Source is Bitmap source)
        {
            var width=Math.Max(1,(int)bounds.Width/2);var height=Math.Max(1,(int)bounds.Height/2);var size=source.PixelSize;
            var crop=new PixelRect(Math.Clamp((int)bounds.X,0,Math.Max(0,size.Width-width)),Math.Clamp((int)bounds.Y,0,Math.Max(0,size.Height-height)),Math.Min(width,size.Width),Math.Min(height,size.Height));
            magnifier.Source=new CroppedBitmap(source,crop);
        }
    }
    /// The annotated pixels exactly as drawn: the canvas is rendered at its 1:1 image size.
    private ImageFrame RenderAnnotation()
    {
        if(bitmap is null)throw new ArgumentException("Choose an image to annotate.");
        selectedAnnotation=null;var size=new PixelSize(bitmap.PixelWidth,bitmap.PixelHeight);
        using var target=new RenderTargetBitmap(size,new Vector(96,96));
        var scale=drawing.RenderTransform;drawing.Measure(new Size(size.Width,size.Height));drawing.Arrange(new Rect(drawing.Bounds.Position,new Size(size.Width,size.Height)));
        target.Render(drawing);drawing.RenderTransform=scale;
        using var stream=new MemoryStream();target.Save(stream);return ImageFrame.FromPng(stream.ToArray());
    }
}

internal static class ColorConversions
{
    internal static Color ToAvaloniaColor(this SkiaSharp.SKColor color)=>Color.FromArgb(color.Alpha,color.Red,color.Green,color.Blue);
}
