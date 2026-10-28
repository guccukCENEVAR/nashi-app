using System.Runtime.InteropServices;
namespace Nashi.Linux;

/// The few Xlib calls Nashi needs for its desktop gestures. Every connection is
/// private to one thread and opened only in X11 sessions (or XWayland windows).
internal static class X11
{
    internal const uint ShiftMask=1,LockMask=2,ControlMask=4,Mod1Mask=8,Mod2Mask=16,Button1Mask=256;
    internal static bool IsX11Session=>string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),"x11",StringComparison.OrdinalIgnoreCase)||string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
    internal static bool Available=>!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    internal sealed class Connection:IDisposable
    {
        internal nint Display{get;private set;}
        internal nint Root{get;}
        private Connection(nint display){Display=display;Root=XDefaultRootWindow(display);}
        internal static Connection? TryOpen()
        {
            if(!Available)return null;
            try{var display=XOpenDisplay(null);return display==0?null:new Connection(display);}
            catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException){return null;}
        }
        private nint dndSelection;
        /// True while some app drives an XDND drag: the source owns XdndSelection from drag start.
        internal bool DragInProgress
        {
            get
            {
                if(Display==0)return false;if(dndSelection==0)dndSelection=XInternAtom(Display,"XdndSelection",0);
                return XGetSelectionOwner(Display,dndSelection)!=0;
            }
        }
        internal bool Pointer(out int x,out int y,out uint mask)
        {
            x=y=0;mask=0;if(Display==0)return false;
            return XQueryPointer(Display,Root,out _,out _,out x,out y,out _,out _,out mask)!=0;
        }
        public void Dispose(){if(Display==0)return;XCloseDisplay(Display);Display=0;}
    }

    // XKeyEvent fields followed by padding to the 192-byte XEvent union.
    [StructLayout(LayoutKind.Sequential)]internal struct Event{public int Type;private readonly int pad;public nint Serial,SendEvent,Display,Window,Root,Subwindow,Time;public int X,Y,XRoot,YRoot;public uint State,Keycode;private readonly long r0,r1,r2,r3,r4,r5,r6,r7,r8,r9,r10,r11,r12;}
    private const string Library="libX11.so.6";
    [DllImport(Library)]internal static extern nint XOpenDisplay(string? name);
    [DllImport(Library)]internal static extern int XCloseDisplay(nint display);
    [DllImport(Library)]internal static extern nint XDefaultRootWindow(nint display);
    [DllImport(Library)]internal static extern int XQueryPointer(nint display,nint window,out nint root,out nint child,out int rootX,out int rootY,out int winX,out int winY,out uint mask);
    [DllImport(Library)]internal static extern nint XInternAtom(nint display,string name,int onlyIfExists);
    [DllImport(Library)]internal static extern nint XGetSelectionOwner(nint display,nint selection);
    [DllImport(Library)]internal static extern byte XKeysymToKeycode(nint display,nuint keysym);
    [DllImport(Library)]internal static extern int XGrabKey(nint display,int keycode,uint modifiers,nint window,int ownerEvents,int pointerMode,int keyboardMode);
    [DllImport(Library)]internal static extern int XUngrabKey(nint display,int keycode,uint modifiers,nint window);
    [DllImport(Library)]internal static extern int XPending(nint display);
    [DllImport(Library)]internal static extern int XNextEvent(nint display,out Event e);
    [DllImport(Library)]internal static extern int XSync(nint display,int discard);
}
