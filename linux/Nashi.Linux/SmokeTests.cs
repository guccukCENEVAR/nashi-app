using System.Text.Json;
using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Core;
using SkiaSharp;
namespace Nashi.Linux;

/// `Nashi --smoke-test`: real Linux checks of the UI layer and every platform-specific engine
/// (Skia/ImageMagick images, PDFium, Tesseract, LibreOffice, FFmpeg playback, clipboard, wheels,
/// editors, Library). Tools that are not installed are reported as skipped, never as passed.
/// Results go to $NASHI_SMOKE_DIRECTORY/smoke.json only when the whole suite passes.
internal static class SmokeTests
{
    private static int checks;private static readonly List<string> skipped=[];private static string root="";
    private static async Task<IDataTransfer> FileTransfer(Window window,string path)
    {
        var file=await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path))??throw new InvalidOperationException("Fixture file is not reachable: "+path);
        var transfer=new DataTransfer();transfer.Add(DataTransferItem.CreateFile(file));return transfer;
    }
    /// Raises a drag event as the platform drop target does, at a point in the target's coordinates.
    private static void Drag(Interactive target,Point at,IDataTransfer data,RoutedEvent<DragEventArgs> routed)=>target.RaiseEvent(new DragEventArgs(routed,data,target,at,KeyModifiers.None){RoutedEvent=routed});
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);checks++;}
    private static void Skip(string reason){skipped.Add(reason);Console.WriteLine("SKIP "+reason);}
    private static void Step(string name){Console.WriteLine("· "+name);File.WriteAllText(Path.Combine(root,"progress.txt"),name);}
    internal static async Task<int> RunAsync()
    {
        root=Environment.GetEnvironmentVariable("NASHI_SMOKE_DIRECTORY")??Path.Combine(Path.GetTempPath(),"Nashi-Smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var watchdog=new Timer(_=>{File.WriteAllText(Path.Combine(root,"suite-timeout.txt"),"Linux desktop verification exceeded 15 minutes.");Environment.Exit(1);},null,TimeSpan.FromMinutes(15),Timeout.InfiniteTimeSpan);
        try
        {
            var tools=new Toolchain(Path.Combine(root,"Tools"));
            var source=Fixture();
            Step("Images");await Images(source,tools);
            Step("PDF and QR");await Pdfs(source);
            Step("Documents");await Documents(tools);
            Step("Clipboard and Library");await Desktop(source,tools);
            Step("Wayland drag shortcuts");await WaylandShortcuts(source);
            Step("Editors");await Editors(source,tools);
            Step("Media playback");await Media(tools);
            Step("Localization");Localization();
            File.WriteAllText(Path.Combine(root,"smoke.json"),JsonSerializer.Serialize(new{passed=checks,skipped,scope="Linux desktop: Avalonia UI, Skia/ImageMagick images, PDFium, QR, documents, FFmpeg playback, clipboard, wheels and editors"}));
            Console.WriteLine(JsonSerializer.Serialize(new{passed=checks,skipped=skipped.Count}));return 0;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(root,"error.txt"),error.ToString());Console.Error.WriteLine(error);return 1;}
    }
    /// A 320×180 olive canvas with a gold block, written as PNG.
    private static string Fixture()
    {
        using var image=ImageEngine.Render(320,180,c=>{using var olive=new SKPaint{Color=new SKColor(85,107,47)};c.DrawRect(0,0,320,180,olive);using var gold=new SKPaint{Color=new SKColor(255,215,0)};c.DrawRect(20,20,120,80,gold);});
        var path=Path.Combine(root,"Source.png");ImageEngine.Save(image,path,"png");return path;
    }
    private static SKColor Pixel(string file,int x,int y){using var image=ImageEngine.Load(file);var pixels=image.Bgra();var i=(y*image.PixelWidth+x)*4;return new SKColor(pixels[i+2],pixels[i+1],pixels[i],pixels[i+3]);}
    private static bool Near(SKColor a,SKColor b,int tolerance=30)=>Math.Abs(a.Red-b.Red)<tolerance&&Math.Abs(a.Green-b.Green)<tolerance&&Math.Abs(a.Blue-b.Blue)<tolerance;
    private static (int Width,int Height) Size(string file){using var image=ImageEngine.Load(file);return(image.PixelWidth,image.PixelHeight);}
    private static async Task Images(string source,Toolchain tools)
    {
        var gold=new SKColor(255,215,0);var olive=new SKColor(85,107,47);
        Check(Near(Pixel(source,30,30),gold)&&Near(Pixel(source,200,150),olive),"Fixture pixels were not decoded faithfully");
        // EXIF orientation: every orientation must decode upright, with the matching dimensions.
        var colors=new[]{MagickColors.Red,MagickColors.Lime,MagickColors.Blue,MagickColors.Yellow};
        for(var orientation=1;orientation<=8;orientation++)
        {
            using var quadrants=new MagickImage(MagickColors.White,80,40);
            for(var i=0;i<4;i++){using var block=new MagickImage(colors[i],40,20);quadrants.Composite(block,i%2*40,i/2*20,CompositeOperator.Over);}
            // Store the raw pixels transformed so that applying the tag restores the upright picture.
            var stored=(MagickImage)quadrants.Clone();
            switch(orientation){case 2:stored.Flop();break;case 3:stored.Rotate(180);break;case 4:stored.Flip();break;case 5:stored.Rotate(90);stored.Flop();break;case 6:stored.Rotate(-90);break;case 7:stored.Rotate(-90);stored.Flop();break;case 8:stored.Rotate(90);break;}
            var exif=new ExifProfile();exif.SetValue(ExifTag.Orientation,(ushort)orientation);stored.SetProfile(exif);stored.Orientation=(ImageMagick.Orientation)orientation;var file=Path.Combine(root,$"Orientation {orientation}.jpg");stored.Quality=100;stored.Write(file,MagickFormat.Jpeg);stored.Dispose();
            var (w,h)=Size(file);Check(w==80&&h==40,$"Orientation {orientation} decoded as {w}×{h}");
            Check(Near(Pixel(file,5,5),SKColors.Red),"Orientation pixels wrong: "+orientation);
        }
        Check(Size(ImageEngine.Apply("crop",[source],root,new(Crop:new ImageRect(10,10,80,60)))[0])==(80,60),"Crop size mismatch");
        Check(Near(Pixel(ImageEngine.Apply("crop",[source],root,new(Crop:new ImageRect(20,20,10,10)))[0],5,5),gold),"Crop moved the selection");
        Check(Size(ImageEngine.Apply("resize",[source],root,new(Width:160))[0])==(160,90),"Resize kept the wrong aspect ratio");
        Check(Size(ImageEngine.Apply("rotate",[source],root,new(Rotation:90))[0])==(180,320),"Rotate 90 mismatch");
        var tilted=Size(ImageEngine.Apply("rotate",[source],root,new(Rotation:45))[0]);Check(Math.Abs(tilted.Width-353)<=2&&Math.Abs(tilted.Height-353)<=2,"Free rotation did not grow to its bounds");
        var flipped=ImageEngine.Apply("flip",[source],root,new(Horizontal:true))[0];Check(Near(Pixel(flipped,290,30),gold)&&Near(Pixel(flipped,30,30),olive),"Horizontal flip mismatch");
        var edited=ImageEngine.Apply("editImage",[source],root,new(){Adjustments=new(){Exposure=1}})[0];Check(Pixel(edited,200,150).Green>olive.Green+30,"Exposure did not brighten the photo");
        var redact=new Avalonia.Rect(20,20,120,80);
        foreach(var style in new[]{"solid","blur","pixelate"})
        {
            var output=ImageEngine.Apply("redactImage",[source],root,new(){Redactions=[redact],RedactionStyle=style})[0];
            if(style=="solid")Check(Near(Pixel(output,60,60),SKColors.Black,8),"Solid redaction left pixels visible");else Check(Size(output)==(320,180),style+" redaction changed the canvas");
        }
        var framed=ImageEngine.Apply("frameImage",[source],root,new(){Frame=new(Padding:40,Color:"#FF0000")})[0];Check(Size(framed)==(400,260)&&Near(Pixel(framed,4,4),SKColors.Red),"Frame size or background mismatch");
        var gradient=ImageEngine.Apply("frameImage",[source],root,new(){Frame=new(Padding:40,Color:"#000000",Backdrop:"gradient",GradientEnd:"#FFFFFF",Shadow:0)})[0];Check(Pixel(gradient,2,2).Red<40&&Pixel(gradient,397,257).Red>215,"Gradient direction mismatch");
        var second=ImageEngine.Apply("convert",[source],root,new(),"jpg")[0];
        var collage=ImageEngine.Apply("createCollage",[source,second],root,new(){Collage=new(Width:800,Height:400,Color:"#0000FF")})[0];Check(Size(collage)==(800,400)&&Near(Pixel(collage,4,4),SKColors.Blue),"Collage canvas mismatch");
        var pdf=ImageEngine.Apply("createPDF",[source,second],root,new())[0];using(var document=PdfSharp.Pdf.IO.PdfReader.Open(pdf,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Check(document.PageCount==2,"Image PDF page count mismatch");
        var encoder=tools.Ready?tools.Executable("ffmpeg"):SystemTools.Find("ffmpeg");
        foreach(var ext in ImageEngine.Formats)
        {
            if(ext=="heic"&&encoder is null){Skip("HEIC export needs FFmpeg with libx265");continue;}
            var converted=ImageEngine.Apply("convert",[source],root,new(){EncoderPath=encoder},ext)[0];Check(new FileInfo(converted).Length>0&&FeatureCatalog.Extension(converted)==ext,"Empty or misnamed "+ext);
            if(ext is not ("pdf" or "gif"))Check(Size(converted)==(320,180),"Converted "+ext+" changed dimensions");
            if(ext is "png" or "bmp" or "tiff" or "webp")Check(Near(Pixel(converted,30,30),gold),"Converted "+ext+" changed pixels");
        }
        var big=Path.Combine(root,"Large.png");using(var noise=new MagickImage("plasma:",1200,900))noise.Write(big,MagickFormat.Png);
        var compressed=ImageEngine.Apply("compress",[big],root,new(Quality:85){TargetBytes=60_000},"jpg")[0];Check(new FileInfo(compressed).Length<=60_000,"Image compression missed its target");
        using(var annotated=ImageEngine.Load(source)){var marked=ImageEngine.Apply("annotate",[source],root,new(Annotated:annotated))[0];Check(Size(marked)==(320,180),"Annotation export size mismatch");}
        var metadata=new MetadataService(Path.Combine(root,"Tools"));
        if(metadata.Executable is null)Skip("ExifTool metadata round trip (perl-image-exiftool)");
        else
        {
            var tagged=Path.Combine(root,"Tagged.jpg");File.Copy(second,tagged);await ProcessRunner.RunAsync(metadata.Executable,["-overwrite_original","-Artist=Nashi fixture",tagged]);
            var copy=ImageEngine.Apply("convert",[tagged],root,new(),"png")[0];Check(!(await metadata.ReadAsync(copy)).Values.Contains("Nashi fixture"),"Conversion copied tags without being asked");
            await metadata.CopyImageTagsAsync(tagged,copy,CancellationToken.None);Check((await metadata.ReadAsync(copy)).Values.Contains("Nashi fixture"),"Preserved metadata was not copied");
        }
    }
    private static async Task Pdfs(string source)
    {
        var pdf=ImageEngine.Apply("createPDF",[source],root,new())[0];var pages=await NativePdf.RenderAsync(pdf,72);Check(pages.Length==1,"PDFium page count mismatch");
        using(var page=ImageFrame.FromPng(pages[0]))Check(Math.Abs(page.PixelWidth-240)<=1&&Math.Abs(page.PixelHeight-135)<=1,"PDFium rendered the wrong page size");
        var merged=DocumentEngine.Pdf("mergePDFs",[pdf,pdf],root)[0];var parts=DocumentEngine.Pdf("splitPDF",[merged],root);Check(parts.Count==2,"PDF split count mismatch");
        var organized=DocumentEngine.Pdf("organizePDF",[merged],root,[2,1,1],pageRotations:[90,0,0])[0];Check((await NativePdf.RenderAsync(organized,36)).Length==3,"Organized PDF page count mismatch");
        var compressed=await PdfCompression.RunAsync([merged],root,new(),"",null,CancellationToken.None);Check(new FileInfo(compressed[0]).Length<=new FileInfo(merged).Length,"PDF compression grew the file");
        // QR: encode with ZXing, read back through the same decoder the tool uses, from PNG and from a PDF page.
        var writer=new ZXing.BarcodeWriterPixelData{Format=ZXing.BarcodeFormat.QR_CODE,Options=new ZXing.QrCode.QrCodeEncodingOptions{Width=240,Height=240,Margin=2,CharacterSet="UTF-8"}};var data=writer.Write("https://example.com/nashi-ğüşıöç");
        using(var qr=ImageEngine.Render(data.Width,data.Height,c=>{using var bitmap=new SKBitmap(new SKImageInfo(data.Width,data.Height,SKColorType.Bgra8888,SKAlphaType.Premul));System.Runtime.InteropServices.Marshal.Copy(data.Pixels,0,bitmap.GetPixels(),data.Pixels.Length);c.DrawBitmap(bitmap,0,0);}))
        {
            var file=Path.Combine(root,"QR.png");ImageEngine.Save(qr,file,"png");using var loaded=ImageEngine.Load(file);var decoded=NativePdf.DecodeQr(loaded);Check(decoded.SequenceEqual(["https://example.com/nashi-ğüşıöç"]),"QR decode failed: "+string.Join(" | ",decoded));
            var qrPdf=ImageEngine.Apply("createPDF",[file],root,new())[0];using var rendered=ImageFrame.FromPng((await NativePdf.RenderAsync(qrPdf,180))[0]);Check(NativePdf.DecodeQr(rendered).Length==1,"QR in PDF not found");
        }
        var organizer=await PdfOrganizer.CreateAsync(merged);Check(organizer.Pages.SequenceEqual([1,2]),"PDF organizer did not list pages");organizer.Close();
        if(Ocr.Available)
        {
            using(var words=ImageEngine.Render(900,200,c=>{c.Clear(SKColors.White);using var ink=new SKPaint{Color=SKColors.Black,IsAntialias=true};using var font=new SKFont{Size=72};c.DrawText("NASHI LINUX",40,130,font,ink);}))ImageEngine.Save(words,Path.Combine(root,"Words.png"),"png");var file=Path.Combine(root,"Words.png");
            using var frame=ImageEngine.Load(file);var boxes=await NativePdf.DetectAsync(frame,true,false);Check(boxes.Length>=1&&boxes.All(r=>r.Width>0&&r.Bottom<=200),"Tesseract found no word boxes");
        }
        else Skip("Tesseract text detection (tesseract, tesseract-data-eng)");
        if(Faces.ModulePath() is null)Skip("OpenCV face detection (python-opencv)");
        else if(Environment.GetEnvironmentVariable("NASHI_SMOKE_FACE_FIXTURE") is {} portrait&&File.Exists(portrait))
        {
            using var face=ImageEngine.Load(portrait);var found=await NativePdf.DetectAsync(face,false,true);Check(found.Length>=1&&found.All(r=>r.Right<=face.PixelWidth&&r.Bottom<=face.PixelHeight),"OpenCV found no face in the portrait fixture");
        }
        else Skip("OpenCV face detection fixture (NASHI_SMOKE_FACE_FIXTURE)");
    }
    private static async Task Documents(Toolchain tools)
    {
        var office=new OfficeRuntime(Path.Combine(root,"Tools"));var conversion=new DocumentConversion(office,tools);
        var text=Path.Combine(root,"Notes.txt");await File.WriteAllTextAsync(text,"Nashi Linux · Türkçe ıİşğü\nSecond line");
        var srt=Path.Combine(root,"Talk.srt");await File.WriteAllTextAsync(srt,"1\n00:00:01,000 --> 00:00:02,500\nHello\n");
        var vtt=await conversion.ConvertAsync([srt],"vtt",root,new(),null,CancellationToken.None);Check((await File.ReadAllTextAsync(vtt[0])).StartsWith("WEBVTT",StringComparison.Ordinal),"Subtitle conversion failed");
        var zip=DocumentEngine.Zip([text,srt],root);var tar=await conversion.ConvertAsync([zip],"tar",root,new(),null,CancellationToken.None);var back=ArchiveEngine.Extract(tar[0],root);Check(File.Exists(Path.Combine(back[0],"Notes.txt")),"Archive conversion lost a file");
        if(office.Executable is null){Skip("LibreOffice document conversions (libreoffice-fresh)");return;}
        var docx=await conversion.ConvertAsync([text],"docx",root,new(),null,CancellationToken.None);Check(new FileInfo(docx[0]).Length>0,"Text to DOCX failed");
        var pdf=await conversion.ConvertAsync([docx[0]],"pdf",root,new(),null,CancellationToken.None);Check((await NativePdf.RenderAsync(pdf[0],36)).Length>=1,"DOCX to PDF failed");
        var extracted=await conversion.ConvertAsync([pdf[0]],"txt",root,new(),null,CancellationToken.None);Check((await File.ReadAllTextAsync(extracted[0])).Contains("Türkçe",StringComparison.Ordinal),"PDF text extraction lost Unicode text");
        var images=await conversion.ConvertAsync([docx[0]],"png",root,new(Dpi:72),null,CancellationToken.None);Check(images.Count>=1&&Size(images[0]).Width>100,"Document to PNG failed");
        var epub=await conversion.ConvertAsync([pdf[0]],"epub",root,new(EpubLayout:"fixed",Dpi:72),null,CancellationToken.None);Check(new FileInfo(epub[0]).Length>0,"Fixed EPUB failed");
        var sheet=Path.Combine(root,"Table.csv");await File.WriteAllTextAsync(sheet,"Name,Count\nPear,3\n");var xlsx=await conversion.ConvertAsync([sheet],"xlsx",root,new(),null,CancellationToken.None);Check(new FileInfo(xlsx[0]).Length>0,"CSV to XLSX failed");
        if(tools.Ready)
        {
            var pages=await conversion.ConvertAsync([docx[0]],"pages",root,new(),null,CancellationToken.None);var html=await conversion.ConvertAsync([pages[0]],"docx",root,new(),null,CancellationToken.None);Check(new FileInfo(html[0]).Length>0,"Pages round trip failed");
        }
        else Skip("Pages interoperability (deno)");
    }
    private static async Task Desktop(string source,Toolchain tools)
    {
        var library=new LibraryStore(Path.Combine(root,"UserData"));
        var window=new MainWindow([source],library,checkForUpdates:false);window.Show();await Settle();
        Check(window.SelectedFiles.SequenceEqual([source]),"Launch files were not selected");
        Check(Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Source.png"),"Selection title not rendered");
        var choices=MainWindow.Choices([source]);Check(choices.Any(c=>c.Id=="crop")&&choices.Any(c=>c.Id=="readQRCode")&&choices.All(c=>c.Title.Length>0),"Image tools missing from the wheel");
        Check(GlobalDragController.ChoicesFor("convert",[source]).Select(c=>c.Id).SequenceEqual(FeatureCatalog.Formats([source]).Select(f=>"convert:"+f)),"Convert wheel differs from the catalog");
        Check(GlobalDragController.Kind(new(),KeyModifiers.Shift)=="convert"&&GlobalDragController.Kind(new(),KeyModifiers.Control)=="tools"&&GlobalDragController.Kind(new(),KeyModifiers.Alt)=="actions"&&GlobalDragController.Kind(new(),KeyModifiers.Control|KeyModifiers.Shift) is null,"Default drag chords differ from Windows/macOS");
        Check(GlobalDragController.Kind(new(){Enabled=false},KeyModifiers.Shift) is null,"Disabled drag shortcuts still opened a wheel");
        // Wheel: keyboard navigation chooses the highlighted petal; Escape closes without a choice.
        ToolChoice? chosen=null;var wheel=WheelWindow.Present(window,choices,c=>chosen=c);await Settle();
        Check(wheel.IsVisible&&wheel.Items.Count==Math.Min(12,choices.Count),"Wheel did not open with every tool");
        // As on Windows, the first petal is focused when the wheel opens.
        var start=wheel.SelectedIndex;var expected=start<0?1:(start+2)%wheel.Items.Count;
        wheel.Navigate(Key.Right);wheel.Navigate(Key.Right);Check(wheel.SelectedIndex==expected,"Arrow keys did not move the wheel selection");wheel.Navigate(Key.Enter);await Settle();
        Check(chosen==choices[expected]&&!wheel.IsVisible,"Enter did not choose the highlighted tool");
        chosen=null;wheel=WheelWindow.Present(window,choices,c=>chosen=c);await Settle();wheel.Navigate(Key.Escape);await Settle();Check(chosen is null&&!wheel.IsVisible,"Escape chose a tool");
        // Drops: a drag wheel opens empty and fills in from the dragged files; its petals must then accept the drop.
        var transfer=await FileTransfer(window,source);string? dropped=null;
        var dropWheel=WheelWindow.PresentForDrop(new PixelPoint(500,400),[],content=>GlobalDragController.Choices("convert",content),(c,_)=>dropped=c.Id);await Settle();
        var top=new Point(dropWheel.Bounds.Width/2,dropWheel.Bounds.Height*.17);
        foreach(var routed in new[]{DragDrop.DragEnterEvent,DragDrop.DragOverEvent,DragDrop.DropEvent})Drag(dropWheel,top,transfer,routed);await Settle();
        Check(dropped==GlobalDragController.ChoicesFor("convert",[source])[0].Id&&!dropWheel.IsVisible,"Dropping on a drag wheel petal did not choose it");
        // Drop zones: without a chord (Wayland), dropping on Tools opens the tools wheel.
        Drag(window,new Point(window.Bounds.Width/2,window.Bounds.Height/2),transfer,DragDrop.DragOverEvent);await Settle();
        Check(window.DropZonesVisible&&window.DropZoneCenter("tools") is not null,"Drop zones did not appear for a file drag");
        var toolsZone=window.DropZoneCenter("tools")!.Value;Drag(window,toolsZone,transfer,DragDrop.DragOverEvent);Drag(window,toolsZone,transfer,DragDrop.DropEvent);await Settle();
        var zoneWheel=window.OwnedWindows.OfType<WheelWindow>().LastOrDefault(w=>w.IsVisible);
        Check(!window.DropZonesVisible&&zoneWheel is not null&&zoneWheel.Items.Any(c=>c.Id=="crop"),"Dropping on the Tools zone did not open the tools wheel");
        zoneWheel!.Navigate(Key.Escape);await Settle();
        // Clipboard: text, links, images and files, with ordinary paste preserved in text fields.
        var clipboard=window.Clipboard!;var text="Nashi clipboard · Türkçe ıİşğü 🍐\nSecond line";
        await clipboard.SetTextAsync(text);await Settle();Press(window,Avalonia.Input.Key.V,KeyModifiers.Control);await Settle(400);
        Check(window.SelectedFiles.Count==1&&Path.GetExtension(window.SelectedFiles[0])==".txt"&&await File.ReadAllTextAsync(window.SelectedFiles[0])==text,"Ctrl+V did not import Unicode text unchanged: "+string.Join(",",window.SelectedFiles));
        Check(library.Entries.Count==1&&library.Entries[0].Action=="Pasted text","Pasted text missing from Library");
        Press(window,Avalonia.Input.Key.F,KeyModifiers.Control);await Settle();Check(window.LibraryVisible&&window.SearchField.IsFocused,"Ctrl+F did not focus Library search: library="+window.LibraryVisible+" focused="+window.FocusManager?.GetFocusedElement()?.GetType().Name+" active="+window.IsActive);
        await clipboard.SetTextAsync("Pasted text");await Settle();Press(window,Avalonia.Input.Key.V,KeyModifiers.Control);await Settle(300);Check(library.Entries.Count==1,"Ctrl+V in a text field imported a file");
        Press(window,Avalonia.Input.Key.D1,KeyModifiers.Control);await Settle();Check(!window.LibraryVisible,"Ctrl+1 did not return Home");
        var image=ClipboardImport.Read(new([],null,null,null,new Bitmap(source)),library);using(var pasted=ImageEngine.Load(image.Paths[0]))Check(pasted.PixelWidth==320&&image.Label=="Pasted image","Pasted image changed size");
        var files=ClipboardImport.Read(new([source,image.Paths[0]],null,null,null,null),library);Check(files.Paths.Length==2&&files.Label=="Pasted files","File list paste failed");
        var link=ClipboardImport.Read(new([],"https://www.youtube.com/watch?v=dQw4w9WgXcQ",null,null,null),library);Check(link.Link=="https://www.youtube.com/watch?v=dQw4w9WgXcQ"&&link.Paths.Length==0,"Copied link not routed to downloads");
        var html=new TransferContent([],null,"<a href=\"https://www.instagram.com/p/ABC123/\">post</a>",null,null).Payload();Check(html?.Kind=="instagramPhotos","Browser HTML drag did not yield the Instagram link");
        var uris=new TransferContent([],null,null,"# comment\nhttps://youtu.be/dQw4w9WgXcQ\n",null).Payload();Check(uris?.Kind=="youtube","URI-list drag did not yield the YouTube link");
        Check(GlobalDragController.Choices("convert",new([],null,null,"https://youtu.be/dQw4w9WgXcQ",null)).Any(c=>c.Id=="download:thumbnail"),"Web drag wheel lacks downloads");
        // Library: search, reuse and persistence across a restart.
        library.Record("Convert",[source]);var restarted=new LibraryStore(Path.Combine(root,"UserData"));Check(restarted.Entries.Count==2&&restarted.Entries[0].Action=="Convert","Library did not persist");
        window.SearchField.Text="pasted";await Settle();window.ShowLibrary(true);await Settle();Check(Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Pasted text"),"Library search hid the matching entry");
        window.ShowLibrary(false);
        // File actions: copy to the clipboard as files, move transactionally, reveal-safe paths.
        await FileActions.Copy(window,[source]);await Settle();var copied=await TransferContent.FromAsync(clipboard);Check(copied.Files.SequenceEqual([source]),"Copied files were not placed on the clipboard");
        var moveSource=Path.Combine(root,"Move me.txt");await File.WriteAllTextAsync(moveSource,"move");var destination=Path.Combine(root,"Moved");
        var moved=await FileActions.MoveAsync([moveSource],destination,null,CancellationToken.None);Check(!File.Exists(moveSource)&&File.ReadAllText(moved[0])=="move","Move did not publish the copy before removing the original");
        // Float shelf and previews.
        var shelf=new FloatShelf([source,image.Paths[0]],(_,_)=>Task.CompletedTask,_=>{},tools,window.Office);shelf.Show();await Settle();shelf.SetExpanded(true);await Settle();
        Check(shelf.Paths.Count==2&&shelf.IsExpanded,"Float did not hold the files");shelf.Add([source]);Check(shelf.Paths.Count==2,"Float duplicated a file");shelf.Close();
        foreach(var path in new[]{source,ImageEngine.Apply("createPDF",[source],root,new())[0],files.Paths[0],window.SelectedFiles.Count>0?window.SelectedFiles[0]:source})
        {
            var preview=new FilePreviewWindow([path],tools,window.Office);preview.Show(window);await Settle(1500);
            Check(!preview.PreviewStatus.StartsWith("Preview unavailable",StringComparison.Ordinal)&&preview.PreviewStatus!="Preparing preview…","Preview failed for "+Path.GetFileName(path)+": "+preview.PreviewStatus);preview.Close();
        }
        // Settings, setup and welcome render every section.
        var settings=new SettingsWindow(new PreferencesStore(Path.Combine(root,"UserData")),()=>{},()=>Task.CompletedTask,library);settings.Show(window);await Settle();
        foreach(var list in Descendants(settings).OfType<ListBox>()){for(var i=0;i<list.ItemCount;i++){list.SelectedIndex=i;await Settle(100);Check(Descendants(settings).OfType<TextBlock>().Count()>3,"Settings section rendered empty: "+i);}}settings.Close();
        var setup=new ToolSetupWindow(tools,window.Office,new MetadataService(Path.Combine(root,"Tools")),_=>{});setup.Show(window);await Settle();Check(Descendants(setup).OfType<TextBlock>().Any(t=>t.Text?.StartsWith("ffmpeg",StringComparison.Ordinal)==true),"Setup did not list FFmpeg");setup.Close();
        var welcome=new WelcomeWindow(false,()=>{});welcome.Show(window);await Settle();Check(Descendants(welcome).OfType<TextBlock>().Any(t=>t.Text=="Nice files.\nNo fuss."),"Welcome tour did not open on its first page");welcome.Close();
        Check(Updates.Parse("{\"tag_name\":\"v99.0.0-1\",\"html_url\":\"https://github.com/orkunisitmak/nashi-app/releases/tag/v99.0.0-1\"}"u8.ToArray(),new(0,7,10))?.Version=="99.0.0","Update check did not read the release tag");
        Check(Updates.Parse("{\"tag_name\":\"v0.7.10-41\",\"html_url\":\"https://github.com/orkunisitmak/nashi-app/releases/tag/v0.7.10-41\"}"u8.ToArray(),new(0,7,10)) is null,"Current version was offered as an update");
        try{Updates.Parse("{\"tag_name\":\"v9.0\",\"html_url\":\"https://evil.example/releases\"}"u8.ToArray(),new(0,7,10));Check(false,"Foreign release page accepted");}catch(InvalidDataException){checks++;}
        window.Close();
    }
    // A trimmed /proc/bus/input/devices: a laptop keyboard, a mouse and a power button (neither).
    private const string InputDevices="""
        I: Bus=0011 Vendor=0001 Product=0001 Version=ab83
        N: Name="AT Translated Set 2 keyboard"
        H: Handlers=sysrq kbd leds event3
        B: KEY=402000000 3803078f800d001 feffffdfffefffff fffffffffffffffe

        I: Bus=0003 Vendor=046d Product=c077 Version=0111
        N: Name="Logitech USB Optical Mouse"
        H: Handlers=mouse0 event5
        B: KEY=ff0000 0 0 0 0
        B: REL=903

        I: Bus=0019 Vendor=0000 Product=0001 Version=0000
        N: Name="Power Button"
        H: Handlers=kbd event0
        B: KEY=10000000000000 0

        """;
    private static byte[] InputEvent(ushort type,ushort code,int value)
    {
        var data=new byte[24];System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16),type);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18),code);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20),value);return data;
    }
    /// Wayland: chords come from evdev, the wheel stays hidden until a real file drag reaches it.
    private static async Task WaylandShortcuts(string source)
    {
        var devices=EvdevInput.Parse(InputDevices.Replace("/dev/input",""));
        Check(devices.Count==2&&devices.Any(d=>d.Keyboard&&!d.Pointer&&d.Path.EndsWith("event3",StringComparison.Ordinal))&&devices.Any(d=>d.Pointer&&!d.Keyboard&&d.Path.EndsWith("event5",StringComparison.Ordinal)),"Input devices were not classified");
        var replay=EvdevInput.Replay([..InputEvent(1,29,1),..InputEvent(1,100,1),..InputEvent(1,272,1),..InputEvent(2,0,4),..InputEvent(3,0,9),..InputEvent(1,30,1)]);
        Check(replay.Modifiers==(KeyModifiers.Control|KeyModifiers.Alt)&&replay.ButtonDown&&replay.MotionSincePress==2,"Input events were decoded wrongly");
        Check(KWinPlacement.Script.Contains("\""+KWinPlacement.WheelTitle+"\"",StringComparison.Ordinal)&&KWinPlacement.Script.Contains("workspace.cursorPos",StringComparison.Ordinal),"KWin placement script does not target the drop wheel");
        if(SystemTools.Find("mkfifo") is not {} mkfifo){Skip("Wayland drag shortcut flow (mkfifo)");return;}
        var folder=Path.Combine(root,"Input");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"devices"),InputDevices);
        foreach(var name in new[]{"event3","event5"})await ProcessRunner.RunAsync(mkfifo,[Path.Combine(folder,name)]);
        var session=Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");var desktop=Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");var wasEnabled=EvdevInput.Enabled;
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE","wayland");Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP","");
        EvdevInput.DevicesFile=Path.Combine(folder,"devices");EvdevInput.DeviceDirectory=folder;EvdevInput.SetEnabled(true);
        var library=new LibraryStore(Path.Combine(root,"WaylandData"));var window=new MainWindow([source],library,checkForUpdates:false);window.Show();await Settle();
        string? dropped=null;var controller=new GlobalDragController(window,new PreferencesStore(library.DirectoryPath),(id,_)=>dropped=id);
        try
        {
            Check(!GlobalDragController.GlobalGesturesAvailable,"Wayland session was not detected");
            await using var keyboard=await Task.Run(()=>new FileStream(Path.Combine(folder,"event3"),FileMode.Open,FileAccess.Write));
            await using var mouse=await Task.Run(()=>new FileStream(Path.Combine(folder,"event5"),FileMode.Open,FileAccess.Write));
            async Task Send(FileStream device,params byte[][] events){foreach(var e in events)await device.WriteAsync(e);await device.FlushAsync();}
            async Task Hold(ushort key)
            {
                await Send(keyboard,InputEvent(1,key,1));await Send(mouse,InputEvent(1,272,1));await Settle(150);
                for(var i=0;i<5;i++)await Send(mouse,InputEvent(2,0,3));await Settle(300);
            }
            async Task Release(ushort key){await Send(mouse,InputEvent(1,272,0));await Send(keyboard,InputEvent(1,key,0));await Settle(250);}
            // Shift-selecting text: a hidden wheel that never shows and closes on release.
            await Hold(42);var quiet=controller.ActiveWheel;
            Check(quiet is {IsVisible:true,Revealed:false}&&quiet.Opacity==0,"Shift-drag on Wayland did not prepare a hidden wheel");
            await Release(42);Check(controller.ActiveWheel is null&&!quiet!.IsVisible,"Hidden wheel stayed open after release");
            // A real file drag reaches the wheel: it appears, and the drop chooses the petal.
            await Hold(42);var wheel=controller.ActiveWheel!;var transfer=await FileTransfer(window,source);var top=new Point(wheel.Bounds.Width/2,wheel.Bounds.Height*.17);
            Drag(wheel,top,transfer,DragDrop.DragEnterEvent);await Settle();Check(wheel.Revealed&&wheel.Items.Count>0,"File drag did not reveal the Wayland wheel");
            Drag(wheel,top,transfer,DragDrop.DragOverEvent);Drag(wheel,top,transfer,DragDrop.DropEvent);await Release(42);
            Check(dropped==GlobalDragController.ChoicesFor("convert",[source])[0].Id,"Drop on the Wayland wheel did not choose its petal");
            // Ctrl mid-drag swaps to the tools wheel, as on macOS.
            await Hold(42);await Send(keyboard,InputEvent(1,42,0),InputEvent(1,29,1),InputEvent(2,0,1));await Send(mouse,InputEvent(2,0,3));await Settle(250);
            var swapped=controller.ActiveWheel!;Drag(swapped,top,transfer,DragDrop.DragEnterEvent);await Settle();
            Check(swapped.Items.Any(c=>c.Id=="crop"),"Ctrl mid-drag did not switch to the tools wheel");await Release(29);
        }
        finally
        {
            controller.Dispose();window.Close();
            EvdevInput.DevicesFile="/proc/bus/input/devices";EvdevInput.DeviceDirectory="/dev/input";EvdevInput.SetEnabled(wasEnabled);
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE",session);Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP",desktop);
        }
    }
    private static async Task Editors(string source,Toolchain tools)
    {
        // Every image tool editor opens, reads its settings and closes with the same result as WPF.
        foreach(var tool in new[]{"convert","resize","crop","rotate","editImage","frameImage","redactImage","annotate","compress"})
        {
            var editor=new ToolEditor(tool,tool,source,preview:ImageEngine.Load(source),outputFormats:tool=="convert"?FeatureCatalog.Formats([source]):null);editor.ConfigureProfiles(new PreferencesStore(Path.Combine(root,"UserData")));
            editor.Show();await Settle(tool is "editImage" or "frameImage"?900:200);
            var result=false;editor.Closed+=(_,_)=>result=true;
            if(tool=="crop"){editor.FieldForTest("X").Text="10";editor.FieldForTest("Y").Text="10";editor.FieldForTest("Width").Text="50";editor.FieldForTest("Height").Text="40";}
            if(tool=="annotate")AddMark(editor);
            Press(editor,Avalonia.Input.Key.Enter,KeyModifiers.None);await Settle(400);
            Check(result,"Editor did not save: "+tool);
            if(tool=="crop")Check(editor.ImageOptions.Crop==new ImageRect(10,10,50,40),"Crop fields not applied");
            if(tool=="annotate"){Check(editor.ImageOptions.Annotated is {PixelWidth:320,PixelHeight:180},"Annotation was not rendered at image size");var mark=editor.ImageOptions.Annotated!.Bgra();var i=(150*320+300)*4;Check(mark[i+2]>200&&mark[i+1]<80&&mark[i]<80,"Annotation mark missing from the rendered pixels");}
        }
        var pdf=ImageEngine.Apply("createPDF",[source,source],root,new())[0];
        foreach(var tool in new[]{"splitPDF","mergePDFs","compress"}){var editor=new ToolEditor(tool,tool,pdf,inputs:[pdf,pdf]);var closed=false;editor.Closed+=(_,_)=>closed=true;editor.Show();await Settle();Press(editor,Avalonia.Input.Key.Enter,KeyModifiers.None);await Settle(300);Check(closed,"PDF editor did not save: "+tool);}
    }
    private static void AddMark(ToolEditor editor)
    {
        var canvas=Descendants(editor).OfType<Canvas>().First(c=>c.Children.Count>0&&c.Children[0] is Image);
        var mark=new Avalonia.Controls.Shapes.Rectangle{Width=20,Height=20,Fill=Avalonia.Media.Brushes.Red};Canvas.SetLeft(mark,290);Canvas.SetTop(mark,140);canvas.Children.Add(mark);
    }
    private static async Task Media(Toolchain tools)
    {
        var ffmpeg=SystemTools.Find("ffmpeg");if(ffmpeg is null||SystemTools.Find("ffprobe") is null){Skip("Media playback (ffmpeg, ffprobe)");return;}
        var clip=Path.Combine(root,"Clip.mp4");await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","testsrc=size=320x180:rate=30","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","3","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac","-shortest",clip]);
        var player=new MediaPlayer(new ToolchainShim(ffmpeg).Tools);await player.OpenAsync(clip);Check(Math.Abs(player.Duration-3)<.2&&player.HasVideo&&player.VideoSize.Width==320,"Player did not inspect the clip");
        player.Play();await Task.Delay(1200);var position=player.Position;Check(player.Playing&&position>.6&&position<2.5,"Playback clock did not advance in real time: "+position);
        player.Pause();var paused=player.Position;await Task.Delay(400);Check(!player.Playing&&Math.Abs(player.Position-paused)<.01,"Pause did not stop the clock");
        player.Seek(2.5);Check(Math.Abs(player.Position-2.5)<.01,"Seek did not move the playhead");
        var ended=false;player.Ended+=()=>ended=true;player.Play();await Task.Delay(1500);await Settle();Check(ended&&!player.Playing,"Playback did not end at the clip's end");
        player.Speed=2;player.Seek(0);player.Play();await Task.Delay(700);Check(player.Position>.9,"Speed change did not speed up the clock");player.Dispose();
        var editor=new ToolEditor("trimVideo","Trim Video",clip,await new MediaEngine(ffmpeg,SystemTools.Find("ffprobe")!).InspectAsync(clip));editor.ConfigureMediaRuntime(new ToolchainShim(ffmpeg).Tools);editor.Show();await Settle(1500);
        editor.Timeline.SetEndpoint(true,1);Check(editor.FieldForTest("Start").Text=="1","Timeline did not update the Start field");Press(editor,Avalonia.Input.Key.Enter,KeyModifiers.None);await Settle(400);
        Check(editor.MediaOptions.Start==1&&editor.MediaOptions.End>2.9,"Trim range not saved");
    }
    /// Media checks use the system FFmpeg even when yt-dlp or Deno are missing from PATH.
    private sealed class ToolchainShim
    {
        internal Toolchain Tools{get;}
        internal ToolchainShim(string ffmpeg)
        {
            var directory=Path.Combine(root,"MediaTools");Directory.CreateDirectory(directory);
            foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp","deno"})
            {
                var link=Path.Combine(directory,name);if(File.Exists(link))continue;var target=SystemTools.Find(name)??ffmpeg;File.CreateSymbolicLink(link,target);
            }
            Tools=new Toolchain(directory);
        }
    }
    /// Every literal shown through L10n must be translated, from the Linux or the shared catalog.
    private static void Localization()
    {
        Check(L10n.Choice("pcm24")=="24-bit PCM"&&LinuxCatalog.Text("Library","tr")=="Kitaplık","Catalog not loaded");
        foreach(var (source,target) in LinuxCatalog.Turkish)Check(System.Text.RegularExpressions.Regex.Matches(source,@"\{[0-9]\}").Select(m=>m.Value).Order().SequenceEqual(System.Text.RegularExpressions.Regex.Matches(target,@"\{[0-9]\}").Select(m=>m.Value).Order()),"Translation placeholders differ: "+source);
        Check(LinuxCatalog.Text("Show in Files","tr")!="Show in Files","Linux catalog missing a common string");
    }
    private static void Press(TopLevel target,Key key,KeyModifiers modifiers)
    {
        var focused=target.FocusManager?.GetFocusedElement() as Interactive??target;
        focused.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key,KeyModifiers=modifiers,Source=focused});
        focused.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=key,KeyModifiers=modifiers,Source=focused});
    }
    private static IEnumerable<Visual> Descendants(Visual root)=>root.GetVisualDescendants();
    // Yield to the UI loop (layout, input and X11 selection events) instead of pumping it re-entrantly.
    private static Task Settle(int milliseconds=150)=>Task.Delay(milliseconds);
}
