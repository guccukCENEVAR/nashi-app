using Avalonia;
using Avalonia.Controls;
namespace Nashi.Linux;

/// StatusNotifierItem tray icon (KDE Plasma, and GNOME with the AppIndicator extension).
internal sealed class TrayHost:IDisposable
{
    private readonly TrayIcon icon;private readonly Func<NativeMenu> menu;private string? update;
    internal TrayHost(Action open,Func<NativeMenu> menu)
    {
        this.menu=menu;
        if(Application.Current is not {} app)throw new InvalidOperationException("The application is not running.");
        icon=new TrayIcon{Icon=Ui.Icon(),ToolTipText="Nashi",Menu=menu(),IsVisible=true};icon.Clicked+=(_,_)=>open();
        var icons=TrayIcon.GetIcons(app)??[];icons.Add(icon);TrayIcon.SetIcons(app,icons);
        LanguageState.Shared.Changed+=Rebuild;
    }
    internal void Rebuild(){icon.Menu=menu();icon.ToolTipText=update is null?"Nashi":"Nashi · "+L10n.Text("Update "+update+" available");}
    internal void UpdateAvailable(string version){update=version;Rebuild();}
    public void Dispose()
    {
        LanguageState.Shared.Changed-=Rebuild;icon.IsVisible=false;
        if(Application.Current is {} app&&TrayIcon.GetIcons(app) is {} icons)icons.Remove(icon);icon.Dispose();
    }
}
