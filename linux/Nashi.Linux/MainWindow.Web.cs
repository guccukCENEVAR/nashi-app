using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    private readonly TextBlock webSummary=Ui.Text("Paste a public media link to begin.",14);
    private readonly ComboBox webQuality=new(){MinWidth=180};
    private readonly StackPanel photoChoices=new();private readonly HashSet<int> selectedWebPhotos=[];private CancellationTokenSource? inspection;private string? inspectedLink,webPayloadKind;
    private void BuildWebOptions()
    {
        webSummary.Margin=new(4,12,4,12);
        webQuality.ItemsSource=new[]{"Best available","Up to 1080p","Up to 720p"}.Select(q=>L10n.Bind(new ComboBoxItem(),ContentControl.ContentProperty,q)).ToArray();webQuality.SelectedIndex=0;
        var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Find media",async()=>await InspectWebLinkAsync()));
        buttons.Children.Add(Ui.Button("Paste link",async()=>{try{var clipboard=webWindow?.Clipboard??Clipboard;if(clipboard is not null){var content=await TransferContent.FromAsync(clipboard);link.Text=content.Payload()?.Url??content.Text;}}catch(Exception e)when(ClipboardImport.IsClipboardError(e)){L10n.SetText(webSummary,"Clipboard is busy. Try Paste again.");}}));
        webContent.Children.Add(buttons);webContent.Children.Add(webSummary);
        var quality=new StackPanel();quality.Children.Add(Ui.Text("Video quality",12,"Muted"));quality.Children.Add(webQuality);webContent.Children.Add(quality);
        downloadKind.SelectionChanged+=(_,_)=>quality.IsVisible=L10n.Selected(downloadKind)=="video";webContent.Children.Add(photoChoices);
        var destination=new DockPanel{Margin=new(0,14,0,4)};var location=Ui.Text(preferences.Value.DownloadFolder,12,"Muted",localize:false);
        var change=Ui.Button("Change…",async()=>{var picked=await Dialogs.PickFolder(webWindow??this,"Save downloaded media to…",preferences.Value.DownloadFolder);if(picked is not null){preferences.Save(preferences.Value with{DownloadFolder=picked});location.Text=picked;}});
        DockPanel.SetDock(change,Dock.Right);destination.Children.Add(change);destination.Children.Add(location);webContent.Children.Add(Ui.Card(destination));
        webContent.Children.Add(Ui.Text("Instagram photo posts open a photo selection. Reel links offer video and audio. Public downloadable media only.",11,"Muted"));
        link.TextChanged+=(_,_)=>
        {
            inspection?.Cancel();inspectedLink=null;photoChoices.Children.Clear();selectedWebPhotos.Clear();var payload=WebMediaPayload.Parse(link.Text);
            if(payload is not null&&payload.Kind!=webPayloadKind){webPayloadKind=payload.Kind;SetDownloadKind(payload);}
            L10n.SetText(webSummary,"Choose Find media to inspect this link.");
        };
    }
    private async Task InspectWebLinkAsync()
    {
        if(busy)return;inspection?.Cancel();var pending=new CancellationTokenSource();inspection=pending;var input=link.Text??"";var payload=WebMediaPayload.Parse(input);
        if(payload is null){L10n.SetText(webSummary,"Paste one complete public media link.");return;}
        SetDownloadKind(payload,L10n.Selected(downloadKind));photoChoices.Children.Clear();selectedWebPhotos.Clear();L10n.SetText(webSummary,"Finding the media…");
        try
        {
            var engine=new WebDownload(tools);
            if(payload.Kind=="instagramPhotos")
            {
                if(!tools.Ready)throw new InvalidOperationException("Install the media tools once to inspect Instagram posts.");var photos=await engine.InspectPhotosAsync(payload.Url,pending.Token);if(input!=link.Text||pending.IsCancellationRequested)return;
                L10n.SetText(webSummary,$"{photos.Length} photos · full-frame images · saved in post order");var row=new WrapPanel();
                row.Children.Add(Ui.Button("All",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=true;}));row.Children.Add(Ui.Button("None",()=>{foreach(var box in photoChoices.Children.OfType<CheckBox>())box.IsChecked=false;}));photoChoices.Children.Add(row);
                foreach(var photo in photos){selectedWebPhotos.Add(photo.Id);var box=new CheckBox{Content=$"Photo {photo.Id}  ·  {photo.Width} × {photo.Height}",IsChecked=true,Margin=new(4,9)};box.IsCheckedChanged+=(_,_)=>{if(box.IsChecked==true)selectedWebPhotos.Add(photo.Id);else selectedWebPhotos.Remove(photo.Id);};photoChoices.Children.Add(box);}
            }
            else if(payload.Kind=="image"||L10n.Selected(downloadKind)=="thumbnail")L10n.SetText(webSummary,payload.Kind=="image"?"Original image · ready to download":"YouTube thumbnail · best available full-size image");
            else
            {
                if(!tools.Ready)throw new InvalidOperationException("Install the media tools once to inspect this media.");var info=await engine.InspectAsync(payload.Url,pending.Token);if(input!=link.Text||pending.IsCancellationRequested)return;
                webSummary.Text=info.Title+"\n"+info.Site+(info.Duration is {} seconds?" · "+TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss",System.Globalization.CultureInfo.InvariantCulture):"");
                if(info.AudioOnly)L10n.SetChoices(downloadKind,["audio"],"audio");
            }
            inspectedLink=input;
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!pending.IsCancellationRequested)webSummary.Text=L10n.Text(error.Message);}
        finally{if(inspection==pending)inspection=null;pending.Dispose();}
    }
}
