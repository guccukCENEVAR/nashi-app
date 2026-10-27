using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

internal readonly record struct FileArtworkStamp(bool Exists,bool Directory,long Length,long Modified)
{
    internal static FileArtworkStamp Read(string path)
    {
        try
        {
            var file=new FileInfo(path);if(file.Exists)return new(true,false,file.Length,file.LastWriteTimeUtc.Ticks);
            var directory=new DirectoryInfo(path);return directory.Exists?new(true,true,0,directory.LastWriteTimeUtc.Ticks):default;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return default;}
    }
}

/// Thumbnails for images and drawn type badges for everything else. Decoding stays off the UI
/// thread, results are cached by path and file stamp, and a failed thumbnail keeps the badge.
internal static class FileArtwork
{
    private static readonly string[] Thumbnailed=[".png",".jpg",".jpeg",".bmp",".gif",".webp"];
    private sealed record Entry(FileArtworkStamp Stamp,Bitmap Image,long Access);
    private static readonly object gate=new();private static readonly Dictionary<string,Entry> cache=new(StringComparer.Ordinal);private static long clock;private const int Capacity=128;
    internal static Control Create(string path,double size)
    {
        var host=new Grid{Width=size,Height=size};host.Children.Add(Badge(path,size));
        if(Thumbnailed.Contains(Path.GetExtension(path).ToLowerInvariant()))_ = LoadAsync(path,host,size);
        return host;
    }
    private static async Task LoadAsync(string path,Grid host,double size)
    {
        var stamp=FileArtworkStamp.Read(path);if(!stamp.Exists||stamp.Directory||stamp.Length>50_000_000)return;
        Bitmap? image;lock(gate){if(cache.TryGetValue(path,out var entry)&&entry.Stamp==stamp){cache[path]=entry with{Access=++clock};image=entry.Image;}else image=null;}
        if(image is null)
        {
            try{image=await Task.Run(()=>{using var stream=File.OpenRead(path);return Bitmap.DecodeToWidth(stream,128,BitmapInterpolationMode.MediumQuality);});}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException){return;}
            lock(gate){cache[path]=new(stamp,image,++clock);while(cache.Count>Capacity)cache.Remove(cache.MinBy(p=>p.Value.Access).Key);}
        }
        await Dispatcher.UIThread.InvokeAsync(()=>{host.Children.Clear();host.Children.Add(new Border{CornerRadius=new(Math.Max(3,size/8)),ClipToBounds=true,Child=new Image{Source=image,Stretch=Stretch.UniformToFill}});});
    }
    private static Control Badge(string path,double size)
    {
        if(Directory.Exists(path)&&FeatureCatalog.Extension(path) is not ("pages" or "rtfd"))
        {
            var folder=new Canvas{Width=size,Height=size};
            folder.Children.Add(new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse("M1,6 L11,6 L14,3 L31,3 L31,29 L1,29 Z"),Fill=new SolidColorBrush(Color.FromRgb(73,177,214)),Stretch=Stretch.Uniform,Width=size,Height=size*.86});
            return folder;
        }
        var kind=FeatureCatalog.IsImage(path)?"Sage":FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path)?"Sand":"Inset";
        var extension=Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var card=new Border{Width=size*.82,Height=size,CornerRadius=new(Math.Max(3,size/7)),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Center}.Res(Border.BackgroundProperty,kind).Res(Border.BorderBrushProperty,"Line");
        if(size>=24)card.Child=new TextBlock{Text=extension.Length>4?extension[..4]:extension,FontSize=Math.Max(6,size/4.2),FontWeight=FontWeight.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}.Res(TextBlock.ForegroundProperty,"Forest");
        return card;
    }
}
