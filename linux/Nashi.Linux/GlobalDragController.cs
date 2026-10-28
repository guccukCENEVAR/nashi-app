using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// Drag chords, as on macOS and Windows: hold Shift (convert), Ctrl (tools) or Alt
/// (file actions) while dragging files or a web link, and a drop wheel opens at the pointer.
/// X11 exposes the pointer and modifier state globally. Wayland deliberately does not: there the
/// chords come from the opt-in <see cref="EvdevInput"/> and, on KDE, KWin places the wheel under
/// the pointer (<see cref="KWinPlacement"/>); elsewhere it opens at the centre of the screen.
/// It reads only the left button and the modifier keys; it never records keystrokes.
internal sealed class GlobalDragController:IDisposable
{
    private readonly MainWindow owner;private readonly PreferencesStore preferences;private readonly Action<string,TransferContent> dropped;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly X11.Connection? pointer;private readonly HotKey? hotKey;
    private PixelPoint start;private bool down;private string? openedKind;private WheelWindow? wheel;private EvdevInput? evdev;
    private static readonly TimeSpan DropGrace=TimeSpan.FromMilliseconds(800);
    internal static bool GlobalGesturesAvailable=>X11.IsX11Session&&X11.Available;
    internal WheelWindow? ActiveWheel=>wheel;
    public GlobalDragController(MainWindow owner,PreferencesStore preferences,Action<string,TransferContent> dropped)
    {
        this.owner=owner;this.preferences=preferences;this.dropped=dropped;
        if(GlobalGesturesAvailable){pointer=X11.Connection.TryOpen();if(pointer is not null){timer.Tick+=(_,_)=>Poll();timer.Start();}hotKey=HotKey.TryRegister(()=>Dispatcher.UIThread.Post(()=>{if(preferences.Value.Enabled)owner.OpenCopiedLinkFromShortcut();}));}
        else{timer.Tick+=(_,_)=>PollWayland();timer.Start();_ = KWinPlacement.LoadAsync();}
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
        if(!pointer.Pointer(out var x,out var y,out var mask))return;var point=new PixelPoint(x,y);
        var modifiers=((mask&X11.ControlMask)!=0?KeyModifiers.Control:KeyModifiers.None)|((mask&X11.ShiftMask)!=0?KeyModifiers.Shift:KeyModifiers.None)|((mask&X11.Mod1Mask)!=0?KeyModifiers.Alt:KeyModifiers.None);
        var held=(mask&X11.Button1Mask)!=0;if(held&&!down)start=point;
        // Only a real file or link drag opens a wheel, not Shift-selecting text or Ctrl-dragging a window.
        Step(held,Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)>=10,modifiers,()=>pointer.DragInProgress,point,hiddenUntilDrag:false);
    }
    private void PollWayland()
    {
        if(!EvdevInput.Enabled){if(evdev is not null){evdev.Dispose();evdev=null;}return;}
        evdev??=new EvdevInput();evdev.Refresh();
        // Evdev cannot tell a file drag from a text selection, so the wheel stays hidden until a drag reaches it.
        Step(evdev.ButtonDown,evdev.MotionSincePress>=4,evdev.Modifiers,()=>true,ScreenCentre(),hiddenUntilDrag:true);
    }
    private void Step(bool held,bool moved,KeyModifiers modifiers,Func<bool> dragging,PixelPoint at,bool hiddenUntilDrag)
    {
        if(!preferences.Value.Enabled||owner.Busy){CloseWheel();down=false;openedKind=null;return;}
        if(!held)
        {
            // The drop reaches the wheel shortly after the button is released; give it time before closing an unused wheel.
            if(down){down=false;openedKind=null;var old=wheel;wheel=null;if(old is {IsVisible:true})DispatcherTimer.RunOnce(()=>{if(old.IsVisible)old.Close();},old.Revealed?DropGrace:TimeSpan.Zero);}
            return;
        }
        if(!down){down=true;openedKind=null;return;}
        if(!moved)return;
        // As on macOS, a different chord mid-drag swaps the wheel; releasing the chord keeps the open one.
        var kind=Kind(preferences.Value,modifiers);if(kind is null||kind==openedKind||!dragging())return;
        CloseWheel();openedKind=kind;
        wheel=WheelWindow.PresentForDrop(at,[],content=>Choices(kind,content),(choice,content)=>{if(content is not null)dropped(choice.Id,content);},hiddenUntilDrag);
    }
    /// KWin moves the wheel under the pointer; without it the wheel waits at the centre of the screen.
    private PixelPoint ScreenCentre()=>(owner.Screens.ScreenFromWindow(owner)??owner.Screens.Primary)?.Bounds.Center??new PixelPoint(0,0);
    internal static IReadOnlyList<ToolChoice> Choices(string kind,TransferContent content)
    {
        if(content.Files.Length>0)return ChoicesFor(kind,content.Files);
        return content.Payload() is {} web?WebChoices(web):[];
    }
    private void CloseWheel(){wheel?.Close();wheel=null;}
    public void Dispose(){timer.Stop();CloseWheel();pointer?.Dispose();hotKey?.Dispose();evdev?.Dispose();if(!GlobalGesturesAvailable)KWinPlacement.Unload();}

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
