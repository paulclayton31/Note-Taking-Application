using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Controls;

internal static partial class Program
{
    private static void CheckCalculationGesturesAndGlyphs()
    {
        var view=new PageViewport { CalculationSelectionEnabled=true, CanStartCalculation = p => p.X>=0 && p.Y>=0 };
        bool? lasso=null; int completed=0,canceled=0,holds=0;
        view.CalculationStarted += (_,pen) => lasso=pen;
        view.CalculationCompleted += _ => completed++;
        view.CalculationCanceled += () => canceled++;
        view.TouchHeld += _ => holds++;
        view.BeginContact(1,new Point(30,40)); view.MoveContact(1,new Point(160,180)); view.CheckTouchHold(Environment.TickCount64+1000);
        Require(lasso==false && view.View.Zoom==1 && view.View.X==24 && holds==0,"Calculate finger selection panned the page or opened a hold menu.");
        view.EndContact(1); Require(completed==1,"Finger rectangle did not complete.");
        Require(view.BeginCalculationPointer(new Point(20,20),true),"Pen lasso did not start.");
        view.MoveCalculationPointer(new Point(200,200)); view.EndCalculationPointer(new Point(20,20));
        Require(lasso==true && completed==2,"Pen lasso did not complete.");
        view.BeginContact(1,new Point(30,40)); view.BeginContact(2,new Point(160,180)); view.MoveContact(2,new Point(220,240));
        view.EndContact(1); view.EndContact(2);
        Require(completed==2 && canceled>=1 && view.View.Zoom>1,"A pinch completed an unwanted calculation or failed to navigate.");
        view.BeginContact(1,new Point(30,40)); view.SetPenPresence(true); view.EndContact(1);
        Require(completed==2,"Palm suppression completed a selection.");
        view.SetPenPresence(false); view.BeginContact(1,new Point(30,40)); view.CalculationSelectionEnabled=false; view.EndContact(1);
        Require(completed==2,"Switching tools completed a canceled calculation.");
        Require(!view.BeginCalculationPointer(new Point(-20,10),true),"Selection started outside the page.");

        foreach (var formula in new[] { @"\text{R: s to a} = 2", @"V_{out}=\frac{60}{7}\,\mathrm{V}", @"x^{2}+\frac{1}{\frac{2}{3}}" })
        foreach (var scale in new[] { 1.0,1.5,2.0 })
        {
            var math=new MathDisplay { Formula=formula, Foreground=Brushes.Black };
            math.Measure(new Size(280,double.PositiveInfinity)); math.Arrange(new Rect(new Point(),math.DesiredSize)); math.UpdateLayout();
            Require(math.Content is ScrollViewer { Content: MathGlyphSurface },"Equation did not use normalized glyph bounds.");
            var surface=(MathGlyphSurface)((ScrollViewer)math.Content).Content;
            Require(surface.ActualHeight>=surface.GlyphBounds.Height+15,"Equation row omitted part of its baseline bounds.");
            // Rasterize the actual measured row: ink must have clear padding on every edge at each DPI.
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(math.ActualWidth*scale),(int)Math.Ceiling(math.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            bitmap.Render(math); int stride=bitmap.PixelWidth*4; var pixels=new byte[stride*bitmap.PixelHeight]; bitmap.CopyPixels(pixels,stride,0);
            int top=bitmap.PixelHeight,bottom=-1,left=bitmap.PixelWidth,right=-1;
            for(int y=0;y<bitmap.PixelHeight;y++) for(int x=0;x<bitmap.PixelWidth;x++) if(pixels[y*stride+x*4+3]>32) { top=Math.Min(top,y); bottom=Math.Max(bottom,y);left=Math.Min(left,x);right=Math.Max(right,x); }
            Require(bottom>=top && top>1 && bottom<bitmap.PixelHeight-2 && left>1 && right<bitmap.PixelWidth-2,"Rendered math touched a clipped row edge.");
        }
    }
}
