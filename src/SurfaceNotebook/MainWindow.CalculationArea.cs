using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private Point? calculationAreaStart;
    private Guid? calculationAreaPage;
    private bool calculationLasso;
    private readonly List<Point> calculationPath = [];
    private Point CalculationPagePoint(Point point)
    {
        var p=Viewport.TranslatePoint(point,InkSurface);
        return new Point(Math.Clamp(p.X,0,InkSurface.Width),Math.Clamp(p.Y,0,InkSurface.Height));
    }
    private void InitializeCalculationGestures()
    {
        Deactivated += (_, _) => CancelCalculationArea();
        Viewport.CanStartCalculation = point => {
            var p=Viewport.TranslatePoint(point,InkSurface);
            return ready && !busy && !homeVisible && CalculationMode && p.X>=0 && p.Y>=0 && p.X<=InkSurface.Width && p.Y<=InkSurface.Height;
        };
        Viewport.CalculationStarted += (point,lasso) => {
            CommitTextEditor(); ClearPageSelection(); CloseToolInspector();
            calculationAreaPage=currentPageId; calculationAreaStart=CalculationPagePoint(point); calculationLasso=lasso;
            calculationPath.Clear(); calculationPath.Add(calculationAreaStart.Value);
            CalculationAreaOverlay.Visibility=Visibility.Visible;
            CalculationAreaOutline.Visibility=lasso ? Visibility.Collapsed : Visibility.Visible;
            CalculationLassoOutline.Visibility=lasso ? Visibility.Visible : Visibility.Collapsed;
            CalculationLassoOutline.Points=new PointCollection(calculationPath);
            UpdateCalculationGesture(point);
        };
        Viewport.CalculationMoved += UpdateCalculationGesture;
        Viewport.CalculationCanceled += ClearCalculationGesture;
        Viewport.CalculationCompleted += point => {
            UpdateCalculationGesture(point);
            var page=calculationAreaPage;
            var bounds=calculationAreaStart is Point start ? new Rect(start,CalculationPagePoint(point)) : Rect.Empty;
            if (calculationLasso) { bounds=Rect.Empty; foreach(var p in calculationPath) bounds.Union(p); }
            var valid=!bounds.IsEmpty && bounds.Width>=8 && bounds.Height>=8 && (!calculationLasso || calculationPath.Count>=3);
            ClearCalculationGesture();
            if (!valid || page!=currentPageId) return;
            Dispatcher.BeginInvoke(() => { if(page==currentPageId && CalculationMode) OpenCalculationEditor(false,region:bounds); });
        };
    }
    private void UpdateCalculationGesture(Point point)
    {
        if(calculationAreaStart is not Point start) return;
        var p=CalculationPagePoint(point);
        if(calculationLasso) { if(calculationPath.Count<10000) calculationPath.Add(p); CalculationLassoOutline.Points=new PointCollection(calculationPath); }
        else {
            var bounds=new Rect(start,p);
            Canvas.SetLeft(CalculationAreaOutline,bounds.Left); Canvas.SetTop(CalculationAreaOutline,bounds.Top);
            CalculationAreaOutline.Width=bounds.Width; CalculationAreaOutline.Height=bounds.Height;
        }
    }
    private void ClearCalculationGesture()
    {
        calculationAreaStart=null; calculationAreaPage=null; calculationPath.Clear();
        if(CalculationAreaOverlay is null) return;
        CalculationAreaOverlay.Visibility=Visibility.Collapsed;
        CalculationAreaOutline.Visibility=Visibility.Collapsed;
        CalculationLassoOutline.Visibility=Visibility.Collapsed;
        CalculationLassoOutline.Points.Clear();
    }
    private void CancelCalculationArea() { Viewport?.CancelCalculationPointer(); ClearCalculationGesture(); }
}
