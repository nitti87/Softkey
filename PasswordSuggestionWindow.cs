using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

sealed class PasswordSuggestionWindow : Window
{
    public const string ChooseLabel = "Välj sparat lösenord";
    readonly Func<Guid, Task<string>> fill;
    readonly StackPanel body = new();
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    public bool IsUnlocked { get; private set; }

    public PasswordSuggestionWindow(Func<Guid, Task<string>> fill)
    {
        this.fill = fill;
        Title = "Sparade lösenord";
        SetStatus("Öppnar lösenordslistan…");
        body.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        SavedSuggestionPickerDesign.Configure(this, body);
    }

    public void ShowEntries((Guid Id, string Name)[] entries)
    {
        IsUnlocked = true;
        if (entries.Length == 0)
        {
            SetStatus("Inga sparade lösenord. Lägg till ett under Tangentbord → Ordlista → Specifika förslag.");
            Recenter();
            return;
        }
        SetStatus(null);
        var rows = new StackPanel();
        status.Margin = new Thickness(2, 7, 2, 2);
        foreach (var (id, name) in entries)
        {
            string initial = StringInfo.GetNextTextElement(name.Trim()).ToUpper(CultureInfo.CurrentCulture);
            var row = SavedSuggestionPickerDesign.Card(name, new TextBlock
            {
                Text = initial, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyPopupDesign.ActionText,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            row.Click += async (_, _) =>
            {
                if (!rows.IsEnabled || !IsVisible) return;
                rows.IsEnabled = false;
                SetStatus(null);
                string error = await fill(id);
                if (!IsVisible) return;
                if (error == null) Close();
                else
                {
                    SetStatus(error);
                    rows.IsEnabled = true;
                }
            };
            rows.Children.Add(row);
        }
        body.Children.Insert(0, SavedSuggestionPickerDesign.ScrollCards(rows));
        Recenter();
    }

    public void SetStatus(string message)
    {
        status.Text = message ?? "";
        status.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Recenter()
    {
        if (!IsLoaded || !IsVisible || WindowStartupLocation != WindowStartupLocation.CenterScreen) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsVisible) SoftKeyPopupDesign.CenterOnOwnerMonitor(this);
        }));
    }

    internal static Path LockIcon(double size, Brush stroke = null) => new()
    {
        Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false,
        Stroke = stroke ?? SoftKeyPopupDesign.ActionText, StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse("M 5,9 V 6 A 5,5 0 0 1 15,6 V 9 M 3,9 H 17 V 19 H 3 Z M 10,13 V 15")
    };
}
