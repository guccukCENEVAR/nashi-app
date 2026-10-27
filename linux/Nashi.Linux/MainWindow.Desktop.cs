using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    private string[]? pendingLaunch;private DispatcherTimer? launchTimer;
    private TrayHost? tray;private bool quitting,desktopReady;private GlobalDragController? globalDrag;
    internal const string OpenCopiedLinkCommand="--open-copied-link";
    internal void OpenCopiedLinkFromShortcut()=>OpenCopiedLink();
    internal void AcceptLaunch(string[] paths)
    {
        if(paths.Contains(OpenCopiedLinkCommand)){OpenCopiedLink();return;}
        paths=paths.Where(p=>Path.IsPathFullyQualified(p)&&(File.Exists(p)||Directory.Exists(p))).ToArray();
        ShowMain();if(paths.Length==0)return;
        if(busy||!IsVisible){pendingLaunch=paths;launchTimer??=new(){Interval=TimeSpan.FromMilliseconds(200)};launchTimer.Stop();launchTimer.Tick-=AcceptPending;launchTimer.Tick+=AcceptPending;launchTimer.Start();return;}
        files.Clear();Add(paths);ShowLibrary(false);
    }
    private void AcceptPending(object? sender,EventArgs e){if(busy||!IsVisible)return;launchTimer?.Stop();if(pendingLaunch is {} paths){pendingLaunch=null;AcceptLaunch(paths);}}
    /// A login launch keeps the window hidden; only the tray and drag shortcuts start.
    internal void StartInBackground(){SetupDesktop();if(tray is null)Show();}
    private void SetupDesktop()
    {
        if(desktopReady)return;desktopReady=true;
        try{tray=new(ShowMain,TrayMenu);}catch(Exception error)when(error is InvalidOperationException or PlatformNotSupportedException){L10n.SetText(status,"The system tray is unavailable. Nashi stays open in its window.");}
        globalDrag=new GlobalDragController(this,preferences,OnGlobalDrop);
        Closing+=(_,e)=>
        {
            if(e.Cancel)return;
            if(tray is not null&&preferences.Value.KeepInTray&&!quitting){e.Cancel=true;Hide();return;}
            quitting=true;App.Quit();
        };
        Closed+=(_,_)=>{launchTimer?.Stop();globalDrag?.Dispose();tray?.Dispose();};
    }
    private void ShowMain(){Show();WindowState=WindowState.Normal;Activate();if(monitorToolSetup)Dispatcher.UIThread.Post(OfferFirstRun,DispatcherPriority.Background);}
    private NativeMenu TrayMenu()
    {
        var menu=new NativeMenu();
        void Item(string text,Action action){var item=new NativeMenuItem(L10n.Text(text));item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Open Nashi",ShowMain);Item("Library",()=>{ShowMain();ShowLibrary(true);});Item("Download copied link",OpenCopiedLink);Item("Settings",()=>{ShowMain();ShowSettings();});
        Item(update is null?"Check for updates":"See Nashi "+update.Version,()=>{ShowMain();_ = CheckUpdatesAsync(true);});
        menu.Items.Add(new NativeMenuItemSeparator());
        Item("Quit Nashi",()=>{if(busy){ShowMain();L10n.SetText(status,"Finish or cancel the current operation before quitting.");return;}quitting=true;App.Quit();});
        return menu;
    }
    private void ApplyPreferences(){L10n.Language=preferences.Value.Language;Feedback.Enabled=preferences.Value.Feedback;WheelPresentation.Preferences=preferences.Value;tray?.Rebuild();if(IsVisible)Refresh();}
    private async void ShowSettings()
    {
        var settings=new SettingsWindow(preferences,ApplyPreferences,()=>CheckUpdatesAsync(true),library);
        if(IsVisible)await settings.ShowDialog(this);else settings.Show();
    }
    private async void OpenCopiedLink()
    {
        if(busy||Clipboard is null)return;
        try
        {
            var payload=(await TransferContent.FromAsync(Clipboard)).Payload();if(payload is null){L10n.SetText(status,"Copy a public media link first.");return;}
            ShowMain();link.Text=payload.Url;SetDownloadKind(payload);OpenDownload();
        }
        catch(Exception error)when(ClipboardImport.IsClipboardError(error)){L10n.SetText(status,"Clipboard is busy. Try again.");}
    }
    private void SetDownloadKind(WebMediaPayload payload,string? chosen=null)
    {
        var formats=payload.Kind switch{"youtube"=>new[]{"video","audio","thumbnail"},"instagramPhotos"=>["photos"],"instagramReel"=>["video","audio"],"image"=>["image"],_=>["video","audio","image"]};
        L10n.SetChoices(downloadKind,formats,formats.Contains(chosen??"")?chosen:formats[0]);
    }
    /// Files dropped on Nashi with a drag chord open the matching wheel, as in Finder or Explorer.
    private void OnWindowDrop(TransferContent data,KeyModifiers modifiers)
    {
        var kind=GlobalDragController.Kind(preferences.Value,modifiers);
        if(data.Files.Length==0&&data.Payload() is {} payload){OnGlobalDrop("download:auto",data);return;}
        if(kind is null||data.Files.Length==0){Import(data);return;}
        files.Clear();Add(data.Files);ShowLibrary(false);
        var choices=GlobalDragController.ChoicesFor(kind,data.Files);if(choices.Count==0){L10n.SetText(status,"No matching tools for these files.");return;}
        WheelWindow.Present(this,choices,async choice=>await RunDropChoice(choice.Id,data.Files));
    }
    internal void OnGlobalDrop(string action,TransferContent data)
    {
        if(busy)return;
        Dispatcher.UIThread.Post(async()=>
        {
            if(busy)return;
            if(data.Files.Length>0&&!action.StartsWith("download:",StringComparison.Ordinal)){files.Clear();Add(data.Files);await RunDropChoice(action,data.Files);}
            else if(data.Payload() is {} payload){link.Text=payload.Url;SetDownloadKind(payload,action.StartsWith("download:",StringComparison.Ordinal)?action[9..]:null);ShowMain();OpenDownload();}
        });
    }
    private async Task RunDropChoice(string action,IReadOnlyList<string> paths)
    {
        if(action.StartsWith("convert:",StringComparison.Ordinal))await EditAsync(new(action,"Convert","↗"));
        else if(FileActionChoices.Any(c=>c.Id==action))await PerformFileAction(action,paths);
        else await EditAsync(new(action,FeatureCatalog.ToolNames.GetValueOrDefault(action,action),"✦"));
    }
}
