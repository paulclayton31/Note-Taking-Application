using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook.Controls;

public partial class LibraryHomeView : UserControl
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int),
        typeof(LibraryHomeView), new PropertyMetadata(3));
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public event Action<Guid>? NotebookOpened;
    public event Action<Guid?>? FolderOpened;
    public event Action<string>? SectionChanged;
    public event Action? FilterChanged;
    public event Action? NewNotebookRequested;
    public event Action? NewFolderRequested;
    public event Action? ResumeRequested;
    public event Action? PasteRequested;
    public event Action? ImportPdfRequested;
    private void ImportPdf_Click(object sender, RoutedEventArgs e) => ImportPdfRequested?.Invoke();
    public void SetPasteAvailable(bool available) => PasteNotebookButton.IsEnabled = available;
    private void PasteNotebook_Click(object sender, RoutedEventArgs e) => PasteRequested?.Invoke();
    public event Action<string, Guid>? ActionRequested;
    public string Query => LibrarySearch.Text.Trim();
    public bool SortByName => SortPicker.SelectedIndex == 1;
    private bool quiet;
    private bool railHidden;
    public LibraryHomeView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Adapt();
    }
    public void ToggleRail() { railHidden = !railHidden; Adapt(); }
    private void Adapt()
    {
        LibraryRail.Visibility = !railHidden && ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        Columns = Math.Max(1, (int)((ActualWidth - (LibraryRail.Visibility == Visibility.Visible ? 224 : 0) - 64) / 230));
    }
    public void ClearSearch() { quiet = true; LibrarySearch.Clear(); quiet = false; SearchPlaceholder.Visibility = Visibility.Visible; }
    public void Render(string title, string subtitle, string section, IReadOnlyList<HomeBreadcrumb> crumbs,
        IReadOnlyList<HomeFolderItem> navigation, IReadOnlyList<HomeFolderItem> folders, IReadOnlyList<HomeNotebookItem> notebooks,
        string? resumeTitle, string? resumeDetail)
    {
        LibraryTitle.Text = title; LibrarySubtitle.Text = subtitle;
        Breadcrumbs.ItemsSource = crumbs; FolderNavigation.ItemsSource = navigation; FolderCards.ItemsSource = folders;
        NotebookCards.ItemsSource = notebooks; NotebookCount.Text = $"{notebooks.Count} notebook{(notebooks.Count == 1 ? "" : "s")}";
        FolderHeading.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibrary.Visibility = notebooks.Count == 0 && folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = Query.Length > 0 ? "No matches yet" : section == "starred" ? "Keep your favorites close" : "Room for your next idea";
        EmptyDescription.Text = Query.Length > 0 ? "Try another notebook or folder name." : section == "starred" ? "Use a notebook's menu to add a star." : "Create a notebook or folder here to get started.";
        ResumePanel.Visibility = section == "library" && crumbs.Count == 1 && Query.Length == 0 && resumeTitle is not null ? Visibility.Visible : Visibility.Collapsed;
        ResumeTitle.Text = resumeTitle ?? ""; ResumeDetail.Text = resumeDetail ?? "";
        foreach (var button in new[] { LibrarySection, RecentSection, StarredSection })
            button.Background = (string)button.Tag == section && crumbs.Count == 1 ? (Brush)FindResource("Selected") : Brushes.Transparent;
    }
    private void Notebook_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is Guid id) NotebookOpened?.Invoke(id); }
    private void Folder_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is Guid id) FolderOpened?.Invoke(id); }
    private void Breadcrumb_Click(object sender, RoutedEventArgs e) => FolderOpened?.Invoke(((Button)sender).Tag as Guid?);
    private void Section_Click(object sender, RoutedEventArgs e) => SectionChanged?.Invoke((string)((Button)sender).Tag);
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder is not null) SearchPlaceholder.Visibility = Query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!quiet) FilterChanged?.Invoke();
    }
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) FilterChanged?.Invoke(); }
    private void NewNotebook_Click(object sender, RoutedEventArgs e) => NewNotebookRequested?.Invoke();
    private void NewFolder_Click(object sender, RoutedEventArgs e) => NewFolderRequested?.Invoke();
    private void Resume_Click(object sender, RoutedEventArgs e) => ResumeRequested?.Invoke();
    private void More_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is { } menu) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void Action_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        if (item.Parent is ContextMenu menu && menu.PlacementTarget is Button button && button.Tag is Guid id)
            ActionRequested?.Invoke((string)item.Tag, id);
    }
}
