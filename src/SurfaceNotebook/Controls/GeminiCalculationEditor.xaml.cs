using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook.Controls;

public partial class GeminiCalculationEditor : UserControl
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private GeminiCalculator calculator = new(Client);
    private GeminiKeyStore? keys;
    private byte[]? image;
    private CancellationTokenSource? request;
    private bool closed, loading;
    public AiCalculation? Result { get; private set; }
    public event Action? NewQuestionRequested;
    private void NewQuestion_Click(object sender, RoutedEventArgs e) => NewQuestionRequested?.Invoke();
    public event Action? Accepted;
    public event Action? Canceled;
    public GeminiCalculationEditor() { InitializeComponent(); }
    public void Prepare(byte[]? png, string question, GeminiKeyStore keyStore, AiCalculation? saved = null, GeminiCalculator? service = null)
    {
        loading = true; image = png; keys = keyStore; calculator = service ?? new GeminiCalculator(Client);
        QuestionBox.Text = question; KeyBox.Password = keyStore.Load(); RememberKey.IsChecked = KeyBox.Password.Length > 0;
        SettingsPanel.IsExpanded = KeyBox.Password.Length == 0;
        SelectionImage.Source = png is null ? null : PageImageSource.Decode(png);
        NoImage.Visibility = png is null ? Visibility.Visible : Visibility.Collapsed;
        Result = saved;
        if (saved is not null) ShowResult(new GeminiCalculator.Result(saved.Problem, saved.Answer, saved.Explanation, false, saved.Steps));
        loading = false; RefreshButtons();
    }
    private void RefreshButtons()
    {
        if (SolveButton is null) return;
        SolveButton.IsEnabled = !closed && request is null && KeyBox.Password.Trim().Length > 0 && (image is not null || !string.IsNullOrWhiteSpace(QuestionBox.Text));
        AcceptButton.IsEnabled = !closed && request is null && Result is not null;
        QuestionBox.IsReadOnly = request is not null; KeyBox.IsEnabled = request is null;
        StopButton.Visibility = request is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void InvalidateResult()
    {
        if (loading || SolveButton is null) return;
        ClearSolution(); Result = null; ProblemText.Text = ExplanationText.Text = ""; AnswerText.Formula = "";
        StatusText.Text = ""; ErrorText.Text = ""; RefreshButtons();
    }
    private void Question_Changed(object sender, TextChangedEventArgs e) => InvalidateResult();
    private void Key_Changed(object sender, RoutedEventArgs e) { if (!loading) RefreshButtons(); }
    public async Task SolveAsync()
    {
        if (closed || request is not null) return;
        ClearSolution(); Result = null; ErrorText.Text = ""; ProblemText.Text = ExplanationText.Text = ""; AnswerText.Formula = "";
        var current = new CancellationTokenSource(); request = current; RefreshButtons();
        StatusText.Text = "Reading the problem and calculating…";
        try
        {
            var key = KeyBox.Password.Trim();
            if (RememberKey.IsChecked == true) keys?.Save(key); else keys?.Delete();
            var instruction = QuestionBox.Text;
            var solved = await calculator.SolveAsync(key, image, instruction, current.Token);
            if (closed || current.IsCancellationRequested) return;
            ShowResult(solved);
            if (!solved.NeedsClarification)
            {
                Result = new AiCalculation(GeminiCalculator.Model, solved.Problem, solved.Answer, solved.Explanation, instruction, image, solved.Steps);
                Result.Validate(); StatusText.Text = "Review what Gemini read and the answer before adding it to your note.";
            }
            else StatusText.Text = "Gemini needs clarification. Correct the question above, then solve again.";
        }
        catch (OperationCanceledException) { if (!closed) StatusText.Text = current.IsCancellationRequested ? "Stopped. No result was added." : "The request timed out. Try a smaller problem."; }
        catch (HttpRequestException) { if (!closed) { StatusText.Text = ""; ErrorText.Text = "Could not connect to Gemini. Check your internet connection and try again."; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or CryptographicException or UnauthorizedAccessException)
        { if (!closed) { StatusText.Text = ""; ErrorText.Text = ex.Message; } }
        finally { request = null; current.Dispose(); if (!closed) RefreshButtons(); }
    }
    private void ShowResult(GeminiCalculator.Result result)
    {
        ProblemText.Text = result.Problem; AnswerText.Formula = result.Answer; ExplanationText.Text = result.Explanation;
        StepsList.ItemsSource = result.Steps?.Select((step, i) => new {
            Heading = $"{i + 1}. {step.Title}", step.Detail, step.Equations,
            EquationVisibility = step.Equations.Length > 0 ? Visibility.Visible : Visibility.Collapsed
        }).ToArray();
        ResultPanel.Visibility = Visibility.Visible;
        SourcePanel.IsExpanded = result.NeedsClarification;
        CopySolutionButton.Visibility = result.NeedsClarification ? Visibility.Collapsed : Visibility.Visible;
        if (!loading) Dispatcher.BeginInvoke(() => AnswerText.BringIntoView());
    }
    private void ClearSolution()
    {
        StepsList.ItemsSource = null; ResultPanel.Visibility = Visibility.Collapsed;
    }
    private void CopySolution_Click(object sender, RoutedEventArgs e)
    {
        if (Result is null) return;
        try { Clipboard.SetText(Result.DisplayText); StatusText.Text = "Solution copied."; }
        catch (System.Runtime.InteropServices.COMException) { ErrorText.Text = "Clipboard is busy. Try copying again."; }
    }
    public void UseAsSidePanel()
    {
        AcceptButton.Visibility = Visibility.Collapsed;
        NewQuestionButton.Visibility = Visibility.Visible;
        HeadingText.Text = "Calculation";
        IntroText.Text = "Keep writing while you review the solution. Close and reopen it with Solution. A new calculation replaces this one.";
    }
    public void CancelPending() => request?.Cancel();
    public void Stop() { closed = true; request?.Cancel(); Result = null; }
    private async void Solve_Click(object sender, RoutedEventArgs e) => await SolveAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => request?.Cancel();
    private void Accept_Click(object sender, RoutedEventArgs e) { if (AcceptButton.IsEnabled) Accepted?.Invoke(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Canceled?.Invoke();
    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        try { keys?.Delete(); KeyBox.Password = ""; RememberKey.IsChecked = false; StatusText.Text = "Saved key removed."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorText.Text = "Could not remove the saved key. Check file access and try again."; }
    }
    private void GetKey_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://aistudio.google.com/api-keys") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { ErrorText.Text = "Open https://aistudio.google.com/api-keys in your browser to create a key."; }
    }
}

