using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// `Nashi --ui-audit OUTPUT [--scenes a,b] [--appearance light|dark|both] [--language en|tr|both]`:
/// renders each window to PNG with an isolated Library, for visual review of both themes and languages.
internal static class UIAudit
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--ui-audit");if(index+1>=args.Length)return 64;var output=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(output);
        string? Option(string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        var scenes=Option("--scenes")?.Split(',');var appearances=Option("--appearance") is {} a&&a!="both"?[a]:new[]{"light","dark"};var languages=Option("--language") is {} l&&l!="both"?[l]:new[]{"en","tr"};
        var work=Path.Combine(output,".fixtures");if(Directory.Exists(work))Directory.Delete(work,true);Directory.CreateDirectory(work);var image=Path.Combine(work,"Pear.png");
        using(var frame=ImageEngine.Render(960,640,c=>{c.Clear(new SkiaSharp.SKColor(240,226,180));using var paint=new SkiaSharp.SKPaint{Color=new SkiaSharp.SKColor(95,130,70),IsAntialias=true};c.DrawCircle(480,330,210,paint);}))ImageEngine.Save(frame,image,"png");
        var pdf=ImageEngine.Apply("createPDF",[image,image],work,new())[0];var text=Path.Combine(work,"Notes.txt");File.WriteAllText(text,"Nashi · Linux\nA small note.");
        var tools=new Toolchain(Path.Combine(work,"Tools"));string? video=null;
        if(SystemTools.Find("ffmpeg") is {} ffmpeg){video=Path.Combine(work,"Clip.mp4");await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=size=640x360:rate=30","-f","lavfi","-i","sine=frequency=330","-t","4","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac","-shortest",video]);}
        var failures=0;
        foreach(var language in languages)foreach(var appearance in appearances)
        {
            L10n.Language=language;NashiTheme.Apply(appearance);await Task.Delay(100);
            var library=new LibraryStore(Path.Combine(work,"Library-"+language+appearance));if(library.Entries.Count==0){library.Record("Convert",[image]);library.Record("Pasted text",[text]);}
            // Each MainWindow re-applies its Library's stored language and appearance, so seed them per variant.
            library.SetAppearance(appearance);var preferences=new PreferencesStore(library.DirectoryPath);preferences.Save(preferences.Value with{Language=language});
            async Task Capture(string name,Func<Task<Window>> open)
            {
                if(scenes is not null&&!scenes.Contains(name))return;
                Window? window=null;
                try
                {
                    window=await open();await Task.Delay(700);
                    var size=new PixelSize(Math.Max(1,(int)window.Bounds.Width),Math.Max(1,(int)window.Bounds.Height));
                    using var bitmap=new RenderTargetBitmap(size,new Vector(96,96));bitmap.Render(window);bitmap.Save(Path.Combine(output,$"{name}-{appearance}-{language}.png"));
                }
                catch(Exception error){failures++;await File.WriteAllTextAsync(Path.Combine(output,$"{name}-{appearance}-{language}.error.txt"),error.ToString());}
                finally{window?.Close();await Task.Delay(100);}
            }
            MainWindow Main(string[] files){var main=new MainWindow(files,library,checkForUpdates:false);main.Show();return main;}
            MainWindow? host=null;
            await Capture("home-empty",()=>Task.FromResult<Window>(Main([])));
            await Capture("home-selection",()=>Task.FromResult<Window>(Main([image,pdf])));
            await Capture("library",async()=>{var main=Main([]);await Task.Delay(200);main.ShowLibrary(true);return main;});
            host=Main([image]);await Task.Delay(300);
            await Capture("wheel",()=>Task.FromResult<Window>(WheelWindow.Present(host,MainWindow.Choices([image]),_=>{})));
            await Capture("wheel-convert",()=>Task.FromResult<Window>(WheelWindow.Present(host,GlobalDragController.ChoicesFor("convert",[image]),_=>{})));
            await Capture("wheel-actions",()=>Task.FromResult<Window>(WheelWindow.Present(host,MainWindow.FileActionChoices,_=>{})));
            foreach(var tool in new[]{"crop","editImage","frameImage","annotate","redactImage","compress","convert"})
                await Capture("editor-"+tool,()=>{var editor=new ToolEditor(tool,tool=="convert"?"Convert":FeatureCatalog.ToolNames.GetValueOrDefault(tool switch{"crop"=>"cropImage","annotate"=>"annotateImage",_=>tool},tool),image,preview:ImageEngine.Load(image),outputFormats:tool=="convert"?FeatureCatalog.Formats([image]):null);editor.ConfigureProfiles(new PreferencesStore(library.DirectoryPath));editor.Show(host);return Task.FromResult<Window>(editor);});
            await Capture("editor-document",()=>{var editor=new ToolEditor("convert","Convert",text,inputs:[text],outputFormats:FeatureCatalog.Formats([text]));editor.ConfigureProfiles(new PreferencesStore(library.DirectoryPath));editor.Show(host);return Task.FromResult<Window>(editor);});
            if(video is not null&&SystemTools.Find("ffprobe") is not null&&host.Tools.Ready)
            {
                var info=await new MediaEngine(SystemTools.Find("ffmpeg")!,SystemTools.Find("ffprobe")!).InspectAsync(video!);
                await Capture("editor-trimVideo",()=>{var editor=new ToolEditor("trimVideo","Trim Video",video!,info);editor.ConfigureMediaRuntime(host.Tools);editor.Show(host);return Task.FromResult<Window>(editor);});
                await Capture("preview-video",()=>{var preview=new FilePreviewWindow([video!],host.Tools,host.Office);preview.Show(host);return Task.FromResult<Window>(preview);});
            }
            await Capture("download",async()=>{host.OpenDownload();await Task.Delay(200);host.LinkField.Text="https://www.youtube.com/watch?v=dQw4w9WgXcQ";return host.DownloadWindow!;});
            await Capture("settings",()=>{var settings=new SettingsWindow(new PreferencesStore(library.DirectoryPath),()=>{},()=>Task.CompletedTask,library);settings.Show(host);return Task.FromResult<Window>(settings);});
            await Capture("setup",()=>{var setup=new ToolSetupWindow(tools,host.Office,new MetadataService(Path.Combine(work,"Tools")),_=>{});setup.Show(host);return Task.FromResult<Window>(setup);});
            await Capture("welcome",()=>{var welcome=new WelcomeWindow(false,()=>{});welcome.Show(host);return Task.FromResult<Window>(welcome);});
            await Capture("preview-image",()=>{var preview=new FilePreviewWindow([image,pdf],host.Tools,host.Office);preview.Show(host);return Task.FromResult<Window>(preview);});
            await Capture("preview-pdf",()=>{var preview=new FilePreviewWindow([pdf],host.Tools,host.Office);preview.Show(host);return Task.FromResult<Window>(preview);});
            await Capture("float",async()=>{var shelf=new FloatShelf([image,pdf,text],(_,_)=>Task.CompletedTask,_=>{},host.Tools,host.Office);shelf.Show();await Task.Delay(200);shelf.SetExpanded(true);return shelf;});
            await Capture("organizer",async()=>{var organizer=await PdfOrganizer.CreateAsync(pdf);organizer.Show(host);return organizer;});
            await Capture("metadata",()=>{var editor=new MetadataEditor(new Dictionary<string,string>{["SourceFile"]=image,["EXIF:Artist"]="Nashi",["File:FileType"]="PNG"});editor.Show(host);return Task.FromResult<Window>(editor);});
            await Capture("toast",()=>{var toast=new ToastWindow(host,null);toast.Show();toast.Update(new(.42,"Saving your files…"));return Task.FromResult<Window>(toast);});
            await Capture("dialog",()=>{_ = Dialogs.Info(host,"Install the missing tools to enable all conversions, previews and web downloads. Tools already on your computer are kept.","Media setup");return Task.FromResult(host.OwnedWindows.Last());});
            host.Close();
        }
        Console.WriteLine($"UI audit written to {output}; {failures} failures");return failures==0?0:1;
    }
}
