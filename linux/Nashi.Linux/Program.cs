using Avalonia;
namespace Nashi.Linux;

internal static class Program
{
    internal static string[] Arguments{get;private set;}=[];
    internal static SingleInstance? Instance{get;private set;}

    [STAThread]
    public static int Main(string[] args)
    {
        Arguments=args;
        if(args.Contains("--version")){Console.WriteLine("Nashi "+Updates.Current);return 0;}
        if(!args.Any(a=>a is "--ui-audit" or "--smoke-test"))
        {
            Instance=new SingleInstance();
            if(!Instance.Primary)
            {
                var paths=args.Contains(MainWindow.OpenCopiedLinkCommand)?[MainWindow.OpenCopiedLinkCommand]:args.Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).ToArray();
                try{Instance.Notify(paths).GetAwaiter().GetResult();return 0;}
                catch(Exception error)when(error is IOException or System.Net.Sockets.SocketException or OperationCanceledException or UnauthorizedAccessException)
                {Console.Error.WriteLine("Nashi is already running but is still starting. Open it from the system tray or try again in a moment.");return 1;}
                finally{Instance.Dispose();}
            }
        }
        try{return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args,Avalonia.Controls.ShutdownMode.OnExplicitShutdown);}
        finally{Instance?.Dispose();}
    }

    // Avalonia's designer and tooling look for this method by name.
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UseX11().UseSkia().With(new X11PlatformOptions{WmClass="nashi"}).LogToTrace();
}
