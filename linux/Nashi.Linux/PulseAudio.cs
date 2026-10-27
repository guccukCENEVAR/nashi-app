using System.Runtime.InteropServices;
namespace Nashi.Linux;

/// Minimal playback through libpulse-simple, served by PipeWire (pipewire-pulse) or PulseAudio.
/// Audio is optional: when the library or a sound server is missing, callers continue silently.
internal sealed class PulseAudio:IDisposable
{
    internal const int Rate=48000,Channels=2,BytesPerFrame=4;
    private nint stream;
    private PulseAudio(nint stream)=>this.stream=stream;
    internal static PulseAudio? TryOpen(string name,int rate=Rate,int channels=Channels,double latencySeconds=.08)
    {
        try
        {
            var spec=new SampleSpec{Format=3,Rate=(uint)rate,Channels=(byte)channels};// PA_SAMPLE_S16LE
            var target=(uint)(rate*channels*2*latencySeconds);
            var attributes=new BufferAttributes{MaxLength=uint.MaxValue,TargetLength=target,Prebuffer=uint.MaxValue,MinimumRequest=uint.MaxValue,FragmentSize=uint.MaxValue};
            var handle=Native.pa_simple_new(null,"Nashi",1,null,name,ref spec,0,ref attributes,out _);
            return handle==0?null:new PulseAudio(handle);
        }
        catch(Exception error)when(error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){return null;}
    }
    internal bool Write(ReadOnlySpan<byte> data)
    {
        if(stream==0)return false;
        unsafe{fixed(byte* pointer=data)return Native.pa_simple_write(stream,(nint)pointer,(nuint)data.Length,out _)==0;}
    }
    internal TimeSpan Latency=>stream==0?TimeSpan.Zero:TimeSpan.FromMicroseconds(Native.pa_simple_get_latency(stream,out _));
    internal void Drain(){if(stream!=0)Native.pa_simple_drain(stream,out _);}
    internal void Flush(){if(stream!=0)Native.pa_simple_flush(stream,out _);}
    public void Dispose(){if(stream==0)return;Native.pa_simple_free(stream);stream=0;}

    /// Plays a 16-bit PCM WAV once, on a background thread.
    internal static void PlayWave(byte[] wave,double volume)
    {
        if(!TryParseWave(wave,out var rate,out var channels,out var offset,out var length))return;
        var samples=wave.AsSpan(offset,length).ToArray();
        for(var i=0;i+1<samples.Length;i+=2){var value=(short)(samples[i]|samples[i+1]<<8);value=(short)Math.Clamp(value*volume,short.MinValue,short.MaxValue);samples[i]=(byte)value;samples[i+1]=(byte)(value>>8);}
        _ = Task.Run(()=>{using var output=TryOpen("Nashi feedback",rate,channels,.05);if(output is null)return;output.Write(samples);output.Drain();});
    }
    private static bool TryParseWave(byte[] data,out int rate,out int channels,out int offset,out int length)
    {
        rate=channels=offset=length=0;
        if(data.Length<44||!data.AsSpan(0,4).SequenceEqual("RIFF"u8)||!data.AsSpan(8,4).SequenceEqual("WAVE"u8))return false;
        var position=12;var pcm=false;
        while(position+8<=data.Length)
        {
            var size=BitConverter.ToInt32(data,position+4);if(size<0||position+8+(long)size>data.Length)return false;var id=data.AsSpan(position,4);
            if(id.SequenceEqual("fmt "u8)&&size>=16){pcm=BitConverter.ToInt16(data,position+8)==1&&BitConverter.ToInt16(data,position+22)==16;channels=BitConverter.ToInt16(data,position+10);rate=BitConverter.ToInt32(data,position+12);}
            else if(id.SequenceEqual("data"u8)){offset=position+8;length=size;return pcm&&channels is 1 or 2&&rate is >=8000 and <=192000;}
            position+=8+size+(size&1);
        }
        return false;
    }
    [StructLayout(LayoutKind.Sequential)]private struct SampleSpec{public int Format;public uint Rate;public byte Channels;}
    [StructLayout(LayoutKind.Sequential)]private struct BufferAttributes{public uint MaxLength,TargetLength,Prebuffer,MinimumRequest,FragmentSize;}
    private static class Native
    {
        private const string Library="libpulse-simple.so.0";
        [DllImport(Library)]internal static extern nint pa_simple_new(string? server,string name,int direction,string? device,string streamName,ref SampleSpec spec,nint map,ref BufferAttributes attributes,out int error);
        [DllImport(Library)]internal static extern int pa_simple_write(nint stream,nint data,nuint bytes,out int error);
        [DllImport(Library)]internal static extern int pa_simple_drain(nint stream,out int error);
        [DllImport(Library)]internal static extern int pa_simple_flush(nint stream,out int error);
        [DllImport(Library)]internal static extern ulong pa_simple_get_latency(nint stream,out int error);
        [DllImport(Library)]internal static extern void pa_simple_free(nint stream);
    }
}
