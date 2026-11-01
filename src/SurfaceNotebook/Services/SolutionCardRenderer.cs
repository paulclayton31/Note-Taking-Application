using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public static class SolutionCardRenderer
{
    public static BitmapSource Render(AiCalculation result, double width)
    {
        var panel=new StackPanel { Width=Math.Max(100,width), Background=Brushes.White, Margin=new Thickness(0) };
        void Text(string text, double size=15) => panel.Children.Add(new TextBlock { Text=text, FontSize=size, Foreground=Brushes.Black, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(12,8,12,4) });
        void Math(string formula) => panel.Children.Add(new MathDisplay { Formula=formula, Foreground=Brushes.Black, Margin=new Thickness(12,0,12,0) });
        Text("Answer",18); Math(result.Answer);
        Text(result.Explanation);
        if(result.Steps is {Length:>0}) foreach(var (step,index) in result.Steps.Select((s,i)=>(s,i))) { Text($"{index+1}. {step.Title}",17); Text(step.Detail); foreach(var equation in step.Equations) Math(equation); }
        else Text(result.Explanation);
        panel.Measure(new Size(width,double.PositiveInfinity));
        // Keep hostile/unusually large model output from allocating unbounded bitmaps.
        var height=Math.Min(12000,Math.Ceiling(panel.DesiredSize.Height)+12);
        panel.Arrange(new Rect(0,0,width,height)); panel.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*1.5),(int)Math.Ceiling(height*1.5),144,144,PixelFormats.Pbgra32);
        bitmap.Render(panel); bitmap.Freeze(); return bitmap;
    }
    public static byte[] Png(BitmapSource source) { var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var stream=new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }
}
