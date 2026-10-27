using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
namespace Nashi.Linux;

public sealed partial class App:Application
{
    internal static IClassicDesktopStyleApplicationLifetime? Desktop{get;private set;}
    internal static void Quit(int code=0)=>Desktop?.Shutdown(code);
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Desktop=desktop;var args=Program.Arguments;
            if(args.Contains("--ui-audit")){Dispatcher.UIThread.Post(async()=>Quit(await UIAudit.RunAsync(args)));base.OnFrameworkInitializationCompleted();return;}
            if(args.Contains("--smoke-test")){Dispatcher.UIThread.Post(async()=>Quit(await SmokeTests.RunAsync()));base.OnFrameworkInitializationCompleted();return;}
            var paths=args.Where(p=>!p.StartsWith("--",StringComparison.Ordinal)&&(File.Exists(p)||Directory.Exists(p))).Select(Path.GetFullPath).ToArray();
            var background=args.Contains("--background");
            var window=new MainWindow(paths,offerToolSetup:!background);
            Program.Instance?.Start(incoming=>Dispatcher.UIThread.Post(()=>window.AcceptLaunch(incoming)));
            desktop.Exit+=(_,_)=>{Feedback.Stop();PdfRasterDocument.Shutdown();};
            if(!background)window.Show();else window.StartInBackground();
            if(args.Contains(MainWindow.OpenCopiedLinkCommand))Dispatcher.UIThread.Post(()=>window.AcceptLaunch([MainWindow.OpenCopiedLinkCommand]),DispatcherPriority.Background);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
