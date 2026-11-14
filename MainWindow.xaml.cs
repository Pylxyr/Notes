using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Notes;

public partial class MainWindow : Window
{
    const double MaxImageWidth = 560;
    static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    readonly NoteStore _store = new();
    readonly ObservableCollection<NoteItem> _notes = new();
    readonly ListCollectionView _view;
    readonly DispatcherTimer _saveTimer;

    NoteItem? _current;
    bool _loading;            // true while a note is being loaded into the editor
    bool _dirty;              // current note has unsaved changes
    bool _suppressSelection;  // true while the list is refreshed from code
    bool _dark;

    public MainWindow()
    {
        _dark = _store.LoadTheme() ?? SystemPrefersDark();
        App.ApplyTheme(_dark);
        InitializeComponent();
        UpdateThemeButton();

        foreach (var n in _store.LoadIndex()) _notes.Add(n);

        _view = new ListCollectionView(_notes);
        _view.SortDescriptions.Add(new SortDescription(nameof(NoteItem.Pinned), ListSortDirection.Descending));
        _view.SortDescriptions.Add(new SortDescription(nameof(NoteItem.Modified), ListSortDirection.Descending));
        _view.Filter = FilterNote;
        NotesList.ItemsSource = _view;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => SaveCurrent();

        DataObject.AddPastingHandler(Editor, OnPaste);

        UpdateEmptyState();
        var first = _view.Cast<NoteItem>().FirstOrDefault();
        if (first is not null) NotesList.SelectedItem = first;
        else ShowEmpty();
    }

