using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux;

/// What each optional runtime provides and which Arch package supplies it.
internal sealed record ToolPackage(string Name,string Arch,Func<string?> Locate);
internal sealed record ToolGroupStatus(string Title,string Detail,ToolPackage[] Packages,bool Required)
{
    internal ToolPackage[] Missing=>Packages.Where(p=>p.Locate() is null).ToArray();
    internal bool Ready=>Missing.Length==0;
}

/// First-run setup. On Linux the tools come from the distribution, so Nashi shows
/// exactly what is missing and can run `pkexec pacman -S --needed …` after asking.
internal sealed class ToolSetupWindow:Window
{
    private readonly Action<bool> workingChanged;private readonly StackPanel groups=new();
    private readonly TextBlock status=Ui.Text("Install the missing tools to enable all conversions, previews and web downloads. Tools already on your computer are kept.",13);
    private readonly ProgressBar progress=new(){IsIndeterminate=true,Height=6,IsVisible=false,Margin=new(0,12)};
    private readonly Button next,install,copy;private bool working;
    private readonly Toolchain media;private readonly OfficeRuntime office;private readonly MetadataService metadata;
    internal static ToolGroupStatus[] Groups(Toolchain media,OfficeRuntime office,MetadataService metadata)
    {
        ToolPackage Media(string name,string arch)=>new(name,arch,()=>media.Missing.Contains(name)?null:media.Executable(name));
        return
        [
            new("Media tools","FFmpeg converts, edits and previews audio and video. yt-dlp and Deno power public web downloads and Pages interoperability.",[Media("ffmpeg","ffmpeg"),Media("ffprobe","ffmpeg"),Media("yt-dlp","yt-dlp"),Media("deno","deno")],true),
            new("Office tools","LibreOffice converts documents, slides and spreadsheets and previews Office files.",[new("soffice","libreoffice-fresh",()=>office.Executable)],true),
            new("Metadata tools","ExifTool reads and edits image and PDF metadata.",[new("exiftool","perl-image-exiftool",()=>metadata.Executable)],true),
            new("Text and face detection","Optional: Tesseract recognizes text in scans and redaction areas; OpenCV finds faces to redact.",[new("tesseract","tesseract tesseract-data-eng tesseract-data-tur",()=>Ocr.Executable),new("python-opencv","python-opencv",Faces.ModulePath)],false)
        ];
    }
    private ToolGroupStatus[] Groups()=>Groups(media,office,metadata);
    private bool Ready=>Groups().Where(g=>g.Required).All(g=>g.Ready);
    private static bool HasPacman=>SystemTools.Find("pacman") is not null;
    private string[] MissingPackages(bool includeOptional)=>Groups().Where(g=>includeOptional||g.Required).SelectMany(g=>g.Missing).SelectMany(p=>p.Arch.Split(' ')).Distinct().ToArray();
    public ToolSetupWindow(Toolchain media,OfficeRuntime office,MetadataService metadata,Action<bool> workingChanged)
    {
        this.media=media;this.office=office;this.metadata=metadata;this.workingChanged=workingChanged;Width=680;Height=720;MinWidth=600;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Set up Nashi tools");
        var root=new DockPanel();Content=root;
        var footer=new StackPanel{Margin=new(24)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(status);footer.Children.Add(progress);
        var actions=new DockPanel{Margin=new(0,14,0,0)};footer.Children.Add(actions);
        next=Ui.Primary("Install missing tools",async()=>{if(Ready)Close();else await InstallAsync();});AutomationProperties.SetAutomationId(next,"SetupContinue");DockPanel.SetDock(next,Dock.Right);actions.Children.Add(next);
        install=Ui.Button("Install optional tools too",async()=>await InstallAsync(includeOptional:true));DockPanel.SetDock(install,Dock.Right);actions.Children.Add(install);
        copy=Ui.Button("Copy command",async()=>{if(Clipboard is not null)await Clipboard.SetTextAsync(Command(true));L10n.SetText(status,"Command copied. Paste it in a terminal, then come back to Nashi.");});actions.Children.Add(copy);
        var content=new StackPanel{Margin=new(28)};root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(Ui.Eyebrow("FIRST-TIME SETUP"));content.Children.Add(Ui.Heading("A few tools. More possibilities."));
        content.Children.Add(Ui.Text("Nashi uses trusted tools from your distribution. Nothing is downloaded from unofficial sources and no account is needed.",14,"Muted").Also(t=>t.Margin=new(0,8,0,8)));
        content.Children.Add(groups);
        content.Children.Add(Ui.Button("Upstream versions, licenses and source links",()=>Ui.OpenUrl("https://github.com/"+PublicUpdates.Repository+"/blob/main/docs/dependencies.md")));
        Closing+=(_,e)=>{if(working)e.Cancel=true;};Activated+=(_,_)=>{if(!working)Refresh();};
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape&&!working){Close();e.Handled=true;}},RoutingStrategies.Tunnel);
        Refresh();
    }
    private string Command(bool includeOptional)
    {
        var packages=MissingPackages(includeOptional);if(packages.Length==0)return "";
        return HasPacman?"sudo pacman -S --needed "+string.Join(' ',packages):"# Install with your package manager: "+string.Join(' ',packages);
    }
    private void Refresh()
    {
        groups.Children.Clear();
        foreach(var group in Groups())
        {
            var row=new StackPanel{Margin=new(14)};row.Children.Add(Ui.Text(group.Title,18));row.Children.Add(Ui.Text(group.Detail,13,"Muted").Also(t=>t.Margin=new(0,4,0,8)));
            foreach(var package in group.Packages)
            {
                var found=package.Locate();var line=new DockPanel{Margin=new(0,2)};
                var mark=Ui.Text(found is null?"○":"●",13,found is null?"Muted":"Forest",localize:false);mark.Margin=new(0,0,8,0);DockPanel.SetDock(mark,Dock.Left);line.Children.Add(mark);
                line.Children.Add(Ui.Text(package.Name+"  ·  "+(found??L10n.Text("missing")+" — "+package.Arch),12,found is null?"Muted":null,localize:false));
                row.Children.Add(line);
            }
            if(group.Ready)row.Children.Add(Ui.Text("Already available on your computer",12,"Forest").Also(t=>t.Margin=new(0,6,0,0)));
            groups.Children.Add(new Border{Child=row,CornerRadius=new(16),BorderThickness=new(1),Margin=new(0,12,0,8)}.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line"));
        }
        var ready=Ready;L10n.Bind(next,ContentControl.ContentProperty,ready?"Continue to Nashi":HasPacman?"Install missing tools":"Copy command");
        next.IsEnabled=!working;install.IsVisible=HasPacman&&MissingPackages(true).Length>MissingPackages(false).Length&&!working;copy.IsVisible=HasPacman&&MissingPackages(true).Length>0;
        progress.IsVisible=working;
        if(ready&&!working)L10n.SetText(status,"Ready. You can go back to your files.");
    }
    /// Asks before touching the system; pacman runs through polkit so no password is handled by Nashi.
    private async Task InstallAsync(bool includeOptional=false)
    {
        if(working)return;var packages=MissingPackages(includeOptional);if(packages.Length==0){Refresh();return;}
        if(!HasPacman||SystemTools.Find("pkexec") is not {} pkexec){if(Clipboard is not null)await Clipboard.SetTextAsync(Command(includeOptional));L10n.SetText(status,"Command copied. Paste it in a terminal, then come back to Nashi.");return;}
        if(!await Dialogs.Confirm(this,"Install these packages with pacman?\n\n"+string.Join(' ',packages)+"\n\nYour system asks for your password. Nashi never sees it.","Install tools","Install"))return;
        working=true;workingChanged(true);Refresh();L10n.SetText(status,"Installing with pacman…");
        try
        {
            var arguments=new List<string>{"pacman","-S","--needed","--noconfirm"};arguments.AddRange(packages);
            await ProcessRunner.RunAsync(pkexec,arguments);
            L10n.SetText(status,Ready?"Ready. You can go back to your files.":"Some tools are still missing. Check the list above.");
        }
        catch(Exception error)when(error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status.Text=L10n.Text("Installation did not finish. Run this in a terminal instead:")+"\n"+Command(includeOptional)+"\n\n"+error.Message.Trim();
        }
        finally{working=false;workingChanged(false);Refresh();}
    }
}
