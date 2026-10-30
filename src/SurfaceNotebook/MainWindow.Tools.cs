using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private string toolName = "Pen";
    private bool changingTool;
    private double penWidth = 2.5, highlighterWidth = 18, eraserSize = 24;
    private string penColor = "#FF24324A", highlighterColor = "#FFE7B944";
    private TextBox? textEditor;
    private PageText? textBeforeEdit;
    private Guid? selectedTextId;
    private readonly DispatcherTimer textTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool toolbarHidden;

    private void InitializeTools()
    {
        InitializeToolbarMovement();
        InitializeSelectionActions();
        InitializePenEditor();
        InitializePenGestures();
        Viewport.AddHandler(PreviewStylusDownEvent, new StylusDownEventHandler((_, e) =>
        {
            if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus && toolName != "Hand") CloseToolInspector();
        }), true);
        InkSurface.StrokeCollected += (_, e) =>
        {
            if (session is null) return;
            CloseToolInspector();
            if (toolName == "Pen") { if (!TryFinishHeldShape(e.Stroke)) TryScribbleErase(e.Stroke); return; }
            if (toolName != "Shape") return;
            var shapes = ShapeFactory.Create(e.Stroke, ShapePicker.SelectedIndex);
            restoring = true;
            try { InkSurface.Strokes.Remove(e.Stroke); InkSurface.Strokes.Add(shapes); }
            finally { restoring = false; }
            SaveFullPage();
        };
        InkSurface.SelectionMoved += (_, _) => SaveSelectionTransform();
        InkSurface.SelectionResized += (_, _) => SaveSelectionTransform();
        InkSurface.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(TextCanvas_Down), handledEventsToo: true);
        textTimer.Tick += (_, _) => { textTimer.Stop(); SaveTextDraft(); };
    }
    private void SaveSelectionTransform()
    {
        if (restoring) return;
        foreach (var stroke in InkSurface.GetSelectedStrokes()) InkMapper.RenewIdentity(stroke);
        if (session is not null)
            foreach (var element in InkSurface.GetSelectedElements().OfType<TextBox>())
            {
                var index = session.Texts.FindIndex(t => t.Id == (Guid)element.Tag);
                if (index >= 0) session.Texts[index] = session.Texts[index] with
                { X = InkCanvas.GetLeft(element), Y = InkCanvas.GetTop(element), Width = Math.Max(40, element.Width) };
            }
        SaveFullPage();
    }
    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (InkSurface is null || ColorPicker is null || sender is not RadioButton button || !IsInitialized) return;
        CommitTextEditor();
        CancelHeldShape();
        barrelSelection = false;
        toolName = (string)button.Tag;
        selectionMenu.IsOpen = false; PenEditorPopup.IsOpen = false; ToolOptionsPopup.IsOpen = false; AllPensPopup.IsOpen = false;
        if (toolName != "Text") selectedTextId = null;
        changingTool = true;
        try
        {
            if (toolName == "Pen") LoadActivePenSettings();
            ThicknessSlider.Minimum = toolName is "Highlighter" or "Eraser" ? 4 : .5;
            ThicknessSlider.Maximum = toolName is "Highlighter" or "Eraser" ? 60 : 12;
            ThicknessSlider.Value = toolName switch { "Highlighter" => highlighterWidth, "Eraser" => eraserSize, _ => penWidth };
            var color = toolName == "Highlighter" ? highlighterColor : penColor;
            SelectInkColor(color);
        }
        finally { changingTool = false; }
        ThicknessValue.Text = ThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        ApplyPen();
        foreach (TextBox box in InkSurface.Children) box.IsHitTestVisible = toolName == "Text";
        ToolTitle.Text = toolName == "Lasso" ? "Select ink" : toolName;
        ToolHint.Text = toolName switch
        {
            "Highlighter" => "Translucent ink with a broad, flat tip",
            "Eraser" => "Erase strokes or precisely remove part of a line",
            "Lasso" => "Circle ink or text · tap the selection for copy, duplicate, or delete",
            "Shape" => "Drag from one corner to the opposite corner",
            "Text" => "Tap the paper with a pen or click to add text",
            "Hand" => "Drag with pen or mouse · fingers always navigate",
            _ => "Pressure-sensitive ink, tuned for Surface Pen"
        };
        ColorControls.Visibility = toolName is "Pen" or "Highlighter" or "Shape" or "Text" ? Visibility.Visible : Visibility.Collapsed;
        ThicknessControls.Visibility = toolName is "Pen" or "Highlighter" or "Shape" or "Eraser" ? Visibility.Visible : Visibility.Collapsed;
        PenStyleControls.Visibility = toolName == "Pen" ? Visibility.Visible : Visibility.Collapsed;
        ScribbleEraseToggle.Visibility = toolName == "Pen" ? Visibility.Visible : Visibility.Collapsed;
        HoldShapesToggle.Visibility = toolName == "Pen" ? Visibility.Visible : Visibility.Collapsed;
        SavedPensPanel.Visibility = toolName == "Pen" ? Visibility.Visible : Visibility.Collapsed;
        EraserControls.Visibility = toolName == "Eraser" ? Visibility.Visible : Visibility.Collapsed;
        ShapeControls.Visibility = toolName == "Shape" ? Visibility.Visible : Visibility.Collapsed;
        TextControls.Visibility = toolName == "Text" ? Visibility.Visible : Visibility.Collapsed;
        ThicknessLabel.Text = toolName == "Eraser" ? "Eraser size" : "Thickness";
        RefreshSavedPens(); RefreshColorChoices(); UpdateSelectionActions();
        InputHint.Text = ToolHint.Text;
        UpdateToolInspectorPlacement(ActualWidth);
    }
    private void ApplyPen()
    {
        if (InkSurface is null || ColorPicker?.SelectedItem is null) return;
        var color = (Color)ColorConverter.ConvertFromString(toolName == "Highlighter" ? highlighterColor : penColor);
        var highlighter = toolName == "Highlighter";
        var width = highlighter ? highlighterWidth : penWidth;
        // The pressure/curve path for the default pen is identical to the Surface-tested prototype.
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color, Width = width, Height = width,
            IgnorePressure = highlighter || toolName == "Shape" || BallStyleButton?.IsChecked == true,
            FitToCurve = toolName != "Shape", IsHighlighter = highlighter,
            StylusTip = highlighter ? StylusTip.Rectangle : StylusTip.Ellipse
        };
        InkSurface.EraserShape = new EllipseStylusShape(eraserSize, eraserSize);
        InkSurface.EditingMode = barrelSelection ? InkCanvasEditingMode.Select : toolName switch
        {
            "Eraser" => EraserModePicker?.SelectedIndex == 1 ? InkCanvasEditingMode.EraseByPoint : InkCanvasEditingMode.EraseByStroke,
            "Lasso" => InkCanvasEditingMode.Select,
            "Hand" or "Text" => InkCanvasEditingMode.None,
            _ => InkCanvasEditingMode.Ink
        };
        Viewport.HandMode = !barrelSelection && toolName == "Hand";
        InkSurface.Cursor = barrelSelection ? Cursors.Pen : toolName == "Hand" ? Cursors.Hand : toolName == "Text" ? Cursors.IBeam : Cursors.Pen;
        if (CurrentColor is not null) CurrentColor.Fill = new SolidColorBrush(color);
        if (RailColor is not null) RailColor.Fill = new SolidColorBrush(color);
        RefreshColorChoices();
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingTool || ColorPicker?.SelectedItem is not ComboBoxItem item) return;
        if (toolName == "Highlighter") highlighterColor = (string)item.Tag; else penColor = (string)item.Tag;
        ApplyPen();
        ChangeTextAppearance(box => box.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(penColor)));
        WritingSettingsChanged();
    }
    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (changingTool) return;
        if (toolName == "Highlighter") highlighterWidth = e.NewValue;
        else if (toolName == "Eraser") eraserSize = e.NewValue;
        else penWidth = e.NewValue;
        if (ThicknessValue is not null) ThicknessValue.Text = e.NewValue.ToString("0.#", CultureInfo.CurrentCulture);
        ApplyPen();
        WritingSettingsChanged();
    }
    private void Properties_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingTool) return;
        ApplyPen();
        if (TextSizePicker?.SelectedItem is ComboBoxItem item)
            ChangeTextAppearance(box => box.FontSize = double.Parse((string)item.Content, CultureInfo.InvariantCulture));
        WritingSettingsChanged();
    }
    private void ChangeTextAppearance(Action<TextBox> change)
    {
        if (toolName != "Text" || session is null || selectedTextId is not Guid id) return;
        var box = InkSurface.Children.OfType<TextBox>().FirstOrDefault(b => (Guid)b.Tag == id);
        if (box is null) return;
        change(box);
        if (textEditor == box) { UpdateTextDraft(); return; }
        var index = session.Texts.FindIndex(t => t.Id == id); if (index < 0) return;
        var updated = session.Texts[index] with { FontSize = box.FontSize, Color = ((SolidColorBrush)box.Foreground).Color.ToString() };
        if (updated == session.Texts[index]) return;
        session.Texts[index] = updated; session.History.Record(InkSurface.Strokes, session.Texts);
        SaveTextDraft(); RefreshHistory();
    }
    private void ToolProperties_Click(object sender, RoutedEventArgs e)
    { selectionMenu.IsOpen = false; ToggleToolInspector(); }
    private void HideToolbar_Click(object sender, RoutedEventArgs e)
    {
        toolbarHidden = !toolbarHidden;
        ToolbarFrame.Visibility = toolbarHidden ? Visibility.Collapsed : Visibility.Visible;
        SavedColorBar.Visibility = toolbarHidden ? Visibility.Collapsed : Visibility.Visible;
        RevealToolbar.Visibility = toolbarHidden ? Visibility.Visible : Visibility.Collapsed;
        CloseToolInspector();
    }

    private void TextCanvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (barrelSelection || toolName != "Text" || session is null || busy || e.ChangedButton != MouseButton.Left ||
            e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch) return;
        CloseToolInspector();
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null && node != InkSurface; node = VisualTreeHelper.GetParent(node))
            if (node is TextBox) return;
        CommitTextEditor();
        var point = e.GetPosition(InkSurface);
        if (point.X < 0 || point.Y < 0 || point.X >= session.Paper.Width || point.Y >= session.Paper.Height) return;
        var size = TextSizePicker.SelectedItem is ComboBoxItem item ? double.Parse((string)item.Content, CultureInfo.InvariantCulture) : 20;
        var text = new PageText(Guid.NewGuid(), point.X, point.Y, Math.Max(60, Math.Min(380, session.Paper.Width - point.X - 12)), size, penColor, "");
        session.Texts.Add(text);
        var box = CreateTextBox(text);
        InkSurface.Children.Add(box); box.Focus(); textEditor = box; textBeforeEdit = null; selectedTextId = text.Id;
        e.Handled = true;
    }
    private TextBox CreateTextBox(PageText text)
    {
        var box = new TextBox
        {
            Tag = text.Id, Text = text.Text, Width = text.Width, FontSize = text.FontSize,
            Foreground = Controls.PaperSurface.Brush(text.Color), CaretBrush = Controls.PaperSurface.Brush(text.Color),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(2), MinHeight = 28, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            IsHitTestVisible = toolName == "Text"
        };
        InkCanvas.SetLeft(box, text.X); InkCanvas.SetTop(box, text.Y);
        box.GotKeyboardFocus += (_, _) =>
        {
            if (textEditor == box) return;
            CommitTextEditor(); textEditor = box; textBeforeEdit = session?.Texts.FirstOrDefault(t => t.Id == (Guid)box.Tag);
            selectedTextId = (Guid)box.Tag;
            changingTool = true;
            try
            {
                for (var i = 0; i < TextSizePicker.Items.Count; i++)
                    if (double.Parse((string)((ComboBoxItem)TextSizePicker.Items[i]).Content, CultureInfo.InvariantCulture) == box.FontSize)
                        TextSizePicker.SelectedIndex = i;
            }
            finally { changingTool = false; }
            box.BorderThickness = new Thickness(1); box.BorderBrush = (Brush)FindResource("Accent");
        };
        box.LostKeyboardFocus += (_, _) => { if (textEditor == box) CommitTextEditor(); };
        box.TextChanged += (_, _) => { if (textEditor == box) UpdateTextDraft(); };
        var menu = new ContextMenu();
        var delete = new MenuItem { Header = "Delete text" }; menu.Items.Add(delete);
        delete.Click += (_, _) =>
        {
            CommitTextEditor();
            if (session is null) return;
            session.Texts.RemoveAll(t => t.Id == (Guid)box.Tag); RenderTexts(); SaveFullPage();
        };
        box.ContextMenu = menu;
        return box;
    }
    private void RenderTexts()
    {
        textEditor = null; textBeforeEdit = null; selectedTextId = null;
        InkSurface.Children.Clear();
        if (session is null) return;
        foreach (var text in session.Texts) InkSurface.Children.Add(CreateTextBox(text));
    }
    private void UpdateTextDraft()
    {
        if (textEditor is not { } box || session is null) return;
        var index = session.Texts.FindIndex(t => t.Id == (Guid)box.Tag);
        if (index < 0) return;
        session.Texts[index] = session.Texts[index] with { Text = box.Text, FontSize = box.FontSize, Color = ((SolidColorBrush)box.Foreground).Color.ToString() };
        textTimer.Stop(); textTimer.Start();
    }
    private void SaveTextDraft()
    {
        if (session is null || writer is null || currentPageId is not Guid id) return;
        var texts = session.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        writer.Write(db => db.SaveTexts(id, texts)); UpdateSaveStatus(); ScheduleThumbnail();
    }
    private void CommitTextEditor()
    {
        if (textEditor is not { } box || session is null) return;
        UpdateTextDraft(); textTimer.Stop();
        var current = session.Texts.FirstOrDefault(t => t.Id == (Guid)box.Tag);
        var before = textBeforeEdit; textEditor = null; textBeforeEdit = null;
        box.BorderThickness = new Thickness(0); box.BorderBrush = Brushes.Transparent;
        if (current is not null && string.IsNullOrWhiteSpace(current.Text)) { session.Texts.Remove(current); InkSurface.Children.Remove(box); current = null; selectedTextId = null; }
        if (current == before) return;
        session.History.Record(InkSurface.Strokes, session.Texts);
        SaveTextDraft(); RefreshHistory();
    }
}
