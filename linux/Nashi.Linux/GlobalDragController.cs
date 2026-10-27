using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// Drag chords, as on macOS and Windows: hold Shift (convert), Ctrl (tools) or Alt
/// (file actions) while dragging files or a web link, and a drop wheel opens at the pointer.
/// X11 exposes the pointer and modifier state globally; Wayland deliberately does not, so
/// there the same chords work when dropping onto Nashi's window or a Float shelf.
/// It reads only the pointer button and modifier mask; it never records keystrokes.
internal sealed class GlobalDragController:IDisposable
{
    private readonly MainWindow owner;private readonly PreferencesStore preferences;private readonly Action<string,TransferContent> dropped;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly X11.Connection? pointer;private readonly HotKey? hotKey;
    private PixelPoint start;private bool down,opened;private WheelWindow? wheel;
    internal static bool GlobalGesturesAvailable=>X11.IsX11Session&&X11.Available;
    internal WheelWindow? ActiveWheel=>wheel;
    public GlobalDragController(MainWindow owner,PreferencesStore preferences,Action<string,TransferContent> dropped)
    {
        this.owner=owner;this.preferences=preferences;this.dropped=dropped;
        if(GlobalGesturesAvailable){pointer=X11.Connection.TryOpen();if(pointer is not null){timer.Tick+=(_,_)=>Poll();timer.Start();}hotKey=HotKey.TryRegister(()=>Dispatcher.UIThread.Post(()=>{if(preferences.Value.Enabled)owner.OpenCopiedLinkFromShortcut();}));}
    }
    internal static string Chord(KeyModifiers modifiers)
    {
        var parts=new List<string>();if(modifiers.HasFlag(KeyModifiers.Control))parts.Add("Ctrl");if(modifiers.HasFlag(KeyModifiers.Shift))parts.Add("Shift");if(modifiers.HasFlag(KeyModifiers.Alt))parts.Add("Alt");return string.Join('+',parts);
    }
    internal static string? Kind(Preferences p,KeyModifiers modifiers)
    {
        if(!p.Enabled)return null;var chord=Chord(modifiers);
        return chord==p.ToolsModifier?"tools":chord==p.ConvertModifier?"convert":chord==p.ActionsModifier?"actions":null;
    }
    internal static IReadOnlyList<ToolChoice> ChoicesFor(string kind,IReadOnlyList<string> files)=>kind switch
    {
        "convert"=>FeatureCatalog.Formats(files).Select(f=>new ToolChoice("convert:"+f,f=="gz"?"GZIP":f.ToUpperInvariant(),"↗")).ToArray(),
        "actions"=>MainWindow.FileActionChoices,
        _=>MainWindow.Choices(files)
    };
    /// Web drags offer the same downloads as the macOS web wheel.
    internal static IReadOnlyList<ToolChoice> WebChoices(WebMediaPayload web)=>web.Kind switch
    {
        "youtube"=>[new("download:video","Download Video","↓"),new("download:thumbnail","Download Thumbnail","▣"),new("download:audio","Download Audio","♫")],
        "instagramPhotos"=>[new("download:photos","Download Images","▣")],
        "instagramReel"=>[new("download:video","Download Video","↓"),new("download:audio","Download Audio","♫")],
        "image"=>[new("download:image","Download Image","↓")],
        _=>[new("download:video","Download Video","↓"),new("download:audio","Download Audio","♫")]
    };
    private void Poll()
    {
        if(pointer is null)return;
        if(!preferences.Value.Enabled||owner.Busy){CloseWheel();down=false;opened=false;return;}
        if(!pointer.Pointer(out var x,out var y,out var mask))return;var held=(mask&X11.Button1Mask)!=0;var point=new PixelPoint(x,y);
        if(!held)
        {
            if(down){down=false;opened=false;var old=wheel;wheel=null;if(old is {IsVisible:true})DispatcherTimer.RunOnce(()=>{if(old.IsVisible)old.Close();},TimeSpan.FromMilliseconds(150));}
            return;
        }
        if(!down){down=true;start=point;opened=false;return;}
        if(opened||Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<10)return;
        var modifiers=((mask&X11.ControlMask)!=0?KeyModifiers.Control:KeyModifiers.None)|((mask&X11.ShiftMask)!=0?KeyModifiers.Shift:KeyModifiers.None)|((mask&X11.Mod1Mask)!=0?KeyModifiers.Alt:KeyModifiers.None);
        var kind=Kind(preferences.Value,modifiers);if(kind is null)return;opened=true;
        wheel=WheelWindow.PresentForDrop(point,[],content=>Choices(kind,content),(choice,content)=>{if(content is not null)dropped(choice.Id,content);});
    }
    internal static IReadOnlyList<ToolChoice> Choices(string kind,TransferContent content)
    {
        if(content.Files.Length>0)return ChoicesFor(kind,content.Files);
        return content.Payload() is {} web?WebChoices(web):[];
    }
    private void CloseWheel(){wheel?.Close();wheel=null;}
    public void Dispose(){timer.Stop();CloseWheel();pointer?.Dispose();hotKey?.Dispose();}

    /// Ctrl+Shift+D everywhere on X11. Wayland sessions bind `nashi --open-copied-link` as a desktop shortcut instead.
    private sealed class HotKey:IDisposable
    {
        private readonly X11.Connection connection;private readonly int keycode;private readonly CancellationTokenSource lifetime=new();
        private static readonly uint[] LockVariants=[0,X11.LockMask,X11.Mod2Mask,X11.LockMask|X11.Mod2Mask];
        private HotKey(X11.Connection connection,int keycode,Action pressed)
        {
            this.connection=connection;this.keycode=keycode;
            foreach(var variant in LockVariants)X11.XGrabKey(connection.Display,keycode,X11.ControlMask|X11.ShiftMask|variant,connection.Root,0,1,1);
            X11.XSync(connection.Display,0);
            var thread=new Thread(()=>
            {
                while(!lifetime.IsCancellationRequested)
                {
                    while(X11.XPending(connection.Display)>0){X11.XNextEvent(connection.Display,out var e);if(e.Type==2&&e.Keycode==keycode)pressed();}
                    Thread.Sleep(50);
                }
            }){IsBackground=true,Name="Nashi shortcut"};thread.Start();
        }
        internal static HotKey? TryRegister(Action pressed)
        {
            var connection=X11.Connection.TryOpen();if(connection is null)return null;
            var keycode=X11.XKeysymToKeycode(connection.Display,0x64);// XK_d
            if(keycode==0){connection.Dispose();return null;}
            return new HotKey(connection,keycode,pressed);
        }
        public void Dispose()
        {
            lifetime.Cancel();Thread.Sleep(60);
            foreach(var variant in LockVariants)X11.XUngrabKey(connection.Display,keycode,X11.ControlMask|X11.ShiftMask|variant,connection.Root);
            connection.Dispose();
        }
    }
}
