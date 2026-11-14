# Notes

A small, fast notebook for Windows. Write notes, format them a little, and paste photos straight into the text.

- Two panes: a searchable list of notes on the left, the editor on the right
- **Paste photos** with Ctrl+V (screenshots, "Copy image" from a browser, image files copied in Explorer). Drag and drop works too, and there is an insert button in the toolbar
- Bold, italic, underline, bulleted and numbered lists
- Pin notes to the top, search by title or text
- Light and dark themes (follows Windows the first time, then remembers your choice)
- Autosaves as you type, nothing to press

Text copied from other apps is pasted as plain text so it always matches the theme. Text copied inside Notes keeps its formatting.

## Shortcuts

| Key | Action |
| --- | --- |
| Ctrl+N | New note |
| Ctrl+F | Search |
| Ctrl+S | Save now (it already autosaves) |
| Ctrl+B / I / U | Bold / italic / underline |
| Ctrl+V | Paste text or a photo |

## Where your notes live

`%APPDATA%\Notes`. Each note is one file in the `notes` folder (photos are stored inside it), plus an `index.json` with titles and dates. Back that folder up and you have backed up everything.

## Build it yourself

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet run
```

A single self-contained `Notes.exe` (no .NET install needed to run it):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Releases with GitHub Actions

`.github/workflows/build.yml` builds the exe on every push and pull request (download it from the run's Artifacts).
To publish a release, push a tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow then attaches `Notes.exe` to a new GitHub Release.
