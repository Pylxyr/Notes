using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Notes;

/// <summary>Metadata for one note. The rich content (text + photos) lives in its own file, see NoteStore.</summary>
public class NoteItem : INotifyPropertyChanged
{
    string _title = "";
    string _text = "";
    bool _pinned;
    bool _hasImages;
    DateTime _modified = DateTime.Now;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Created { get; set; } = DateTime.Now;

    public DateTime Modified
    {
        get => _modified;
        set { _modified = value; On(); On(nameof(ModifiedText)); }
    }

    public string Title
    {
        get => _title;
        set { _title = value ?? ""; On(); On(nameof(DisplayTitle)); }
    }

    /// <summary>Plain-text copy of the note (capped), used for search and the list preview.</summary>
    public string Text
    {
        get => _text;
        set { _text = value ?? ""; On(); On(nameof(Snippet)); }
    }

    public bool Pinned
    {
        get => _pinned;
        set { _pinned = value; On(); }
    }

    public bool HasImages
    {
        get => _hasImages;
        set { _hasImages = value; On(); On(nameof(Snippet)); }
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title.Trim();

    [JsonIgnore]
    public string Snippet
    {
        get
        {
            var flat = Regex.Replace(Text, @"\s+", " ").Trim();
            if (flat.Length == 0) return HasImages ? "Photo" : "No additional text";
            return flat.Length > 110 ? flat[..110] : flat;
        }
    }

    [JsonIgnore]
    public string ModifiedText
    {
        get
        {
            var now = DateTime.Now;
            if (Modified.Date == now.Date) return Modified.ToString("t");
            if (Modified.Date == now.Date.AddDays(-1)) return "Yesterday";
            return Modified.Year == now.Year ? Modified.ToString("MMM d") : Modified.ToString("MMM d, yyyy");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void On([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
