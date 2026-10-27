using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

// One preview lifecycle for Library and Float. Each navigation cancels the previous load.
internal sealed class FilePreviewWindow:Window
{
    private readonly string[] paths;private int index;
    private readonly Toolchain tools;private readonly OfficeRuntime office;private readonly string temporary=Path.Combine(Path.GetTempPath(),"Nashi-quick-preview-"+Guid.NewGuid().ToString("N"));private CancellationTokenSource lifetime=new();
    private readonly ContentControl content=new();private readonly TextBlock caption=new(){TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    private MediaPlayer? player;private readonly Slider scrub=new(){Minimum=0,Maximum=1,MinWidth=240};private bool updating;
    private PdfRasterDocument? pdfDocument;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly StackPanel transport=new(){Orientation=Orientation.Horizontal,IsVisible=false};
    internal string PreviewStatus=>content.Content is TextBlock text?text.Text??"":content.Content?.GetType().Name??"Empty";
    internal string CurrentPath=>paths[index];internal bool Playing=>player?.Playing==true;
    internal FilePreviewWindow(IEnumerable<string> paths,Toolchain tools,OfficeRuntime office)
    {
        this.tools=tools;this.office=office;
        this.paths=paths.Distinct(StringComparer.Ordinal).ToArray();if(this.paths.Length==0)throw new ArgumentException("Choose a file to preview.");
        Width=800;Height=620;MinWidth=560;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();Title="Preview · Nashi";
        var root=new DockPanel{Margin=new(20)};Content=root;var header=new DockPanel{Margin=new(0,0,0,14)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(navigation,Dock.Right);header.Children.Add(navigation);
        navigation.Children.Add(Ui.Button("←",()=>Navigate(-1)));navigation.Children.Add(Ui.Button("→",()=>Navigate(1)));navigation.Children.Add(Ui.Button("Open",()=>FileActions.Open(CurrentPath)));header.Children.Add(caption);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);transport.Children.Add(Ui.Button("Play / pause",Toggle));transport.Children.Add(scrub);footer.Children.Add(transport);
        footer.Children.Add(Ui.Text("←/→ files · Media: Space play/pause, ←/→ 1s, Alt+←/→ files · Esc closes",11,"Muted").Also(t=>t.Margin=new(0,12,0,0)));root.Children.Add(content);
        scrub.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!updating)player?.Seek(scrub.Value);};
        timer.Tick+=(_,_)=>{if(player is null)return;updating=true;scrub.Value=player.Position;updating=false;};
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.Key==Key.Escape){Close();e.Handled=true;return;}
            if(ClipboardImport.IsEditing(FocusManager?.GetFocusedElement())&&FocusManager?.GetFocusedElement() is not TextBox{IsReadOnly:true})return;
            if(e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))return;
            if(e.Key==Key.Space){if(player is null)Close();else Toggle();e.Handled=true;}
            else if(e.Key is Key.Left or Key.Right){var direction=e.Key==Key.Left?-1:1;if(player is not null&&e.KeyModifiers==KeyModifiers.None)player.Seek(player.Position+direction);else Navigate(direction);e.Handled=true;}
        },RoutingStrategies.Tunnel);
        Opened+=async(_,_)=>await Load();
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();timer.Stop();CloseMedia();pdfDocument?.Dispose();pdfDocument=null;_ = Cleanup();};
    }
    internal void Navigate(int delta){if(paths.Length<2)return;index=(index+delta+paths.Length)%paths.Length;_ = Load();}
    private void CloseMedia(){player?.Dispose();player=null;transport.IsVisible=false;timer.Stop();}
    private void Toggle()=>player?.Toggle();
    private async Task Cleanup(){for(var n=0;n<10;n++){try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);return;}catch(IOException){await Task.Delay(250);}catch(UnauthorizedAccessException){return;}}}
    private void Message(string text){content.Content=Ui.Text(text,14).Also(t=>t.Margin=new(20));}
    private async Task Load()
    {
        lifetime.Cancel();lifetime.Dispose();lifetime=new();var token=lifetime.Token;CloseMedia();pdfDocument?.Dispose();pdfDocument=null;Message("Preparing preview…");
        var path=CurrentPath;caption.Text=$"{Path.GetFileName(path)}  ·  {index+1} / {paths.Length}";Title=Path.GetFileName(path)+" · "+L10n.Text("Preview")+" · Nashi";
        try
        {
            if(Directory.Exists(path)&&FeatureCatalog.Extension(path) is not ("pages" or "rtfd")){content.Content=Ui.Text("Folder · "+Path.GetFileName(path)+"\nUse Open to view its contents.",14);return;}
            if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("This file has moved or was removed.");
            if(new[]{"docx","doc","rtf","rtfd","pages","xlsx","xls","ods","pptx","ppt","odp","odt","epub"}.Contains(FeatureCatalog.Extension(path)))
            {
                if(office.Executable is null){Message("Install LibreOffice to preview this document here, or use Open.");return;}
                Directory.CreateDirectory(temporary);path=(await new DocumentConversion(office,tools).ConvertAsync([path],"pdf",temporary,new(),null,token))[0];token.ThrowIfCancellationRequested();
            }
            if(FeatureCatalog.IsImage(path))
            {
                var frame=await Task.Run(()=>ImageEngine.Load(path),token);using(frame){if(!token.IsCancellationRequested)content.Content=new Image{Source=frame.ToAvalonia(),Stretch=Stretch.Uniform};}
            }
            else if(FeatureCatalog.Extension(path)=="pdf")
            {
                // Pages render on demand rather than decoding the whole document first.
                var pdf=await Task.Run(()=>PdfRasterDocument.Open(path),token);if(token.IsCancellationRequested){pdf.Dispose();return;}pdfDocument=pdf;
                var pageIndex=0;var panel=new DockPanel();var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Bottom);panel.Children.Add(controls);var image=new Image{Stretch=Stretch.Uniform};panel.Children.Add(image);
                async Task Page(int delta)
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();pageIndex=Math.Clamp(pageIndex+delta,0,pdf.PageCount-1);var requested=pageIndex;var png=await Task.Run(()=>pdf.RenderPage(requested,144,token),token);
                        if(!token.IsCancellationRequested&&requested==pageIndex){using var rendered=ImageFrame.FromPng(png);image.Source=rendered.ToAvalonia();}
                    }
                    catch(OperationCanceledException){}
                    catch(Exception error)when(error is IOException or InvalidDataException or ObjectDisposedException or ArgumentOutOfRangeException){if(!token.IsCancellationRequested&&ReferenceEquals(pdfDocument,pdf))Message("Preview unavailable. "+error.Message);}
                }
                controls.Children.Add(Ui.Button("Previous page",async()=>await Page(-1)));controls.Children.Add(Ui.Button("Next page",async()=>await Page(1)));controls.Children.Add(Ui.Text($"{pdf.PageCount} pages",13));content.Content=panel;await Page(0);
            }
            else if(FeatureCatalog.IsVideo(path)||FeatureCatalog.IsAudio(path))
            {
                if(!tools.Ready){Message("Install the media tools to preview this file here, or use Open.");return;}
                var media=new MediaPlayer(tools);player=media;await media.OpenAsync(path,token:token);if(token.IsCancellationRequested||!ReferenceEquals(player,media))return;
                scrub.Maximum=Math.Max(.001,media.Duration);media.Ended+=()=>{if(ReferenceEquals(player,media)){updating=true;scrub.Value=scrub.Maximum;updating=false;}};
                content.Content=media.HasVideo?media.View:Ui.Text("♫  "+Path.GetFileName(path),20,localize:false).Also(t=>{t.HorizontalAlignment=HorizontalAlignment.Center;t.VerticalAlignment=VerticalAlignment.Center;});
                transport.IsVisible=true;timer.Start();
            }
            else if(new[]{"txt","md","csv","tsv","srt","vtt","json","html","xml","log"}.Contains(FeatureCatalog.Extension(path)))
            {
                if(new FileInfo(path).Length>2_000_000)throw new InvalidDataException("This text is too large to preview. Use Open to read the complete file.");
                var text=LegacyTextCodec.Decode(await File.ReadAllBytesAsync(path,token));if(!token.IsCancellationRequested)content.Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true};
            }
            else Message("Use Open to preview this document in its default app.");
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or ImageMagick.MagickException){if(!token.IsCancellationRequested)Message("Preview unavailable. "+error.Message);}
    }
}
