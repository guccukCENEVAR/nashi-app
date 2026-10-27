using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class ToolEditor
{
    // Input order (merge PDFs, join videos).
    private readonly ListBox inputOrder=new(){MaxHeight=340};
    internal string[] OrderedInputs{get;private set;}=[];
    private void BuildInputOrder(IReadOnlyList<string> inputs)
    {
        OrderedInputs=inputs.ToArray();if(tool is not ("joinVideos" or "mergePDFs"))return;
        options.Children.Add(Ui.Text("File order",13).Also(t=>t.Margin=new(4,12,4,4)));var items=inputs.Select(path=>new ListBoxItem{Content=Path.GetFileName(path),Tag=path}).ToList();inputOrder.ItemsSource=items;options.Children.Add(inputOrder);
        void Move(int delta)
        {
            var i=inputOrder.SelectedIndex;var next=i+delta;if(i<0||next<0||next>=items.Count)return;var item=items[i];items.RemoveAt(i);items.Insert(next,item);
            inputOrder.ItemsSource=null;inputOrder.ItemsSource=items;inputOrder.SelectedIndex=next;OrderedInputs=items.Select(x=>(string)x.Tag!).ToArray();
        }
        var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Move up",()=>Move(-1)));buttons.Children.Add(Ui.Button("Move down",()=>Move(1)));options.Children.Add(buttons);
    }

    // Crop gestures: move inside, resize near a corner, start anew outside.
    private string? cropGesture;private Rect cropBefore;private readonly ComboBox aspectRatio=new();
    private void BuildCropOptions()
    {
        if(tool is not ("crop" or "cropVideo"))return;
        aspectRatio.ItemsSource=new[]{"Free","1:1","16:9","9:16","4:3","3:4"}.Select(v=>v=="Free"?L10n.Bind(new ComboBoxItem{Tag=v},ContentControl.ContentProperty,"Free"):new ComboBoxItem{Content=v,Tag=v}).ToArray();aspectRatio.SelectedIndex=0;
        options.Children.Add(Ui.Text("Aspect ratio",13).Also(t=>t.Margin=new(4,12,4,0)));options.Children.Add(aspectRatio);
        aspectRatio.SelectionChanged+=(_,_)=>{var ratio=CropRatio();if(ratio is null)return;var w=Math.Min(drawing.Width,drawing.Height*ratio.Value);var h=w/ratio.Value;SetCrop(new((drawing.Width-w)/2,(drawing.Height-h)/2,w,h));};
        options.Children.Add(Ui.Text("Drag inside to move. Drag near a corner to resize. Drag outside to start a new selection.",13).Also(t=>t.Margin=new(4,10)));
    }
    private double? CropRatio(){if((aspectRatio.SelectedItem as ComboBoxItem)?.Tag is not string value||value=="Free")return null;var p=value.Split(':');return double.Parse(p[0],System.Globalization.CultureInfo.InvariantCulture)/double.Parse(p[1],System.Globalization.CultureInfo.InvariantCulture);}
    private bool BeginCropGesture(Point point)
    {
        cropGesture=null;if(tool is not ("crop" or "cropVideo")||selection is null||selection.Width<1||selection.Height<1)return false;
        cropBefore=new(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height);var tolerance=Math.Max(10,drawing.Width/45);
        foreach(var (kind,corner) in new[]{("NW",cropBefore.TopLeft),("NE",cropBefore.TopRight),("SW",cropBefore.BottomLeft),("SE",cropBefore.BottomRight)})if(Point.Distance(point,corner)<=tolerance){cropGesture=kind;return true;}
        if(cropBefore.Contains(point)){cropGesture="move";return true;}return false;
    }
    private bool MoveCropGesture(Point point)
    {
        if(cropGesture is null)return false;var delta=point-origin;
        if(cropGesture=="move"){SetCrop(new(Math.Clamp(cropBefore.X+delta.X,0,drawing.Width-cropBefore.Width),Math.Clamp(cropBefore.Y+delta.Y,0,drawing.Height-cropBefore.Height),cropBefore.Width,cropBefore.Height));return true;}
        var anchor=cropGesture switch{"NW"=>cropBefore.BottomRight,"NE"=>cropBefore.BottomLeft,"SW"=>cropBefore.TopRight,_=>cropBefore.TopLeft};var dx=point.X-anchor.X;var dy=point.Y-anchor.Y;var w=Math.Abs(dx);var h=Math.Abs(dy);
        var ratio=CropRatio()??(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)?cropBefore.Width/cropBefore.Height:(double?)null);if(ratio is {} r){w=Math.Min(w,h*r);h=w/r;}
        SetCrop(new(Math.Min(anchor.X,anchor.X+Math.CopySign(w,dx)),Math.Min(anchor.Y,anchor.Y+Math.CopySign(h,dy)),Math.Max(1,w),Math.Max(1,h)));return true;
    }
    private void SetCrop(Rect rect)
    {
        if(selection is null){selection=new Rectangle{Stroke=Brushes.Gold,StrokeThickness=Math.Max(2,drawing.Width/350),Fill=new SolidColorBrush(Color.FromArgb(35,224,189,82)),IsHitTestVisible=false};drawing.Children.Add(selection);}
        Canvas.SetLeft(selection,rect.X);Canvas.SetTop(selection,rect.Y);selection.Width=rect.Width;selection.Height=rect.Height;
        fields["X"].Text=Number(Math.Floor(rect.X));fields["Y"].Text=Number(Math.Floor(rect.Y));fields["Width"].Text=Number(Math.Floor(rect.Width));fields["Height"].Text=Number(Math.Floor(rect.Height));
    }

    // Compression targets and image size presets.
    private void BuildCompressionOptions()
    {
        if(tool!="compress")return;var preset=L10n.Choices(["balanced","strong"],"balanced");options.Children.Add(Ui.Text("Compression preset",13).Also(t=>t.Margin=new(4,12,4,0)));options.Children.Add(preset);
        preset.SelectionChanged+=(_,_)=>fields["Quality (%)"].Text=L10n.Selected(preset)=="balanced"?"80":"55";
        Field("Target size (KB; 0 = auto)",0);Field("Reduction (%; 0 = auto)",0);
        if(bitmap is null)return;
        Field("Width",bitmap.PixelWidth);Field("Height",bitmap.PixelHeight);
        var sizes=new[]{"Original dimensions","75%","50%","25%","2560 px longest edge","1920 px longest edge","1280 px longest edge","Custom dimensions"};
        var size=new ComboBox{ItemsSource=sizes.Select(s=>L10n.Bind(new ComboBoxItem(),ContentControl.ContentProperty,s)).ToArray(),SelectedIndex=0};options.Children.Add(Ui.Text("Image size",13).Also(t=>t.Margin=new(4,12,4,0)));options.Children.Add(size);
        size.SelectionChanged+=(_,_)=>
        {
            if(size.SelectedIndex==7)return;var longest=Math.Max(bitmap.PixelWidth,bitmap.PixelHeight);
            var factor=size.SelectedIndex switch{1=>.75,2=>.5,3=>.25,4=>Math.Min(1,2560d/longest),5=>Math.Min(1,1920d/longest),6=>Math.Min(1,1280d/longest),_=>1};
            fields["Width"].Text=Number(Math.Max(1,Math.Round(bitmap.PixelWidth*factor)));fields["Height"].Text=Number(Math.Max(1,Math.Round(bitmap.PixelHeight*factor)));
        };
    }

    // Documents: page ranges, passwords and layouts.
    internal string PageRanges=>fields.GetValueOrDefault("Page ranges")?.Text??"";
    internal int PagesPerFile=>(int)Value("Pages per file",1);
    public DocumentOptions DocumentOptions{get;private set;}=new();
    private readonly ComboBox documentLayout=L10n.Choices(["editable","preserve"]),epubLayout=L10n.Choices(["reflowable","fixed"]);private readonly TextBox archivePassword=new(){PasswordChar='•'};
    private void BuildDocumentOptions()
    {
        if(tool=="splitPDF"){Field("Pages per file",1);TextField("Page ranges","");}
        var password=false;
        if(FeatureCatalog.Extension(input)=="pdf"){options.Children.Add(Ui.Text("PDF password (if needed)",13).Also(t=>t.Margin=new(4,12,4,0)));options.Children.Add(archivePassword);password=true;}
        if(tool is "convert"&&media is null&&bitmap is null){Field("DPI",144);options.Children.Add(Ui.Text("PDF → Office layout",13));options.Children.Add(documentLayout);options.Children.Add(Ui.Text("EPUB layout",13));options.Children.Add(epubLayout);}
        if(!password&&(tool=="extractArchive"||FeatureCatalog.IsArchive(input))){options.Children.Add(Ui.Text("Archive password (if needed)",13).Also(t=>t.Margin=new(4,12,4,0)));options.Children.Add(archivePassword);}
    }

    // Saved export profiles per output format.
    private readonly CheckBox preserveMetadata=new(){IsChecked=true,Margin=new(4,12)};
    private OutputProfile exportProfile=new();
    internal void ConfigureProfiles(PreferencesStore preferences,string? requestedFormat=null)
    {
        if(tool!="convert")return;L10n.Bind(preserveMetadata,ContentControl.ContentProperty,"Preserve metadata");
        if(!fields.ContainsKey("Quality (%)"))Field("Quality (%)",85);
        if(media is not null){Field("Audio bitrate (kbps)",256);Field("Maximum video height (0 = original)",0);Field("GIF frames per second",15);Field("GIF width",960);options.Children.Add(Ui.Text("Audio sample depth",13));options.Children.Add(sampleDepth);}
        options.Children.Add(preserveMetadata);
        void Apply()
        {
            if(L10n.Selected(format) is not {} value)return;
            void Show(Control control,bool visible){control.IsVisible=visible;var index=options.Children.IndexOf(control);if(index>0&&options.Children[index-1] is TextBlock label)label.IsVisible=visible;}
            void Field(string name,bool visible){if(fields.TryGetValue(name,out var box))Show(box,visible);}
            var video=media is not null&&MediaEngine.VideoFormats.Contains(value);var audio=media is not null&&MediaEngine.AudioFormats.Contains(value);
            Field("Quality (%)",video&&value!="gif"||bitmap is not null&&new[]{"jpg","webp","heic","avif"}.Contains(value));Field("Audio bitrate (kbps)",(audio||video)&&value is not ("wav" or "aiff" or "flac" or "gif"));Field("Maximum video height (0 = original)",video&&value!="gif");
            Field("GIF frames per second",video&&value=="gif");Field("GIF width",video&&value=="gif");Field("DPI",value is "png" or "jpg" or "epub");Show(sampleDepth,audio&&value is "wav" or "aiff");
            preserveMetadata.IsVisible=media is not null&&value!="gif"||bitmap is not null&&value is not ("pdf" or "bmp" or "gif");Show(documentLayout,FeatureCatalog.Extension(input)=="pdf"&&value is "docx" or "pages");Show(epubLayout,value=="epub");
            exportProfile=preferences.Profile(value);fields["Quality (%)"].Text=Number(exportProfile.Quality*100);preserveMetadata.IsChecked=exportProfile.PreserveMetadata;
            if(fields.TryGetValue("Audio bitrate (kbps)",out var bitrate))bitrate.Text=Number(exportProfile.AudioBitrate);if(fields.TryGetValue("Maximum video height (0 = original)",out var height))height.Text=Number(exportProfile.MaxVideoHeight);
            if(fields.TryGetValue("GIF frames per second",out var fps))fps.Text=Number(exportProfile.GifFrameRate);if(fields.TryGetValue("GIF width",out var width))width.Text=Number(exportProfile.GifWidth);L10n.Select(sampleDepth,exportProfile.SampleDepth);
            if(fields.TryGetValue("DPI",out var dpi))dpi.Text=Number(exportProfile.Dpi);L10n.Select(documentLayout,exportProfile.Layout);L10n.Select(epubLayout,exportProfile.EpubLayout);
        }
        format.SelectionChanged+=(_,_)=>Apply();if(requestedFormat is not null&&L10n.Values(format).Contains(requestedFormat))L10n.Select(format,requestedFormat);Apply();
    }
    internal bool PreserveImageMetadata=>preserveMetadata.IsChecked==true;
}
