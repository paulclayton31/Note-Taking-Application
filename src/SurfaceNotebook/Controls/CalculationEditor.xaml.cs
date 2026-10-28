using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook.Controls;

public partial class CalculationEditor : UserControl
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<InkStroke> ink = [];
    private string prefix = "";
    private int revision;
    private bool recognizing, closed;
    public event Action<string>? Accepted;
    public event Action? Canceled;
    public string Source { get => SourceBox.Text; set => SourceBox.Text = value; }
    public CalculationEngine.Result? Result { get; private set; }
    public CalculationEditor()
    {
        InitializeComponent();
        timer.Tick += (_, _) => { timer.Stop(); RefreshResult(); };
        RecognizeButton.IsEnabled = false;
    }
    public void Prepare(IReadOnlyList<InkStroke> strokes, string source, bool editing)
    {
        ink = strokes; prefix = source; Source = source;
        RecognizeButton.IsEnabled = ink.Count > 0;
        AcceptButton.Content = editing ? "Update calculation" : "Add to page";
        RefreshResult();
    }
    private void Source_Changed(object sender, TextChangedEventArgs e)
    {
        revision++;
        if (AcceptButton is null) return;
        Result = null; AcceptButton.IsEnabled = false; ResultText.Text = ""; ErrorText.Text = "";
        timer.Stop(); timer.Start();
    }
    public void RefreshResult()
    {
        timer.Stop(); Result = null;
        try
        {
            Result = CalculationEngine.Evaluate(Source);
            ResultText.Text = Result.DisplayText; ErrorText.Text = "";
        }
        catch (CalculationEngine.CalculationException ex) { ResultText.Text = ""; ErrorText.Text = ex.Message; }
        AcceptButton.IsEnabled = Result is not null && !recognizing && !closed;
    }
    public async Task RecognizeAsync()
    {
        if (closed || recognizing || ink.Count == 0) return;
        recognizing = true; var startedAt = revision;
        RecognizeButton.IsEnabled = false; AcceptButton.IsEnabled = false;
        RecognitionStatus.Text = "Reading the selected handwriting… You can still type or correct your draft.";
        try
        {
            var result = await HandwritingRecognition.RecognizeAsync(ink, lifetime.Token);
            if (closed) return;
            RecognitionStatus.Text = result.Message;
            if (startedAt == revision)
                Source = string.Join(Environment.NewLine, new[] { prefix, result.Text }.Where(s => !string.IsNullOrWhiteSpace(s)));
            else RecognitionStatus.Text = "Your edits were kept. Choose Recognize handwriting to replace the draft with recognition results.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            recognizing = false;
            if (!closed) { RecognizeButton.IsEnabled = ink.Count > 0; RefreshResult(); }
        }
    }
    public void Stop()
    {
        if (closed) return;
        closed = true; timer.Stop(); lifetime.Cancel(); lifetime.Dispose();
    }
    private async void Recognize_Click(object sender, RoutedEventArgs e) => await RecognizeAsync();
    private void Example_Click(object sender, RoutedEventArgs e) { Source = "F = m * a\nm = 2\na = 3"; RefreshResult(); SourceBox.Focus(); }
    private void Accept_Click(object sender, RoutedEventArgs e) { RefreshResult(); if (AcceptButton.IsEnabled) Accepted?.Invoke(Source); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Canceled?.Invoke();
}
