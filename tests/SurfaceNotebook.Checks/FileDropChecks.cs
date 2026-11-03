using System.IO;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckFileDrops(string root)
    {
        Directory.CreateDirectory(root);
        var path=Path.Combine(root,"picture.png"); var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[] {0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
        var window=new MainWindow(Path.Combine(root,"library"));Await(window.InitializeLibraryAsync());window.ShowWorkspace();ArrangeWorkspace(window);
        try {
            var ink=(PenCanvas)window.FindName("InkSurface");
            Require(((PageViewport)window.FindName("Viewport")).AllowDrop,"Page does not accept file drops.");
            Await(window.ImportPageFilesAsync([path],new Point(40,50)));
            Require(ink.Children.OfType<Image>().Count()==1,"Dropped image was not inserted.");
            Click((Button)window.FindName("UndoButton"));Require(!ink.Children.OfType<Image>().Any(),"File insertion did not undo.");
            var files=new StringCollection();files.Add(path);Clipboard.SetFileDropList(files);window.PasteSelectedObjects();
            Require(ink.Children.OfType<Image>().Count()==1,"Copied Explorer image file did not paste.");
            try { Await(window.ImportPageFilesAsync([Path.Combine(root,"bad.exe")]));throw new Exception("Unsupported file accepted."); } catch(InvalidDataException) {}
            Require(ink.Children.OfType<Image>().Count()==1,"Rejected import changed existing content.");
            var corrupt=Path.Combine(root,"bad.png");File.WriteAllText(corrupt,"not an image");
            try { Await(window.ImportPageFilesAsync([path,corrupt]));throw new Exception("Corrupt image accepted."); } catch(Exception ex) when(ex is FileFormatException or NotSupportedException or InvalidDataException) {}
            Require(ink.Children.OfType<Image>().Count()==1,"Failed batch import partially changed the page.");
        } finally {Clipboard.Clear();Await(window.ShutdownLibraryAsync());}
    }
}
