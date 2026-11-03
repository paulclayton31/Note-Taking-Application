using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    public static bool IsSupportedPageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".pdf" or ".stempage";
    private static bool HasClipboardFiles() => Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().Any() && Clipboard.GetFileDropList().Cast<string>().All(IsSupportedPageFile);
    private void InitializeFileDrops()
    {
        Viewport.AllowDrop=true;
        Viewport.PreviewDragOver += (_,e) => {
            e.Effects=!busy && session is not null && !homeVisible && e.Data.GetData(DataFormats.FileDrop) is string[] {Length:>0} files && files.All(IsSupportedPageFile) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled=true;
        };
        Viewport.PreviewDrop += async (_,e) => {
            e.Handled=true;
            if(e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            try { await ImportPageFilesAsync(files,e.GetPosition(InkSurface)); }
            catch(Exception ex) when(ex is IOException or FileFormatException or InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.ExternalException) { ShowError(ex); }
        };
        Viewport.PreviewMouseDown += (_,e) => { if(e.ChangedButton==MouseButton.Left && toolName!="Text") Keyboard.Focus(InkSurface); };
    }
    private async void PasteClipboardFiles(Point? position)
    {
        try { await ImportPageFilesAsync(Clipboard.GetFileDropList().Cast<string>().ToArray(),position); }
        catch(Exception ex) when(ex is IOException or FileFormatException or InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.ExternalException) { ShowError(ex); }
    }
    public async Task ImportPageFilesAsync(string[] paths,Point? position=null)
    {
        if(busy || session is null || homeVisible) return;
        if(paths.Length is <1 or >16 || paths.Any(p=>!IsSupportedPageFile(p))) throw new InvalidDataException("Drop up to 16 images, PDFs or .stempage files. Other file types are not supported.");
        foreach(var path in paths) if(!File.Exists(path) || new FileInfo(path).Length>64_000_000) throw new InvalidDataException("A dropped file is missing or exceeds 64 MB.");
        CommitTextEditor(); var page=currentPageId; var paper=session.Paper;
        var images=new List<PageImage>(); var texts=new List<PageText>(); var strokes=new List<InkStroke>();
        SetBusy(true);
        try
        {
            int index=0;
            foreach(var path in paths)
            {
                if(Path.GetExtension(path).Equals(".stempage",StringComparison.OrdinalIgnoreCase)) {
                    var doc=await PageStore.LoadAsync(path); images.AddRange(doc.Images??[]);texts.AddRange(doc.Texts??[]);strokes.AddRange(doc.Strokes);
                    if(doc.Background is not null) images.Add(ImageFromBitmap(PdfService.LoadPreview(doc.Background,1600),paper,index++));
                    continue;
                }
                BitmapSource bitmap;
                if(Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)) {
                    var docs=await PdfService.ImportAsync(path);
                    // A PDF's first page is inserted as a picture; full PDF notebooks remain available through Import PDF.
                    bitmap=PdfService.LoadPreview(docs[0].Background!,1600);
                } else {
                    using var stream=File.OpenRead(path);
                    var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);
                    var frame=decoder.Frames[0];
                    if(frame.PixelWidth>8192 || frame.PixelHeight>8192 || (long)frame.PixelWidth*frame.PixelHeight>32_000_000) throw new InvalidDataException("This image is too large to insert.");
                    bitmap=new CachedBitmap(frame,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                }
                images.Add(ImageFromBitmap(bitmap,paper,index++));
            }
        }
        finally { SetBusy(false); }
        if(page!=currentPageId || session is null) return;
        var target=position??Viewport.TranslatePoint(new Point(Viewport.ActualWidth/2,Viewport.ActualHeight/2),InkSurface);
        InsertSelection(new PageDocument(PageDocument.CurrentVersion,paper.Width,paper.Height,strokes,paper,Texts:texts,Images:images),target);
        model.Status=paths.Any(p=>Path.GetExtension(p).Equals(".pdf",StringComparison.OrdinalIgnoreCase)) ? "Files inserted · PDF first page shown; use Import PDF for all pages" : "Files inserted · drag or resize the selection";
    }
    private static PageImage ImageFromBitmap(BitmapSource bitmap,PaperSettings paper,int index)
    {
        if(bitmap.PixelWidth is <1 or >8192 || bitmap.PixelHeight is <1 or >8192 || (long)bitmap.PixelWidth*bitmap.PixelHeight>32_000_000) throw new InvalidDataException("This image is too large to insert.");
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var bytes=new MemoryStream(); encoder.Save(bytes);
        var scale=Math.Min(1,Math.Min(paper.Width*.7/bitmap.PixelWidth,paper.Height*.7/bitmap.PixelHeight));
        var image=new PageImage(Guid.NewGuid(),index*18,index*18,bitmap.PixelWidth*scale,bitmap.PixelHeight*scale,bytes.ToArray()); image.Validate(); return image;
    }
}
