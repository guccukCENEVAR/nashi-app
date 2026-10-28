using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed record ClipboardResult(string[] Paths,string? Link,string Label);

/// What a paste or a drop carried, read once from either Avalonia transfer API.
internal sealed record TransferContent(string[] Files,string? Text,string? Html,string? UriList,Bitmap? Image)
{
    internal static readonly DataFormat<string> HtmlFormat=DataFormat.CreateStringPlatformFormat("text/html");
    internal static readonly DataFormat<string> UriListFormat=DataFormat.CreateStringPlatformFormat("text/uri-list");
    internal static readonly DataFormat<string> MozillaUrlFormat=DataFormat.CreateStringPlatformFormat("text/x-moz-url");
    internal static TransferContent From(IDataTransfer data)
    {
        string? Read(DataFormat<string> format){try{return data.Contains(format)?data.TryGetValue(format):null;}catch(Exception error)when(IsTransferError(error)){return null;}}
        var files=(data.TryGetFiles()??[]).Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray();
        Bitmap? image=null;if(files.Length==0){try{image=data.TryGetBitmap();}catch(Exception error)when(IsTransferError(error)){}}
        return new(files,data.TryGetText(),Read(HtmlFormat),Read(UriListFormat)??Read(MozillaUrlFormat),image);
    }
    internal static async Task<TransferContent> FromAsync(IClipboard clipboard)
    {
        using var data=await clipboard.TryGetDataAsync();if(data is null)return new([],null,null,null,null);
        async Task<string?> Read(DataFormat<string> format){try{return data.Contains(format)?await data.TryGetValueAsync(format):null;}catch(Exception error)when(IsTransferError(error)){return null;}}
        var files=((await data.TryGetFilesAsync())??[]).Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray();
        Bitmap? image=null;if(files.Length==0){try{image=await data.TryGetBitmapAsync();}catch(Exception error)when(IsTransferError(error)){}}
        return new(files,await data.TryGetTextAsync(),await Read(HtmlFormat),await Read(UriListFormat),image);
    }
    internal static bool IsTransferError(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or FormatException or ArgumentException;
    /// A web link carried by browser text, HTML or URL-list data.
    internal WebMediaPayload? Payload(string? hint=null)
    {
        var urls=UriList?.Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.StartsWith('#')).Where(l=>!l.StartsWith("file:",StringComparison.OrdinalIgnoreCase));
        var text=string.Join('\n',new[]{Text}.Concat(urls??[]).OfType<string>());
        return WebMediaPayload.Parse(text.Length==0?null:text,Html,hint);
    }
}

internal static class ClipboardImport
{
    // Text entry keeps its normal shortcuts; walk up because the focused element can be a template part.
    internal static bool IsEditing(object? element)
    {
        var visual=element as Avalonia.Visual;
        while(visual is not null){if(visual is TextBox or ComboBox)return true;visual=visual.GetVisualParent();}
        return false;
    }
    internal static ClipboardResult Read(TransferContent data,LibraryStore library)
    {
        if(data.Files.Length>0)
        {
            var files=data.Files.Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
            if(files.Length==0)throw new InvalidOperationException("Those files are no longer available.");
            return new(files,null,"Pasted files");
        }
        if(data.Image is {} bitmap)
        {
            var size=bitmap.PixelSize;if(size.Width<1||size.Height<1||(long)size.Width*size.Height>100_000_000)throw new InvalidOperationException("This image is too large to paste. Choose the image file instead.");
            var file=Destination(library,"png");using(var stream=new FileStream(file,FileMode.CreateNew))bitmap.Save(stream,PngBitmapEncoderOptions.Default);return new([file],null,"Pasted image");
        }
        var text=data.Text;
        if(string.IsNullOrWhiteSpace(text)&&data.Payload() is {} payload)return new([],payload.Url,"Copied link");
        if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException("Copy a file, some text, an image or a public media link first.");
        var trimmed=text.Trim();
        if(Uri.TryCreate(trimmed,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")return new([],uri.AbsoluteUri,"Copied link");
        if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new InvalidOperationException("This text is too large to paste. Choose a text file instead.");
        var path=Destination(library,"txt");File.WriteAllText(path,text,new UTF8Encoding(false));return new([path],null,"Pasted text");
    }
    private static string Destination(LibraryStore library,string extension)
    {
        var folder=Path.Combine(library.DirectoryPath,"Imports");Directory.CreateDirectory(folder);
        return Path.Combine(folder,$"Clipboard {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.{extension}");
    }
    internal static bool IsClipboardError(Exception e)=>TransferContent.IsTransferError(e);
}
