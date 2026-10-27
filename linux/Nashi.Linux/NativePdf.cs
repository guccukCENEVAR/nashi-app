using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Nashi.Core;
namespace Nashi.Linux;

internal static class NativePdf
{
    public static Task<byte[][]> RenderAsync(string path,int dpi=144,CancellationToken token=default,string password="")=>Task.Run(()=>
    {
        using var pdf=PdfRasterDocument.Open(path,password);var images=new List<byte[]>();long total=0;
        for(var i=0;i<pdf.PageCount;i++){token.ThrowIfCancellationRequested();var image=pdf.RenderPage(i,dpi,token);total+=image.LongLength;if(total>512_000_000)throw new InvalidDataException("Rendered pages exceed 512 MB. Choose a lower DPI or split the PDF first.");images.Add(image);}
        return images.ToArray();
    },token);
    /// Text comes from Tesseract word boxes; faces from OpenCV's frontal-face cascade. Both run locally.
    public static async Task<Rect[]> DetectAsync(ImageFrame image,bool text,bool faces,CancellationToken token=default)
    {
        var regions=new List<Rect>();var png=image.ToPng();
        if(text)regions.AddRange((await Ocr.RecognizeAsync(png,token)).Words);
        if(faces)regions.AddRange(await Faces.DetectAsync(png,token));
        return regions.ToArray();
    }
    public static string[] DecodeQr(ImageFrame image)
    {
        var reader=new ZXing.BarcodeReaderGeneric{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=true,PossibleFormats=[ZXing.BarcodeFormat.QR_CODE]}};
        return reader.DecodeMultiple(image.Bgra(),image.PixelWidth,image.PixelHeight,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32)?.Select(r=>r.Text).Distinct().ToArray()??[];
    }
}

/// Tesseract OCR (pacman: tesseract with tesseract-data-eng / tesseract-data-tur).
internal static class Ocr
{
    internal static string? Executable=>SystemTools.Find("tesseract");
    internal static bool Available=>Executable is not null;
    private static async Task<string> LanguagesAsync(string tesseract,CancellationToken token)
    {
        var listed=(await ProcessRunner.RunAsync(tesseract,["--list-langs"],token)).Output.Split('\n').Select(l=>l.Trim()).ToHashSet(StringComparer.Ordinal);
        var preferred=(L10n.Language=="tr"?new[]{"tur","eng"}:["eng","tur"]).Where(listed.Contains).ToArray();
        if(preferred.Length==0)preferred=listed.Where(l=>l.Length>0&&!l.Contains(' ')&&l!="osd"&&!l.Contains(':')).Take(1).ToArray();
        return preferred.Length>0?string.Join('+',preferred):throw new InvalidOperationException("Install a Tesseract language, such as tesseract-data-eng or tesseract-data-tur.");
    }
    internal static async Task<(Rect[] Words,string Text)> RecognizeAsync(byte[] png,CancellationToken token)
    {
        var tesseract=Executable??throw new InvalidOperationException("Install Tesseract to detect or recognize text. On CachyOS and Arch: sudo pacman -S tesseract tesseract-data-eng");
        var work=Directory.CreateTempSubdirectory("Nashi-ocr-").FullName;
        try
        {
            var input=Path.Combine(work,"page.png");await File.WriteAllBytesAsync(input,png,token);
            var result=await ProcessRunner.RunAsync(tesseract,[input,"stdout","-l",await LanguagesAsync(tesseract,token),"tsv"],token);
            var words=new List<Rect>();var text=new StringBuilder();(int,int,int)? line=null;
            foreach(var row in result.Output.Split('\n').Skip(1))
            {
                var cells=row.TrimEnd('\r').Split('\t');if(cells.Length<12||cells[0]!="5")continue;var value=cells[11].Trim();if(value.Length==0)continue;
                var numbers=cells[1..10].Select(c=>int.TryParse(c,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)?n:-1).ToArray();if(numbers.Any(n=>n<0))continue;
                words.Add(new Rect(numbers[5],numbers[6],numbers[7],numbers[8]));
                var current=(numbers[1],numbers[2],numbers[3]);if(line is not null)text.Append(line==current?' ':'\n');line=current;text.Append(value);
            }
            return(words.ToArray(),text.ToString());
        }
        finally{Directory.Delete(work,true);}
    }
}

/// Optional face detection through python-opencv's bundled Haar cascade.
internal static class Faces
{
    private const string Script="""
import json, os, sys
import cv2
roots = [getattr(getattr(cv2, 'data', None), 'haarcascades', ''), '/usr/share/opencv4/haarcascades/', '/usr/share/opencv/haarcascades/']
path = next((os.path.join(r, 'haarcascade_frontalface_default.xml') for r in roots if r and os.path.exists(os.path.join(r, 'haarcascade_frontalface_default.xml'))), None)
if path is None:
    sys.exit('The OpenCV face model is missing.')
image = cv2.imread(sys.argv[1], cv2.IMREAD_GRAYSCALE)
found = cv2.CascadeClassifier(path).detectMultiScale(image, scaleFactor=1.1, minNeighbors=5, minSize=(24, 24))
print(json.dumps([[int(v) for v in box] for box in found]))
""";
    /// The installed cv2 package, found without starting Python.
    internal static string? ModulePath()
    {
        foreach(var root in new[]{"/usr/lib","/usr/lib64","/usr/local/lib"})
        {
            if(!Directory.Exists(root))continue;
            foreach(var python in Directory.EnumerateDirectories(root,"python3*"))foreach(var packages in new[]{"site-packages","dist-packages"})
            {var module=Path.Combine(python,packages,"cv2");if(Directory.Exists(module))return module;}
        }
        return null;
    }
    internal static async Task<Rect[]> DetectAsync(byte[] png,CancellationToken token)
    {
        var python=SystemTools.Find("python3")??throw new InvalidOperationException("Install python-opencv to detect faces. On CachyOS and Arch: sudo pacman -S python-opencv");
        var work=Directory.CreateTempSubdirectory("Nashi-faces-").FullName;
        try
        {
            var input=Path.Combine(work,"image.png");await File.WriteAllBytesAsync(input,png,token);
            ProcessResult result;
            try{result=await ProcessRunner.RunAsync(python,["-I","-c",Script,input],token);}
            catch(IOException error)when(error.Message.Contains("No module named",StringComparison.Ordinal)){throw new InvalidOperationException("Install python-opencv to detect faces. On CachyOS and Arch: sudo pacman -S python-opencv",error);}
            var boxes=JsonSerializer.Deserialize<int[][]>(result.Output)??[];
            return boxes.Where(b=>b.Length==4&&b[2]>0&&b[3]>0).Select(b=>new Rect(b[0],b[1],b[2],b[3])).ToArray();
        }
        finally{Directory.Delete(work,true);}
    }
}
