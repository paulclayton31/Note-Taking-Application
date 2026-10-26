using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SurfaceNotebook.Services;

// PDFium is not thread safe. Import holds Gate for the full document lifetime;
// every page/bitmap/document closes before its pinned input and library are freed.
internal sealed class PdfiumDocument : IDisposable
{
    internal static object Gate { get; } = new();
    private GCHandle input;
    private IntPtr document;
    private bool initialized;
    public int PageCount => FPDF_GetPageCount(document);
    public PdfiumDocument(byte[] bytes)
    {
        try
        {
            FPDF_InitLibrary(); initialized = true; input = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            document = FPDF_LoadMemDocument64(input.AddrOfPinnedObject(), (nuint)bytes.Length, IntPtr.Zero);
            if (document == IntPtr.Zero) throw new InvalidDataException($"The PDF could not be rendered (error {FPDF_GetLastError()}).");
        }
        catch { Dispose(); throw; }
    }
    public (double Width, double Height, byte[] Png) Render(int index, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var page = FPDF_LoadPage(document, index);
        if (page == IntPtr.Zero) throw new InvalidDataException($"PDF page {index + 1} could not be loaded.");
        try
        {
            var width = FPDF_GetPageWidthF(page) * 96d / 72; var height = FPDF_GetPageHeightF(page) * 96d / 72;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 288 or > 3456 || height is < 288 or > 3456)
                throw new InvalidDataException("PDF pages must be between 3 and 36 inches on each side.");
            var scale = Math.Min(2, 3072 / Math.Max(width, height));
            var pixelsX = (int)Math.Ceiling(width * scale); var pixelsY = (int)Math.Ceiling(height * scale);
            var bitmap = FPDFBitmap_Create(pixelsX, pixelsY, 1);
            if (bitmap == IntPtr.Zero) throw new InvalidDataException("The PDF preview could not be allocated.");
            try
            {
                if (FPDFBitmap_FillRect(bitmap, 0, 0, pixelsX, pixelsY, 0xFFFFFFFF) == 0) throw new InvalidDataException("The PDF preview could not be initialized.");
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, pixelsX, pixelsY, 0, 1); // Include annotation appearances; no JavaScript engine.
                cancellation.ThrowIfCancellationRequested();
                var stride = FPDFBitmap_GetStride(bitmap); var pixels = new byte[checked(stride * pixelsY)];
                Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
                var image = BitmapSource.Create(pixelsX, pixelsY, 96, 96, PixelFormats.Bgra32, null, pixels, stride); image.Freeze();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var png = new MemoryStream(); encoder.Save(png); return (width, height, png.ToArray());
            }
            finally { FPDFBitmap_Destroy(bitmap); }
        }
        finally { FPDF_ClosePage(page); }
    }
    public void Dispose()
    {
        if (document != IntPtr.Zero) { FPDF_CloseDocument(document); document = IntPtr.Zero; }
        if (input.IsAllocated) input.Free();
        if (initialized) { FPDF_DestroyLibrary(); initialized = false; }
    }
    private const string Library = "pdfium";
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDF_InitLibrary();
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDF_DestroyLibrary();
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, IntPtr password);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern uint FPDF_GetLastError();
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
}
