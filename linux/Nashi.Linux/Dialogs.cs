using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace Nashi.Linux;

/// Message boxes and native (XDG portal) file pickers.
internal static class Dialogs
{
    internal static Task Info(Window? owner,string message,string title)=>Show(owner,message,title,"OK",null);
    internal static Task<bool> Confirm(Window? owner,string message,string title,string ok="OK",string cancel="Cancel")=>Show(owner,message,title,ok,cancel);
    private static async Task<bool> Show(Window? owner,string message,string title,string ok,string? cancel)
    {
        var window=new Window{Width=520,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=owner is {IsVisible:true}?WindowStartupLocation.CenterOwner:WindowStartupLocation.CenterScreen,ShowInTaskbar=owner is not {IsVisible:true},Icon=Ui.Icon()};
        L10n.Title(window,title);
        var stack=new StackPanel{Margin=new(26)};
        var heading=Ui.Text(title,19);heading.Margin=new(0,0,0,10);stack.Children.Add(heading);
        var body=Ui.Text(message,13);body.Margin=new(0,0,0,18);stack.Children.Add(body);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};stack.Children.Add(buttons);
        var result=false;
        if(cancel is not null)buttons.Children.Add(Ui.Button(cancel,()=>window.Close()));
        var accept=Ui.Primary(ok,()=>{result=true;window.Close();});accept.IsDefault=true;buttons.Children.Add(accept);
        window.Content=stack;
        window.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){window.Close();e.Handled=true;}};
        window.Opened+=(_,_)=>accept.Focus();
        if(owner is {IsVisible:true})await window.ShowDialog(owner);
        else{var closed=new TaskCompletionSource();window.Closed+=(_,_)=>closed.TrySetResult();window.Show();await closed.Task;}
        return result;
    }
    internal static readonly FilePickerFileType Images=new("Images"){Patterns=["*.png","*.jpg","*.jpeg","*.webp","*.heic","*.heif","*.avif","*.tiff","*.tif","*.bmp","*.gif","*.svg"],MimeTypes=["image/*"]};
    internal static async Task<string[]> PickFiles(Window owner,string title,bool multiple=true,FilePickerFileType? filter=null)
    {
        var options=new FilePickerOpenOptions{Title=L10n.Text(title),AllowMultiple=multiple};if(filter is not null)options.FileTypeFilter=[filter];
        var files=await owner.StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray();
    }
    internal static async Task<string?> PickFolder(Window owner,string title,string? start=null)
    {
        var options=new FolderPickerOpenOptions{Title=L10n.Text(title),AllowMultiple=false};
        if(start is not null&&Directory.Exists(start))options.SuggestedStartLocation=await owner.StorageProvider.TryGetFolderFromPathAsync(start);
        var folders=await owner.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Select(f=>f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }
}
