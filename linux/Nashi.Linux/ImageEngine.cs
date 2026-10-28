using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
namespace Nashi.Linux;

/// A decoded, upright sRGB image. Owns its pixels; dispose when finished.
internal sealed class ImageFrame:IDisposable
{
    internal SKBitmap Bitmap{get;}
    public int PixelWidth=>Bitmap.Width;
    public int PixelHeight=>Bitmap.Height;
    internal ImageFrame(SKBitmap bitmap)
    {
        if(bitmap.ColorType!=SKColorType.Bgra8888||bitmap.AlphaType!=SKAlphaType.Premul)
        {
            var converted=new SKBitmap(new SKImageInfo(bitmap.Width,bitmap.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
            using(var canvas=new SKCanvas(converted)){canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(bitmap,0,0);}
            bitmap.Dispose();bitmap=converted;
        }
        Bitmap=bitmap;
    }
    public static ImageFrame FromPng(byte[] png)=>new(SKBitmap.Decode(png)??throw new InvalidDataException("This image could not be decoded."));
    public byte[] ToPng()
    {
        using var image=SKImage.FromBitmap(Bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100)??throw new InvalidDataException("This image could not be encoded.");return data.ToArray();
    }
    /// Straight (unpremultiplied) BGRA rows, as image codecs and QR decoding expect.
    internal byte[] Bgra()
    {
        using var straight=new SKBitmap(new SKImageInfo(PixelWidth,PixelHeight,SKColorType.Bgra8888,SKAlphaType.Unpremul));
        if(!Bitmap.CopyTo(straight,SKColorType.Bgra8888))using(var canvas=new SKCanvas(straight))canvas.DrawBitmap(Bitmap,0,0);
        return straight.Bytes;
    }
    internal ImageFrame Copy()=>new(Bitmap.Copy());
    internal Avalonia.Media.Imaging.Bitmap ToAvalonia()
    {
        var bitmap=new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(PixelWidth,PixelHeight),new Avalonia.Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
        using(var target=bitmap.Lock())
        {
            var rowBytes=PixelWidth*4;var source=Bitmap.GetPixels();
            for(var y=0;y<PixelHeight;y++)unsafe{Buffer.MemoryCopy((byte*)source+y*Bitmap.RowBytes,(byte*)target.Address+y*target.RowBytes,target.RowBytes,rowBytes);}
        }
        return bitmap;
    }
    public void Dispose()=>Bitmap.Dispose();
}

internal sealed record ImageRect(int X,int Y,int Width,int Height);
internal sealed record ImageOptions(int? Width=null,int? Height=null,int Rotation=0,ImageRect? Crop=null,int Quality=85,bool Horizontal=true,ImageFrame? Annotated=null)
{
    public PhotoAdjustments Adjustments{get;init;}=new();public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public Avalonia.Rect[] Redactions{get;init;}=[];
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public bool FlipHorizontal{get;init;}public bool FlipVertical{get;init;}
    public string? EncoderPath{get;init;}
}

internal static partial class ImageEngine
{
    public static readonly string[] Formats=["png","jpg","webp","heic","avif","bmp","gif","tiff","pdf"];
    /// Decodes through ImageMagick, which applies EXIF orientation and converts to sRGB for every supported format.
    public static ImageFrame Load(string file)
    {
        using var image=PhotoCodec.Read(file);
        if((long)image.Width*image.Height>100_000_000)throw new InvalidDataException("This image is too large (100 MP limit).");
        var pixels=image.ToByteArray(MagickFormat.Bgra);
        var bitmap=new SKBitmap(new SKImageInfo((int)image.Width,(int)image.Height,SKColorType.Bgra8888,SKAlphaType.Unpremul));
        unsafe{fixed(byte*source=pixels)Buffer.MemoryCopy(source,(void*)bitmap.GetPixels(),bitmap.ByteCount,Math.Min(pixels.LongLength,bitmap.ByteCount));}
        bitmap.NotifyPixelsChanged();return new ImageFrame(bitmap);
    }
    public static IReadOnlyList<string> Apply(string tool,IReadOnlyList<string> inputs,string destination,ImageOptions options,string? format=null,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();var completed=0;
        if(tool=="createCollage")
        {
            using var result=Collage(inputs,options.Collage,token);var file=Path.Combine(tx.StagingDirectory,"Collage.png");Save(result,file,"png");progress?.Report(new(.99,"Saving collage…"));return tx.Publish([file],token);
        }
        if(tool=="createPDF")
        {
            var file=Path.Combine(tx.StagingDirectory,"Images.pdf");using var pdf=new PdfDocument();
            foreach(var input in inputs){token.ThrowIfCancellationRequested();using var source=Load(input);AddPdfPage(pdf,source);progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));}
            pdf.Save(file);return tx.Publish([file],token);
        }
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var image=options.Annotated?.Copy()??Load(input);var ext=(format??Path.GetExtension(input).TrimStart('.')).ToLowerInvariant();if(ext=="jpeg")ext="jpg";if(ext=="tif")ext="tiff";
            if(!Formats.Contains(ext))ext="png";
            try
            {
                switch(tool)
                {
                    case "resize":
                        var width=options.Width??(int)Math.Round(image.PixelWidth*(double)(options.Height??image.PixelHeight)/image.PixelHeight);
                        var height=options.Height??(int)Math.Round(image.PixelHeight*(double)width/image.PixelWidth);
                        Replace(ref image,Resize(image,width,height));break;
                    case "crop":
                        if(options.Crop is not {} crop||crop.Width<1||crop.Height<1||crop.X<0||crop.Y<0||crop.X+crop.Width>image.PixelWidth||crop.Y+crop.Height>image.PixelHeight)throw new ArgumentException("Drag a crop inside the image.");
                        Replace(ref image,Crop(image,crop));break;
                    case "rotate":Replace(ref image,Rotate(image,options.Rotation));break;
                    case "flip":Replace(ref image,Flip(image,options.Horizontal,!options.Horizontal));break;
                    case "editImage":
                        using(var adjusted=ImageFrame.FromPng(PhotoCodec.Adjust(image.ToPng(),options.Adjustments)))Replace(ref image,TransformPhoto(adjusted,options));break;
                    case "frameImage":Replace(ref image,Frame(image,options.Frame));ext="png";break;
                    case "redactImage":Replace(ref image,Redact(image,options.Redactions,options.RedactionStyle));ext="png";break;
                    case "convert":case "compress":case "removeMetadata":case "annotate":break;
                    default:throw new ArgumentException("Unsupported image tool.");
                }
                if(tool=="compress"&&!new[]{"jpg","png","heic","webp","avif"}.Contains(ext))ext="jpg";
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="convert"?"":" "+tool)+"."+ext);
                if(tool=="compress")CompressImage(image,input,output,ext,options,token,progress);else Save(image,output,ext,options.Quality,options.EncoderPath,token);
                outputs.Add(output);progress?.Report(new(++completed/(double)inputs.Count*.98,"Saving images…"));
            }
            finally{image.Dispose();}
        }
        return tx.Publish(outputs,token);
    }
    private static void Replace(ref ImageFrame image,ImageFrame next){if(ReferenceEquals(image,next))return;image.Dispose();image=next;}
    internal static ImageFrame TransformPhoto(ImageFrame source,ImageOptions options)
    {
        var image=source.Copy();
        if(options.Width is {} ew&&options.Height is {} eh&&(ew!=image.PixelWidth||eh!=image.PixelHeight))Replace(ref image,Resize(image,ew,eh));
        if(options.FlipHorizontal||options.FlipVertical)Replace(ref image,Flip(image,options.FlipHorizontal,options.FlipVertical));
        if(options.Rotation%360!=0)Replace(ref image,Rotate(image,options.Rotation));
        return image;
    }
    internal static void ValidateCanvas(int width,int height)
    {
        if(width<1||height<1||width>30000||height>30000||(long)width*height>100_000_000)throw new ArgumentException("Choose dimensions between 1 and 30,000 pixels (100 MP limit).");
    }
    internal static ImageFrame Resize(ImageFrame image,int width,int height)
    {
        ValidateCanvas(width,height);
        return Render(width,height,canvas=>DrawSmooth(canvas,image.Bitmap,new SKRect(0,0,width,height)));
    }
    /// High-quality scaling: mipmapped linear when shrinking (no aliasing), Mitchell cubic when enlarging.
    internal static void DrawSmooth(SKCanvas canvas,SKBitmap bitmap,SKRect target,SKPaint? paint=null)
    {
        using var image=SKImage.FromBitmap(bitmap);
        var sampling=target.Width<bitmap.Width||target.Height<bitmap.Height?new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear):new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image,target,sampling,paint);
    }
    internal static ImageFrame Crop(ImageFrame image,ImageRect crop)
    {
        var target=new SKBitmap(new SKImageInfo(crop.Width,crop.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
        if(!image.Bitmap.ExtractSubset(target,new SKRectI(crop.X,crop.Y,crop.X+crop.Width,crop.Y+crop.Height))){target.Dispose();throw new ArgumentException("Drag a crop inside the image.");}
        var copy=target.Copy();target.Dispose();return new ImageFrame(copy);
    }
    internal static ImageFrame Flip(ImageFrame image,bool horizontal,bool vertical)=>Render(image.PixelWidth,image.PixelHeight,canvas=>
    {
        canvas.Scale(horizontal?-1:1,vertical?-1:1,image.PixelWidth/2f,image.PixelHeight/2f);canvas.DrawBitmap(image.Bitmap,0,0);
    });
    /// Clockwise rotation by any angle; the canvas grows to the rotated bounds, like WPF's TransformedBitmap.
    internal static ImageFrame Rotate(ImageFrame image,double degrees)
    {
        var turn=((degrees%360)+360)%360;if(turn==0)return image.Copy();
        var radians=turn*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));
        var width=(int)Math.Round(image.PixelWidth*cos+image.PixelHeight*sin);var height=(int)Math.Round(image.PixelWidth*sin+image.PixelHeight*cos);
        if(turn%90==0){width=turn%180==0?image.PixelWidth:image.PixelHeight;height=turn%180==0?image.PixelHeight:image.PixelWidth;}
        ValidateCanvas(width,height);
        return Render(width,height,canvas=>
        {
            // Right-angle turns copy pixels exactly; other angles resample smoothly.
            using var paint=new SKPaint{IsAntialias=turn%90!=0};using var source=SKImage.FromBitmap(image.Bitmap);
            canvas.Translate(width/2f,height/2f);canvas.RotateDegrees((float)turn);
            canvas.DrawImage(source,-image.PixelWidth/2f,-image.PixelHeight/2f,turn%90==0?new SKSamplingOptions(SKFilterMode.Nearest):new SKSamplingOptions(SKCubicResampler.Mitchell),paint);
        });
    }
    internal static ImageFrame Render(int width,int height,Action<SKCanvas> draw)
    {
        ValidateCanvas(width,height);
        var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul));
        try{using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);draw(canvas);canvas.Flush();return new ImageFrame(bitmap);}
        catch{bitmap.Dispose();throw;}
    }
    /// Pixels are copied into a fresh image: no comments, EXIF or source colour profile can reach the encoder.
    public static void Save(ImageFrame image,string output,string format,int quality=85,string? encoderPath=null,CancellationToken token=default)
    {
        if(format=="pdf"){using var pdf=new PdfDocument();AddPdfPage(pdf,image);pdf.Save(output);return;}
        if(format=="png")
        {
            using var encoded=SKImage.FromBitmap(image.Bitmap);using var data=encoded.Encode(SKEncodedImageFormat.Png,100);
            using var file=new FileStream(output,FileMode.CreateNew);data.SaveTo(file);return;
        }
        if(File.Exists(output))throw new IOException("The output already exists.");
        PhotoCodec.EncodePng(image.ToPng(),output,format,quality,encoderPath,token);
    }
    private static void AddPdfPage(PdfDocument pdf,ImageFrame image)
    {
        using var png=new MemoryStream(image.ToPng());using var resource=XImage.FromStream(png);var page=pdf.AddPage();
        page.Width=XUnit.FromPoint(image.PixelWidth*72/96d);page.Height=XUnit.FromPoint(image.PixelHeight*72/96d);
        using var graphic=XGraphics.FromPdfPage(page);graphic.DrawImage(resource,0,0,page.Width.Point,page.Height.Point);
    }
}
