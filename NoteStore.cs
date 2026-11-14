using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace Notes;

/// <summary>
/// Everything is stored under %APPDATA%\Notes :
///   index.json          list of notes (title, dates, pin state, search text)
///   notes\{id}.xamlpkg  the note body, including any pasted photos
///   theme.txt           "dark" or "light"
/// </summary>
public sealed class NoteStore
{
    readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notes");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    string IndexPath => Path.Combine(_root, "index.json");
    string ThemePath => Path.Combine(_root, "theme.txt");
    string ContentPath(Guid id) => Path.Combine(_root, "notes", id + ".xamlpkg");

    public NoteStore()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
    }

    public List<NoteItem> LoadIndex()
    {
        if (!File.Exists(IndexPath)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(IndexPath)) ?? new();
        }
        catch
        {
            // Keep the unreadable file around instead of silently overwriting it.
            try { File.Copy(IndexPath, IndexPath + ".corrupt", true); } catch { }
            return new();
        }
    }

    public void SaveIndex(IEnumerable<NoteItem> notes)
    {
        var tmp = IndexPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(notes.ToList(), JsonOptions));
        File.Move(tmp, IndexPath, true);
    }

    public FlowDocument LoadDocument(Guid id)
    {
        var doc = new FlowDocument();
        var path = ContentPath(id);
        if (!File.Exists(path)) return doc;
        try
        {
            using var fs = File.OpenRead(path);
            new TextRange(doc.ContentStart, doc.ContentEnd).Load(fs, DataFormats.XamlPackage);
        }
        catch
        {
            doc = new FlowDocument();
        }
        return doc;
    }

    public void SaveDocument(Guid id, FlowDocument doc)
    {
        var path = ContentPath(id);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        {
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, DataFormats.XamlPackage);
        }
        File.Move(tmp, path, true);
    }

    public void DeleteDocument(Guid id)
    {
        try { File.Delete(ContentPath(id)); } catch { }
    }

    public bool? LoadTheme()
    {
        try
        {
            if (!File.Exists(ThemePath)) return null;
            return File.ReadAllText(ThemePath).Trim() == "dark";
        }
        catch { return null; }
    }

    public void SaveTheme(bool dark)
    {
        try { File.WriteAllText(ThemePath, dark ? "dark" : "light"); } catch { }
    }
}