    // ------------------------------------------------------------------ window

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetTitleBarDark(_dark);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveCurrent(refresh: false);
        base.OnClosing(e);
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.N: NewNote(); e.Handled = true; break;
            case Key.F: SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; break;
            case Key.S: SaveCurrent(); e.Handled = true; break;
        }
    }

    // ------------------------------------------------------------------ theme

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    void SetTitleBarDark(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); } catch { }
    }

    static bool SystemPrefersDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int v && v == 0;
        }
        catch { return false; }
    }

    void UpdateThemeButton() => ThemeButton.ToolTip = _dark ? "Switch to light theme" : "Switch to dark theme";

    void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        App.ApplyTheme(_dark);
        SetTitleBarDark(_dark);
        UpdateThemeButton();
        _store.SaveTheme(_dark);
    }

    // ------------------------------------------------------------------ list

    bool FilterNote(object item)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        var n = (NoteItem)item;
        return n.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || n.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    void RefreshList()
    {
        if (_view is null) return;
        _suppressSelection = true;
        try
        {
            _view.Refresh();
            if (_current is not null && _view.Contains(_current)) NotesList.SelectedItem = _current;
        }
        finally { _suppressSelection = false; }
        UpdateEmptyState();
    }

    void UpdateEmptyState()
    {
        EmptyList.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyList.Text = _notes.Count == 0
            ? "No notes yet.\nPress + to write your first one."
            : "No notes match your search.";
    }

    void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (NotesList.SelectedItem is NoteItem note) ShowNote(note);
    }

    // ------------------------------------------------------------------ notes

    void NewNote_Click(object sender, RoutedEventArgs e) => NewNote();

    void NewNote()
    {
        SaveCurrent(refresh: false);
        SearchBox.Text = "";
        var note = new NoteItem();
        _notes.Add(note);
        _store.SaveIndex(_notes);
        UpdateEmptyState();
        NotesList.SelectedItem = note;
        NotesList.ScrollIntoView(note);
        TitleBox.Focus();
    }

    void ShowNote(NoteItem note)
    {
        if (ReferenceEquals(note, _current)) return;

        bool saved = SaveCurrent(refresh: false);

        _loading = true;
        _current = note;

        var doc = _store.LoadDocument(note.Id);
        PrepareDocument(doc);
        Editor.Document = doc;
        TitleBox.Text = note.Title;
        StatusText.Text = string.IsNullOrEmpty(note.Text) && !note.HasImages
            ? "New note"
            : $"Edited {note.Modified:MMM d, yyyy 'at' h:mm tt}";
        UpdatePinButton();

        EditorPanel.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        // Loading raises TextChanged; ignore it until things have settled.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _loading = false;
            _dirty = false;
        }));

        if (saved) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshList));
    }

    void ShowEmpty()
    {
        _current = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
    }

    static void PrepareDocument(FlowDocument doc)
    {
        doc.PagePadding = new Thickness(0);
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph());
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _current.Pinned = !_current.Pinned;
        _store.SaveIndex(_notes);
        UpdatePinButton();
        RefreshList();
    }

    void UpdatePinButton()
    {
        if (_current is null) return;
        PinButton.Content = _current.Pinned ? "\uE77A" : "\uE718";
        PinButton.ToolTip = _current.Pinned ? "Unpin note" : "Pin note";
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var answer = MessageBox.Show(this,
            $"Delete \u201C{_current.DisplayTitle}\u201D? This can\u2019t be undone.",
            "Delete note", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        _saveTimer.Stop();
        _dirty = false;
        var gone = _current;
        _current = null;

        _notes.Remove(gone);
        _store.DeleteDocument(gone.Id);
        _store.SaveIndex(_notes);
        UpdateEmptyState();

        var next = _view.Cast<NoteItem>().FirstOrDefault();
        if (next is not null) NotesList.SelectedItem = next;
        else ShowEmpty();
    }

    // ------------------------------------------------------------------ editing & saving

    void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Title = TitleBox.Text;
        MarkDirty();
    }

    void TitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Down)
        {
            Editor.Focus();
            e.Handled = true;
        }
    }

    void Editor_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    void MarkDirty()
    {
        if (_loading || _current is null) return;
        _dirty = true;
        StatusText.Text = "Saving\u2026";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <returns>true if something was written to disk.</returns>
    bool SaveCurrent(bool refresh = true)
    {
        _saveTimer.Stop();
        if (_current is null || !_dirty) return false;
        try
        {
            var doc = Editor.Document;
            _store.SaveDocument(_current.Id, doc);

            var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            _current.Text = text.Length > 4000 ? text[..4000] : text;
            _current.HasImages = ContainsImage(doc);
            _current.Modified = DateTime.Now;
            _store.SaveIndex(_notes);

            _dirty = false;
            StatusText.Text = "Saved";
            if (refresh) RefreshList();
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn\u2019t save: " + ex.Message;
            return false;
        }
    }

    static bool ContainsImage(FlowDocument doc)
    {
        for (var p = doc.ContentStart; p is not null && p.CompareTo(doc.ContentEnd) < 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ photos

    void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Insert photo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames) TryInsertFile(file);
    }

    /// <summary>Ctrl+V (and drag &amp; drop). Photos are inserted inline; text from other apps is pasted as plain text.</summary>
    void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;

        // Image files copied in Explorer
        var files = ImageFilesIn(data);
        if (files.Count > 0)
        {
            e.CancelCommand();
            foreach (var file in files) TryInsertFile(file);
            return;
        }

        bool hasText = data.GetDataPresent(DataFormats.UnicodeText)
                    || data.GetDataPresent(DataFormats.Text)
                    || data.GetDataPresent(DataFormats.Rtf);

        if (hasText)
        {
            // Keep formatting when copying inside this app; otherwise paste plain text so it matches the theme.
            bool fromThisApp = data.GetDataPresent(DataFormats.Xaml) || data.GetDataPresent(DataFormats.XamlPackage);
            if (!fromThisApp && data.GetDataPresent(DataFormats.UnicodeText))
                e.FormatToApply = DataFormats.UnicodeText;
            return;
        }

        // A screenshot or "Copy image" from a browser
        var image = ReadImage(data);
        if (image is not null)
        {
            e.CancelCommand();
            InsertImage(image);
        }
    }

    void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (ImageFilesIn(e.Data).Count == 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Editor_PreviewDrop(object sender, DragEventArgs e)
    {
        var files = ImageFilesIn(e.Data);
        if (files.Count == 0) return;
        e.Handled = true;
        foreach (var file in files) TryInsertFile(file);
    }

    static List<string> ImageFilesIn(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                return paths
                    .Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        catch { }
        return new List<string>();
    }

    static BitmapSource? ReadImage(IDataObject data)
    {
        // The "PNG" clipboard format keeps transparency intact.
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream png) return Decode(png);
        }
        catch { }

        try
        {
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
                return Reencode(bmp);
        }
        catch { }

        return null;
    }

    static BitmapSource Reencode(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Decode(ms);
    }

    static BitmapSource Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        stream.CopyTo(ms);
        ms.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    void TryInsertFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            InsertImage(Decode(fs));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn\u2019t add \u201C{Path.GetFileName(path)}\u201D.\n\n{ex.Message}",
                "Insert photo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void InsertImage(BitmapSource source)
    {
        if (_current is null) return;

        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Width = Math.Min(source.Width, MaxImageWidth)
        };

        if (!Editor.Selection.IsEmpty) Editor.Selection.Text = "";
        var position = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        var container = new InlineUIContainer(image, position);
        Editor.CaretPosition = container.ElementEnd;
        Editor.Focus();
    }
}
