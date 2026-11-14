using System.Windows;

namespace Notes;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow().Show();
    }

    /// <summary>Swaps the colour palette. All brushes are DynamicResources, so the UI updates live.</summary>
    public static void ApplyTheme(bool dark)
    {
        var name = dark ? "Dark" : "Light";
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Notes;component/Themes/{name}.xaml", UriKind.Absolute)
        };
        var merged = Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(palette);
    }
}
