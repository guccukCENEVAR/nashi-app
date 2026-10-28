using System.Buffers.Binary;
using System.Globalization;
using Avalonia.Input;
namespace Nashi.Linux;

/// Drag chords on Wayland, which keeps other apps' input private: with the user's opt-in and
/// membership of the `input` group, read Shift, Ctrl, Alt and the left button straight from
/// the input devices. Every other event is discarded on arrival; nothing is stored or logged.
internal sealed class EvdevInput:IDisposable
{
    internal enum Access{Ready,NeedsPermission,NoDevices}
    internal readonly record struct Device(string Path,bool Keyboard,bool Pointer);
    private const ushort KeyEvent=1,RelativeEvent=2,AbsoluteEvent=3;
    private const ushort LeftShift=42,RightShift=54,LeftCtrl=29,RightCtrl=97,LeftAlt=56,RightAlt=100,ButtonLeft=272;
    // struct input_event on 64-bit Linux: 16-byte timeval, then type, code and value.
    private const int EventSize=24;
    // Settable only so the smoke test can stand in fake devices.
    internal static string DevicesFile{get;set;}="/proc/bus/input/devices";
    internal static string DeviceDirectory{get;set;}="/dev/input";
    private static readonly TimeSpan RescanInterval=TimeSpan.FromSeconds(3);

    internal static string MarkerPath=>Path.Combine(MainWindow.DataDirectory,"wayland-drag-shortcuts");
    internal static bool Enabled=>File.Exists(MarkerPath);
    internal static void SetEnabled(bool on)
    {
        if(on){Directory.CreateDirectory(MainWindow.DataDirectory);File.WriteAllText(MarkerPath,"");}else File.Delete(MarkerPath);
    }
    internal static Access Check()
    {
        var devices=Parse(ReadDevices());
        if(!devices.Any(d=>d.Keyboard)||!devices.Any(d=>d.Pointer))return Access.NoDevices;
        foreach(var device in devices.Where(d=>d.Keyboard))
        {
            try{using var probe=new FileStream(device.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,1);return Access.Ready;}
            catch(UnauthorizedAccessException){return Access.NeedsPermission;}
            catch(IOException){}
        }
        return Access.NoDevices;
    }

    private readonly object gate=new();private readonly HashSet<string> open=[];
    private volatile bool disposed;private DateTime nextScan=DateTime.MinValue;
    // Indexed by key code; written by reader threads, read by the UI poll.
    private readonly bool[] keys=new bool[ButtonLeft+1];private int motion;
    internal bool ButtonDown=>Volatile.Read(ref keys[ButtonLeft]);
    internal int MotionSincePress=>Volatile.Read(ref motion);
    internal KeyModifiers Modifiers=>
        (Volatile.Read(ref keys[LeftCtrl])||Volatile.Read(ref keys[RightCtrl])?KeyModifiers.Control:KeyModifiers.None)|
        (Volatile.Read(ref keys[LeftShift])||Volatile.Read(ref keys[RightShift])?KeyModifiers.Shift:KeyModifiers.None)|
        (Volatile.Read(ref keys[LeftAlt])||Volatile.Read(ref keys[RightAlt])?KeyModifiers.Alt:KeyModifiers.None);

    /// Opens newly attached keyboards and pointers; cheap enough to call from a UI timer.
    internal void Refresh()
    {
        if(disposed||DateTime.UtcNow<nextScan)return;nextScan=DateTime.UtcNow+RescanInterval;
        foreach(var device in Parse(ReadDevices()).Where(d=>d.Keyboard||d.Pointer))
        {
            lock(gate){if(!open.Add(device.Path))continue;}
            new Thread(()=>Read(device.Path)){IsBackground=true,Name="Nashi input"}.Start();
        }
    }
    private void Read(string path)
    {
        try
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite,1);
            var buffer=new byte[EventSize*64];
            while(!disposed)
            {
                var count=stream.Read(buffer,0,buffer.Length);if(count<=0)break;
                for(var offset=0;offset+EventSize<=count;offset+=EventSize)Handle(buffer.AsSpan(offset+16,8));
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ObjectDisposedException){}
        finally
        {
            lock(gate)open.Remove(path);
            // A device that vanished mid-press must not leave a key stuck down.
            Array.Clear(keys);Volatile.Write(ref motion,0);
        }
    }
    private void Handle(ReadOnlySpan<byte> data)
    {
        var type=BinaryPrimitives.ReadUInt16LittleEndian(data);var code=BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);var value=BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if(type==KeyEvent)
        {
            if(code==ButtonLeft){if(value==2)return;if(value==1)Volatile.Write(ref motion,0);Volatile.Write(ref keys[ButtonLeft],value==1);}
            else if(code is LeftShift or RightShift or LeftCtrl or RightCtrl or LeftAlt or RightAlt)Volatile.Write(ref keys[code],value!=0);
        }
        // Mice report relative motion; touchpads and tablets report absolute positions.
        else if((type==RelativeEvent&&code<=1)||(type==AbsoluteEvent&&code is 0 or 1 or 53 or 54))
        {
            if(Volatile.Read(ref keys[ButtonLeft]))Interlocked.Increment(ref motion);
        }
    }
    public void Dispose(){disposed=true;}

    private static string ReadDevices(){try{return File.ReadAllText(DevicesFile);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){return "";}}
    /// Reads /proc/bus/input/devices: a keyboard has letter and Shift keys, a pointer has a left button.
    internal static IReadOnlyList<Device> Parse(string text)
    {
        var devices=new List<Device>();
        foreach(var block in text.Split("\n\n",StringSplitOptions.RemoveEmptyEntries))
        {
            string? handler=null;string[] keyWords=[];
            foreach(var line in block.Split('\n'))
            {
                if(line.StartsWith("H: Handlers=",StringComparison.Ordinal))handler=line[12..].Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(h=>h.StartsWith("event",StringComparison.Ordinal)&&h[5..].All(char.IsAsciiDigit)&&h.Length>5);
                else if(line.StartsWith("B: KEY=",StringComparison.Ordinal))keyWords=line[7..].Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
            }
            if(handler is null)continue;
            var keyboard=HasBit(keyWords,30)&&HasBit(keyWords,LeftShift);var pointer=HasBit(keyWords,ButtonLeft);
            if(keyboard||pointer)devices.Add(new(Path.Combine(DeviceDirectory,handler),keyboard,pointer));
        }
        return devices;
    }
    // The bitmap is printed as 64-bit hex words, most significant first.
    private static bool HasBit(string[] words,int bit)
    {
        var index=words.Length-1-bit/64;
        return index>=0&&ulong.TryParse(words[index],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var word)&&(word>>(bit%64)&1)==1;
    }
    /// Replays raw input_event records, for tests.
    internal static EvdevInput Replay(ReadOnlySpan<byte> events)
    {
        var input=new EvdevInput{disposed=true};
        for(var offset=0;offset+EventSize<=events.Length;offset+=EventSize)input.Handle(events.Slice(offset+16,8));
        return input;
    }
}
