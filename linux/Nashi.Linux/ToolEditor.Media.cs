using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class ToolEditor
{
    private readonly CheckBox trimSilence=new(){Margin=new(4,12)},joinClips=new(){Margin=new(4,12)};
    private readonly ComboBox visualization=L10n.Choices(["waveform","spectrum","bars","image"]),redactMode=L10n.Choices(["solid","blur","pixelate"]),bleepMode=L10n.Choices(["bleep","mute"]),sampleDepth=L10n.Choices(["matchSource","pcm16","pcm24","pcm32","float32"]);
    private readonly List<MediaRange> bleepRanges=[];private readonly List<MediaRegion> videoRegions=[];private readonly ListBox rangesList=new(){MaxHeight=140};private string? visualImage;private bool playingRange;
    private readonly CancellationTokenSource previewLifetime=new();private Toolchain? previewTools;private string? previewDirectory;
    private Button? processedButton;private bool processingPreview,processedPlayback;
    internal string? ProcessedPreviewPath{get;private set;}
    internal bool ProcessedPlayback=>processedPlayback;
    private void BuildMediaOptions()
    {
        L10n.Bind(trimSilence,ContentControl.ContentProperty,"Remove silence at the beginning and end");L10n.Bind(joinClips,ContentControl.ContentProperty,"Join retained clips into one video");
        if(media is null)return;
        if(tool is "trimAudio" or "trimVideo")options.Children.Add(trimSilence);
        if(tool=="normalizeAudio")Field("Loudness range",11);
        if(tool=="splitVideo"){TextField("Split points (seconds; separated by semicolons)","");TextField("Remove clips (numbers; separated by semicolons)","");options.Children.Add(joinClips);}
        if(tool=="audioToVideo")
        {
            options.Children.Add(Ui.Text("Visualization",13));options.Children.Add(visualization);
            options.Children.Add(Ui.Button("Choose visualization image…",async()=>{var picked=await Dialogs.PickFiles(this,"Choose visualization image…",false,Dialogs.Images);if(picked.Length>0){visualImage=picked[0];L10n.Select(visualization,"image");}}));
        }
        if(tool=="redactAudio")
        {
            options.Children.Add(Ui.Text("Replace selected audio with",13));options.Children.Add(bleepMode);
            options.Children.Add(Ui.Button("Add selected range",async()=>{try{var a=Value("Start");var b=Value("End",duration);if(a<0||b<=a||b>duration)throw new ArgumentException("Choose a valid range first.");bleepRanges.Add(new(a,b));ReloadRanges();}catch(ArgumentException error){await Dialogs.Info(this,error.Message,"Check selected range");}}));
            options.Children.Add(rangesList);options.Children.Add(Ui.Button("Remove selected range",()=>{if(rangesList.SelectedIndex is var i&&i>=0&&i<bleepRanges.Count){bleepRanges.RemoveAt(i);ReloadRanges();}}));
        }
        if(tool=="redactVideo")
        {
            options.Children.Add(Ui.Text("Cover style",13));options.Children.Add(redactMode);options.Children.Add(rangesList);
            options.Children.Add(Ui.Button("Remove selected area",()=>{var i=rangesList.SelectedIndex;if(i>=0&&i<videoRegions.Count){videoRegions.RemoveAt(i);var shape=drawing.Children.OfType<Rectangle>().ElementAtOrDefault(i);if(shape is not null)drawing.Children.Remove(shape);ReloadRanges();}}));
        }
    }
    private void ReloadRanges()=>rangesList.ItemsSource=tool=="redactAudio"?bleepRanges.Select((r,i)=>$"{i+1} · {r.Start:0.###}–{r.End:0.###} s").ToArray():videoRegions.Select((r,i)=>$"{i+1} · {r.Width:0}×{r.Height:0} · {r.Start:0.###}–{r.End:0.###} s").ToArray();
    private void PlayRange(){if(player is null)return;var a=Value("Start");var b=Value("End",duration);if(a<0||b<=a||b>duration)throw new ArgumentException("Choose a valid range.");player.Seek(a);playingRange=true;player.Play();}
    private MediaOptions MoreMediaSettings(MediaOptions original)
    {
        double[] Numbers(string name)=>fields.TryGetValue(name,out var field)&&!string.IsNullOrWhiteSpace(field.Text)?field.Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s.Trim(),CultureInfo.CurrentCulture)).ToArray():[];
        var target=Value("Target size (KB; 0 = auto)");var reduction=Value("Reduction (%; 0 = auto)");var maxHeight=(int)Value("Maximum video height (0 = original)");
        return original with
        {
            AudioBitrate=(int)Value("Audio bitrate (kbps)",256),SampleDepth=L10n.Selected(sampleDepth)??"matchSource",GifFrameRate=(int)Value("GIF frames per second",15),GifWidth=(int)Value("GIF width",960),Height=tool=="convert"&&maxHeight>0?maxHeight:original.Height,
            PreserveMetadata=preserveMetadata.IsChecked==true,TargetBytes=target>0?(long)(target*1024):null,ReductionPercent=reduction>0?reduction:null,TrimSilence=trimSilence.IsChecked==true,LoudnessRange=Value("Loudness range",11),
            SplitPoints=Numbers("Split points (seconds; separated by semicolons)"),DiscardedClips=Numbers("Remove clips (numbers; separated by semicolons)").Select(n=>(int)n-1).ToArray(),JoinSplitClips=joinClips.IsChecked==true,
            Visualization=L10n.Selected(visualization)??"waveform",VisualizationImage=visualImage,RedactionMode=tool=="redactAudio"?L10n.Selected(bleepMode)??"bleep":L10n.Selected(redactMode)??"solid",
            AudioRedactions=tool=="redactAudio"&&bleepRanges.Count>0?bleepRanges.ToArray():original.AudioRedactions,Regions=tool=="redactVideo"&&videoRegions.Count>0?videoRegions.ToArray():original.Regions
        };
    }
    /// Opens the preview player and the filmstrip or waveform once FFmpeg is available.
    internal void ConfigureMediaRuntime(Toolchain tools)
    {
        previewTools=tools;if(media is null||!tools.Ready)return;
        Opened+=async(_,_)=>
        {
            try
            {
                var created=new MediaPlayer(tools);player=created;created.Ended+=()=>{playingRange=false;UpdateTime();};created.Failed+=message=>time.Text=L10n.Text("Preview unavailable: "+message);
                await created.OpenAsync(input,media,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;
                if(mediaHost is not null)mediaHost.Child=created.HasVideo?created.View:Ui.Text("♫",48,"Muted",localize:false).Also(t=>{t.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center;t.Foreground=Avalonia.Media.Brushes.White;});
                UpdateTime();
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is IOException or InvalidDataException or FileNotFoundException or System.ComponentModel.Win32Exception){time.Text=L10n.Text("Preview unavailable: "+error.Message);}
            await LoadTimelineArtwork();
        };
    }
    private string PreviewDirectory()=>previewDirectory??=Directory.CreateTempSubdirectory("Nashi-preview-").FullName;
    private async Task LoadTimelineArtwork()
    {
        if(previewTools is null||media is null)return;
        try
        {
            if(media.Video is not null)
            {
                var frames=new List<Avalonia.Media.Imaging.Bitmap>();
                for(var n=0;n<10;n++)
                {
                    previewLifetime.Token.ThrowIfCancellationRequested();var frame=Path.Combine(PreviewDirectory(),$"frame-{n}.png");
                    await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-ss",(Math.Max(0,duration-.001)*n/10).ToString(CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-vf","scale=160:-1",frame],previewLifetime.Token);
                    frames.Add(new Avalonia.Media.Imaging.Bitmap(frame));
                }
                if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork(frames.ToArray());
            }
            else
            {
                var frame=Path.Combine(PreviewDirectory(),"waveform.png");
                await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-i",input,"-filter_complex","aformat=channel_layouts=mono,showwavespic=s=1200x140:colors=0xe5bc59","-frames:v","1",frame],previewLifetime.Token);
                if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork([new Avalonia.Media.Imaging.Bitmap(frame)]);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or ArgumentException or FileNotFoundException){if(IsVisible)ToolTip.SetTip(timeline,L10n.Text("Timeline preview unavailable: "+error.Message));}
    }
    private void DeletePreviewFiles()
    {
        if(previewDirectory is null)return;var path=previewDirectory;
        _ = Task.Run(async()=>{for(var n=0;n<10;n++){try{if(Directory.Exists(path))Directory.Delete(path,true);return;}catch(IOException){await Task.Delay(200);}catch(UnauthorizedAccessException){return;}}});
    }
    private async void PlayOriginalPreview()
    {
        if(processingPreview||media is null||player is null)return;processedPlayback=false;drawing.IsEnabled=true;drawing.IsVisible=true;duration=media.Duration;scrubber.Maximum=duration;
        try{await player.OpenAsync(input,media,previewLifetime.Token);player.Play();}catch(Exception error)when(error is IOException or OperationCanceledException or InvalidDataException){}
    }
    private async Task PreviewProcessed()
    {
        if(processingPreview||previewTools?.Ready!=true||player is null){L10n.SetText(time,"Install the media tools to preview the processed result.");return;}
        if(!await ReadSettings(false))return;processingPreview=true;if(processedButton is not null){processedButton.IsEnabled=false;L10n.Bind(processedButton,ContentControl.ContentProperty,"Preparing…");}L10n.SetText(time,"Preparing processed preview…");
        try
        {
            var engine=new MediaEngine(previewTools.Executable("ffmpeg"),previewTools.Executable("ffprobe"));var settings=tool=="splitVideo"?MediaOptions with{JoinSplitClips=true}:MediaOptions;
            var rendered=(await engine.ToolAsync(tool,[input],PreviewDirectory(),settings,null,previewLifetime.Token))[0];
            var playable=(await engine.ConvertAsync([rendered],media?.Video is null&&tool!="audioToVideo"?"wav":"mp4",PreviewDirectory(),new(){PreserveMetadata=false},null,previewLifetime.Token))[0];
            var info=await engine.InspectAsync(playable,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;
            ProcessedPreviewPath=playable;await player.OpenAsync(playable,info,previewLifetime.Token);
            if(mediaHost is not null&&player.HasVideo)mediaHost.Child=player.View;
            duration=info.Duration;scrubber.Maximum=duration;processedPlayback=true;drawing.IsEnabled=false;drawing.IsVisible=false;player.Play();L10n.SetText(time,"Processed preview · export uses the original file.");
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or ArgumentException or InvalidDataException or FileNotFoundException){if(IsVisible)time.Text=L10n.Text("Preview unavailable: "+error.Message);}
        finally{processingPreview=false;if(processedButton is not null){processedButton.IsEnabled=true;L10n.Bind(processedButton,ContentControl.ContentProperty,ProcessedLabel);}}
    }
}
