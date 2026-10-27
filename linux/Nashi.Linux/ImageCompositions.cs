using System.Globalization;
using Nashi.Core;
using SkiaSharp;
namespace Nashi.Linux;

internal sealed record FrameOptions(int Padding=80,double CornerRadius=24,string Color="#FFFFFF",string Backdrop="solid",string GradientEnd="#FFD85C",string? BackgroundImage=null,double Blur=0,double Shadow=24,double ShadowOpacity=.25,int? Width=null,int? Height=null,double Scale=1,double X=.5,double Y=.5);
internal sealed record CollageOptions(string Layout="grid",int Columns=2,int Width=2048,int Height=2048,int Spacing=12,int Padding=16,double CornerRadius=0,string Color="#FFFFFF",bool Fill=true,int[]? Order=null);

internal static partial class ImageEngine
{
    /// Accepts #RGB, #RRGGBB and #AARRGGBB, as the WPF colour converter does for hex input.
    internal static SKColor ParseColor(string color)
    {
        var value=color.Trim();if(!value.StartsWith('#'))throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");
        var hex=value[1..];if(hex.Length==3)hex=string.Concat(hex.Select(c=>new string(c,2)));
        if(hex.Length is not (6 or 8)||!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var number))throw new ArgumentException("Choose a hex background color, such as #FFFFFF or #00FFFFFF.");
        return hex.Length==6?new SKColor(0xFF000000|number):new SKColor(number);
    }
    private static void Fitted(SKCanvas canvas,ImageFrame source,SKRect area,bool fill,double radius=0)
    {
        var scale=fill?Math.Max(area.Width/source.PixelWidth,area.Height/source.PixelHeight):Math.Min(area.Width/source.PixelWidth,area.Height/source.PixelHeight);
        var w=(float)(source.PixelWidth*scale);var h=(float)(source.PixelHeight*scale);
        canvas.Save();canvas.ClipRoundRect(new SKRoundRect(area,(float)radius,(float)radius),SKClipOperation.Intersect,true);
        using var paint=new SKPaint{FilterQuality=SKFilterQuality.High,IsAntialias=true};
        canvas.DrawBitmap(source.Bitmap,SKRect.Create(area.Left+(area.Width-w)/2,area.Top+(area.Height-h)/2,w,h),paint);canvas.Restore();
    }
    internal static ImageFrame Frame(ImageFrame image,FrameOptions o)
    {
        if(o.Padding<0||o.Padding>4000||!new[]{o.CornerRadius,o.Blur,o.Shadow,o.ShadowOpacity,o.Scale,o.X,o.Y}.All(double.IsFinite)||o.Scale<=0||o.Scale>10)throw new ArgumentException("Choose valid background and image dimensions.");
        var w=o.Width??image.PixelWidth+2*o.Padding;var h=o.Height??image.PixelHeight+2*o.Padding;
        var start=ParseColor(o.Color);var end=o.Backdrop=="gradient"?ParseColor(o.GradientEnd):start;
        ImageFrame? back=null;
        if(o.Backdrop=="image")
        {
            if(o.BackgroundImage is null)throw new ArgumentException("Choose a background image.");back=Load(o.BackgroundImage);
            if(o.Blur>0){var blurred=ImageFrame.FromPng(PhotoCodec.Adjust(back.ToPng(),new(){Blur=o.Blur}));back.Dispose();back=blurred;}
        }
        try
        {
            return Render(w,h,g=>
            {
                var bounds=SKRect.Create(0,0,w,h);
                using(var paint=new SKPaint{Color=start})
                {
                    if(o.Backdrop=="gradient")paint.Shader=SKShader.CreateLinearGradient(new SKPoint(0,0),new SKPoint(w,h),[start,end],SKShaderTileMode.Clamp);
                    g.DrawRect(bounds,paint);
                }
                if(back is not null)Fitted(g,back,bounds,true);
                var scale=Math.Min(Math.Max(1,w-2*o.Padding)/(double)image.PixelWidth,Math.Max(1,h-2*o.Padding)/(double)image.PixelHeight)*o.Scale;var iw=image.PixelWidth*scale;var ih=image.PixelHeight*scale;
                var rect=SKRect.Create((float)((w-iw)*Math.Clamp(o.X,0,1)),(float)((h-ih)*Math.Clamp(o.Y,0,1)),(float)iw,(float)ih);
                if(o.Shadow>0)
                {
                    // Eight soft layers, matching the Windows renderer's shadow falloff.
                    var spread=(float)Math.Min(100,o.Shadow);using var shadow=new SKPaint{Color=new SKColor(0,0,0,(byte)(Math.Clamp(o.ShadowOpacity,0,1)*6)),IsAntialias=true};
                    for(var n=8;n>0;n--){var r=rect;r.Inflate(spread*n/8,spread*n/8);var radius=(float)o.CornerRadius+spread*n/8;g.DrawRoundRect(r,radius,radius,shadow);}
                }
                Fitted(g,image,rect,true,o.CornerRadius);
            });
        }
        finally{back?.Dispose();}
    }
    internal static ImageFrame Collage(IReadOnlyList<string> inputs,CollageOptions o,CancellationToken token)
    {
        if(inputs.Count<2||inputs.Count>100)throw new ArgumentException("Choose 2–100 images for a collage.");var order=o.Order??Enumerable.Range(0,inputs.Count).ToArray();
        if(order.Length!=inputs.Count||!order.Order().SequenceEqual(Enumerable.Range(0,inputs.Count)))throw new ArgumentException("The image order must include every image once.");
        if(o.Columns<1||o.Columns>100||o.Padding<0||o.Spacing<0)throw new ArgumentException("Choose valid collage spacing and columns.");
        var columns=o.Layout=="row"?inputs.Count:o.Layout=="column"?1:Math.Min(o.Columns,inputs.Count);var rows=(int)Math.Ceiling(inputs.Count/(double)columns);var width=o.Width;var height=o.Height;
        var background=ParseColor(o.Color);
        return Render(width,height,g=>
        {
            using(var paint=new SKPaint{Color=background})g.DrawRect(SKRect.Create(0,0,width,height),paint);
            var cw=(width-2d*o.Padding-(columns-1)*o.Spacing)/columns;var ch=(height-2d*o.Padding-(rows-1)*o.Spacing)/rows;if(cw<=0||ch<=0)throw new ArgumentException("Reduce padding or spacing to fit the images.");
            for(var n=0;n<order.Length;n++)
            {
                token.ThrowIfCancellationRequested();SKRect cell;
                if(o.Layout=="featured")
                {
                    var availableWidth=width-2d*o.Padding;var availableHeight=height-2d*o.Padding;var largeWidth=(availableWidth-o.Spacing)*.62;var smallHeight=(availableHeight-(order.Length-2)*o.Spacing)/(order.Length-1);
                    if(largeWidth<=0||smallHeight<=0)throw new ArgumentException("Reduce spacing to fit the featured layout.");
                    cell=n==0?SKRect.Create(o.Padding,o.Padding,(float)largeWidth,(float)availableHeight):SKRect.Create((float)(o.Padding+largeWidth+o.Spacing),(float)(o.Padding+(n-1)*(smallHeight+o.Spacing)),(float)(availableWidth-largeWidth-o.Spacing),(float)smallHeight);
                }
                else cell=SKRect.Create((float)(o.Padding+n%columns*(cw+o.Spacing)),(float)(o.Padding+n/columns*(ch+o.Spacing)),(float)cw,(float)ch);
                using var image=Load(inputs[order[n]]);Fitted(g,image,cell,o.Fill,o.CornerRadius);
            }
        });
    }
    internal static ImageFrame Redact(ImageFrame image,Avalonia.Rect[] regions,string style)
    {
        if(regions.Length==0)throw new ArgumentException("Draw at least one area to redact.");
        foreach(var r in regions)if(r.Width<1||r.Height<1||r.X<0||r.Y<0||r.Right>image.PixelWidth||r.Bottom>image.PixelHeight)throw new ArgumentException("Keep redaction areas inside the image.");
        return Render(image.PixelWidth,image.PixelHeight,g=>
        {
            g.DrawBitmap(image.Bitmap,0,0);
            foreach(var r in regions)
            {
                var area=new ImageRect((int)r.X,(int)r.Y,Math.Max(1,(int)r.Width),Math.Max(1,(int)r.Height));var target=SKRect.Create(area.X,area.Y,area.Width,area.Height);
                if(style=="solid"){using var black=new SKPaint{Color=SKColors.Black};g.DrawRect(target,black);continue;}
                using var crop=Crop(image,new(area.X,area.Y,Math.Min(area.Width,image.PixelWidth-area.X),Math.Min(area.Height,image.PixelHeight-area.Y)));
                if(style=="pixelate")
                {
                    using var small=Resize(crop,Math.Max(1,area.Width/16),Math.Max(1,area.Height/16));using var nearest=new SKPaint{FilterQuality=SKFilterQuality.None,IsAntialias=false};
                    g.DrawBitmap(small.Bitmap,target,nearest);
                }
                else{using var blurred=ImageFrame.FromPng(PhotoCodec.Adjust(crop.ToPng(),new(){Blur=24}));g.DrawBitmap(blurred.Bitmap,target);}
            }
        });
    }
}
