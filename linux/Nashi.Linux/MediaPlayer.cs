using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Nashi.Core;
namespace Nashi.Linux;

/// Preview playback built from the same FFmpeg Nashi exports with: video frames are decoded
/// to BGRA and drawn into a bitmap, audio is streamed to the sound server, and the audio
/// clock paces the frames. Seeking restarts decoding at the requested time.
internal sealed class MediaPlayer:IDisposable
{
    private readonly Toolchain tools;private readonly object gate=new();
    private string? path;private MediaInfo? info;private int width,height;private double frameRate=30;
    private WriteableBitmap? bitmap;private CancellationTokenSource? session;private long generation;
    private double origin;private double speed=1;private bool playing;
    private readonly Stopwatch clock=new();private long audioBytes;private PulseAudio? audio;private Task? audioTask;private bool audioEnded;private double audioOffset;
    private int pendingFrame;
    internal Image View{get;}=new(){Stretch=Stretch.Uniform};
    internal double Duration=>info?.Duration??0;
    internal bool Playing{get{lock(gate)return playing;}}
    internal bool HasVideo=>info?.Video is not null;
    internal PixelSize VideoSize=>new(width,height);
    internal event Action? Ended;
    internal event Action<string>? Failed;
    internal MediaPlayer(Toolchain tools)=>this.tools=tools;
    internal double Speed{get=>speed;set{var next=Math.Clamp(double.IsFinite(value)?value:1,.05,32);if(Math.Abs(next-speed)<1e-9)return;var resume=Playing;var at=Position;speed=next;if(resume)Start(at);}}
    internal double Position
    {
        get
        {
            lock(gate)
            {
                if(!playing)return origin;
                // The sound card's position is the master clock; after the audio ends a stopwatch continues it.
                double elapsed;
                if(audio is not null&&!audioEnded)elapsed=Math.Max(0,audioBytes/(double)(PulseAudio.Rate*PulseAudio.BytesPerFrame)-audio.Latency.TotalSeconds);
                else elapsed=audioOffset+clock.Elapsed.TotalSeconds;
                return Math.Min(Duration>0?Duration:double.MaxValue,origin+elapsed*speed);
            }
        }
    }
    internal async Task OpenAsync(string file,MediaInfo? known=null,CancellationToken token=default)
    {
        Stop();path=file;info=known??await new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).InspectAsync(file,token);origin=0;
        if(info.Video is {} video)
        {
            var sourceWidth=Math.Max(2,video.Width??1280);var sourceHeight=Math.Max(2,video.Height??720);var scale=Math.Min(1,Math.Min(1280d/sourceWidth,720d/sourceHeight));
            width=Math.Max(2,(int)Math.Round(sourceWidth*scale/2)*2);height=Math.Max(2,(int)Math.Round(sourceHeight*scale/2)*2);frameRate=Math.Clamp(video.FrameRate,1,60);
            bitmap=new WriteableBitmap(new PixelSize(width,height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Opaque);View.Source=bitmap;
            await ShowFrameAsync(0,token);
        }
        else{width=height=0;bitmap=null;View.Source=null;}
    }
    internal void Play(){if(path is null||Playing)return;var at=origin>=Duration-.05&&Duration>0?0:origin;Start(at);}
    internal void Pause(){var at=Position;Stop();origin=at;}
    internal void Toggle(){if(Playing)Pause();else Play();}
    internal void Seek(double seconds)
    {
        var target=Math.Clamp(double.IsFinite(seconds)?seconds:0,0,Math.Max(0,Duration));
        if(Playing){Start(target);return;}
        origin=target;if(HasVideo)_ = ShowFrameAsync(target,CancellationToken.None);
    }
    private void Start(double at)
    {
        Stop();if(path is null||info is null)return;
        var current=new CancellationTokenSource();var version=Interlocked.Increment(ref generation);
        lock(gate){session=current;origin=at;playing=true;audioBytes=0;audioEnded=false;audioOffset=0;clock.Restart();audio=info.HasAudio?PulseAudio.TryOpen("Nashi preview"):null;}
        var output=audio;var token=current.Token;var file=path;
        // Loops always run to completion so they release their decoder and sound stream themselves.
        if(output is not null)audioTask=Task.Run(()=>AudioLoop(file,at,output,token));
        if(HasVideo)_ = Task.Run(()=>VideoLoop(file,at,version,token));
        else _ = Task.Run(()=>WaitForEnd(version,token));
    }
    private void Stop()
    {
        CancellationTokenSource? old;PulseAudio? output;Task? loop;
        lock(gate){old=session;session=null;output=audio;audio=null;loop=audioTask;audioTask=null;playing=false;clock.Stop();}
        old?.Cancel();old?.Dispose();
        // Free the sound stream only after its loop has stopped writing to it.
        if(output is not null)(loop??Task.CompletedTask).ContinueWith(_=>output.Dispose(),TaskScheduler.Default);
    }
    private List<string> Arguments(string file,double at,bool video)
    {
        var arguments=new List<string>{"-hide_banner","-nostdin","-v","error","-protocol_whitelist","file,pipe","-ss",at.ToString("0.###",CultureInfo.InvariantCulture),"-i",file};
        var tempo=speed==1?"":Tempo(speed);
        if(video)arguments.AddRange(["-map","0:v:0","-an","-vf",$"scale={width}:{height}:flags=bilinear{(speed==1?"":$",setpts=PTS/{speed.ToString(CultureInfo.InvariantCulture)}")},fps={frameRate.ToString("0.###",CultureInfo.InvariantCulture)}","-f","rawvideo","-pix_fmt","bgra","-"]);
        else{arguments.AddRange(["-map","0:a:0","-vn"]);if(tempo.Length>0)arguments.AddRange(["-af",tempo]);arguments.AddRange(["-f","s16le","-ac","2","-ar","48000","-"]);}
        return arguments;
    }
    private static string Tempo(double speed){var parts=new List<string>();while(speed>2){parts.Add("atempo=2");speed/=2;}while(speed<.5){parts.Add("atempo=0.5");speed*=2;}parts.Add("atempo="+speed.ToString("0.####",CultureInfo.InvariantCulture));return string.Join(',',parts);}
    private Process Launch(List<string> arguments)
    {
        var start=new ProcessStartInfo(tools.Executable("ffmpeg")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,CreateNoWindow=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        var process=Process.Start(start)??throw new IOException("The preview decoder could not start.");process.StandardInput.Close();_ = process.StandardError.ReadToEndAsync();return process;
    }
    private static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}process.Dispose();}
    private void AudioLoop(string file,double at,PulseAudio output,CancellationToken token)
    {
        Process? process=null;
        try
        {
            process=Launch(Arguments(file,at,video:false));var stream=process.StandardOutput.BaseStream;var buffer=new byte[4096];
            while(!token.IsCancellationRequested)
            {
                var read=stream.ReadAtLeast(buffer,buffer.Length,throwOnEndOfStream:false);if(read<=0)break;read-=read%PulseAudio.BytesPerFrame;
                if(!output.Write(buffer.AsSpan(0,read)))break;lock(gate)if(!token.IsCancellationRequested)audioBytes+=read;
            }
            if(!token.IsCancellationRequested)output.Drain();
        }
        catch(Exception error)when(error is IOException or ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        finally
        {
            if(process is not null)Kill(process);
            if(token.IsCancellationRequested)output.Flush();
            else lock(gate)if(ReferenceEquals(audio,output)){audioOffset=audioBytes/(double)(PulseAudio.Rate*PulseAudio.BytesPerFrame);audioEnded=true;clock.Restart();}
        }
    }
    private void VideoLoop(string file,double at,long version,CancellationToken token)
    {
        Process? process=null;var frameBytes=width*height*4;var buffer=new byte[frameBytes];long index=0;
        try
        {
            process=Launch(Arguments(file,at,video:true));var stream=process.StandardOutput.BaseStream;
            while(!token.IsCancellationRequested)
            {
                if(stream.ReadAtLeast(buffer,frameBytes,throwOnEndOfStream:false)<frameBytes)break;
                var due=at+index++/frameRate*speed;
                while(!token.IsCancellationRequested&&Position+0.002<due)Thread.Sleep(Math.Clamp((int)((due-Position)/speed*1000),1,20));
                if(token.IsCancellationRequested)break;
                if(Position-due>2/frameRate*speed)continue;// late: drop the frame
                Present(buffer,version);
            }
        }
        catch(Exception error)when(error is IOException or ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception){Dispatcher.UIThread.Post(()=>Failed?.Invoke(error.Message));}
        finally{if(process is not null)Kill(process);}
        if(!token.IsCancellationRequested)WaitForEnd(version,token);
    }
    private void WaitForEnd(long version,CancellationToken token)
    {
        while(!token.IsCancellationRequested&&Position<Duration-.02)Thread.Sleep(30);
        if(token.IsCancellationRequested||Interlocked.Read(ref generation)!=version)return;
        Dispatcher.UIThread.Post(()=>{if(Interlocked.Read(ref generation)!=version)return;Stop();origin=Duration;Ended?.Invoke();});
    }
    private void Present(byte[] frame,long version)
    {
        if(Interlocked.Exchange(ref pendingFrame,1)==1)return;// the UI has not shown the previous frame yet
        var copy=(byte[])frame.Clone();
        Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref pendingFrame,0);if(Interlocked.Read(ref generation)==version)Draw(copy);},DispatcherPriority.Render);
    }
    private void Draw(byte[] frame)
    {
        if(bitmap is null)return;
        using(var target=bitmap.Lock())
        {
            var row=width*4;for(var y=0;y<height;y++)System.Runtime.InteropServices.Marshal.Copy(frame,y*row,target.Address+y*target.RowBytes,row);
        }
        View.InvalidateVisual();
    }
    private async Task ShowFrameAsync(double at,CancellationToken token)
    {
        if(path is null||!HasVideo)return;var version=Interlocked.Increment(ref generation);
        var arguments=Arguments(path,at,video:true);arguments.InsertRange(arguments.Count-5,["-frames:v","1"]);
        var frameBytes=width*height*4;
        try
        {
            var frame=await Task.Run(()=>
            {
                var process=Launch(arguments);try{var buffer=new byte[frameBytes];var read=process.StandardOutput.BaseStream.ReadAtLeast(buffer,frameBytes,throwOnEndOfStream:false);return read==frameBytes?buffer:null;}finally{Kill(process);}
            },token);
            if(frame is not null&&Interlocked.Read(ref generation)==version)Draw(frame);
        }
        catch(Exception error)when(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException){}
    }
    public void Dispose(){Interlocked.Increment(ref generation);Stop();View.Source=null;bitmap?.Dispose();bitmap=null;}
}
