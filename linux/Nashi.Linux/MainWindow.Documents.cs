using Avalonia.Input.Platform;
using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    private readonly OfficeRuntime officeRuntime=new(Path.Combine(DataDirectory,"Tools"));
    private readonly MetadataService metadataService=new(Path.Combine(DataDirectory,"Tools"));
    private async Task EditMetadataAsync(string[] selected)
    {
        if(selected.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)&&FeatureCatalog.Extension(p)!="gif")){await EditMediaMetadataAsync(selected);return;}
        if(metadataService.Executable is null){await Dialogs.Info(this,"Install ExifTool to inspect and edit metadata. On CachyOS and Arch: sudo pacman -S perl-image-exiftool","Set up metadata tools");return;}
        var removeAll=selected.Length>1;Dictionary<string,string>? changes=null;
        if(selected.Length==1)
        {
            var values=await metadataService.ReadAsync(selected[0]);var editor=new MetadataEditor(values);if(!await editor.ShowDialog<bool>(this))return;
            removeAll=editor.RemoveAll;changes=removeAll?null:editor.Updates;
        }
        await RunAsync((destination,p,t)=>metadataService.WriteAsync(selected,destination,removeAll,changes,progress:p,token:t),"Metadata");
    }
    internal static string ToolSymbol(string id)=>id switch{"compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴",_=>"↗"};
    private async Task EditDocumentAsync(ToolChoice choice,string[] selected,string? requestedFormat=null)
    {
        if(choice.Id=="convert")
        {
            var editor=new ToolEditor("convert","Convert",selected[0],inputs:selected,outputFormats:FeatureCatalog.Formats(selected));editor.ConfigureProfiles(preferences,requestedFormat);if(!await editor.ShowDialog<bool>(this))return;
            await RunAsync((destination,p,t)=>new DocumentConversion(officeRuntime,tools).ConvertAsync(selected,editor.Format,destination,editor.DocumentOptions,p,t),"Convert to "+editor.Format.ToUpperInvariant());return;
        }
        if(choice.Id=="organizePDF")
        {
            var organizer=await PdfOrganizer.CreateAsync(selected[0]);if(!await organizer.ShowDialog<bool>(this))return;
            await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf("organizePDF",selected,destination,organizer.Pages,t,organizer.Rotations,pageRotations:organizer.PageRotations,uniformWidth:organizer.UniformWidth),t),"Organize PDF");return;
        }
        var dialog=new ToolEditor(choice.Id,choice.Title,selected[0],inputs:selected);if(!await dialog.ShowDialog<bool>(this))return;
        if(choice.Id=="compress"){await RunAsync((destination,p,t)=>PdfCompression.RunAsync(selected,destination,dialog.ImageOptions,dialog.DocumentOptions.Password,p,t),"Compress PDF");return;}
        await RunAsync((destination,p,t)=>Task.Run(()=>DocumentEngine.Pdf(choice.Id,dialog.OrderedInputs,destination,null,t,pagesPerFile:dialog.PagesPerFile,password:dialog.DocumentOptions.Password,pageRanges:dialog.PageRanges),t),choice.Title);
    }
    private async Task ReadQrAsync(IReadOnlyList<string> inputs)
    {
        SetBusy(true);var results=new List<(string File,string Value)>();try
        {
            foreach(var file in inputs)
            {
                if(FeatureCatalog.Extension(file)=="pdf"){var pages=await NativePdf.RenderAsync(file,180);for(var n=0;n<pages.Length;n++){using var page=ImageFrame.FromPng(pages[n]);foreach(var value in NativePdf.DecodeQr(page))results.Add((Path.GetFileName(file)+$" · Page {n+1}",value));}}
                else{using var image=await Task.Run(()=>ImageEngine.Load(file));foreach(var value in NativePdf.DecodeQr(image))results.Add((Path.GetFileName(file),value));}
            }
        }
        catch(Exception error){SetBusy(false);await Dialogs.Info(this,error.Message,"Couldn’t read QR codes");return;}
        finally{SetBusy(false);}
        var window=new Window{Width=720,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Ui.Icon()};L10n.Title(window,"Read QR Codes · Nashi");
        var stack=new StackPanel{Margin=new(26)};stack.Children.Add(Ui.Heading("Read QR Codes"));
        stack.Children.Add(Ui.Text(results.Count==0?"No QR codes found. Try a larger or sharper image.":$"{results.Count} QR {(results.Count==1?"code":"codes")} found.",14,"Muted"));
        foreach(var result in results)
        {
            var content=new StackPanel();content.Children.Add(Ui.Text(result.File,12,"Muted",localize:false));content.Children.Add(new TextBox{Text=result.Value,IsReadOnly=true,TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxHeight=150});
            var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Copy",async()=>{if(window.Clipboard is {} clipboard)await clipboard.SetTextAsync(result.Value);}));
            if(Uri.TryCreate(result.Value,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https")buttons.Children.Add(Ui.Button("Open link",()=>Ui.OpenUrl(url.AbsoluteUri)));
            content.Children.Add(buttons);stack.Children.Add(Ui.Card(content));
        }
        window.Content=new ScrollViewer{Content=stack};await window.ShowDialog(this);
    }
}
