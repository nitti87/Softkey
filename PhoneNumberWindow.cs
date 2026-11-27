using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

sealed record SavedPhoneNumber(SavedTextEntry Entry, string Number)
{
    public string Name => Entry.Name;
    readonly string digits = Digits(Number);

    public bool Matches(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return true;
        if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(Name, query, CompareOptions.IgnoreCase) >= 0) return true;
        return SavedTextStore.IsPhoneNumber(query) && digits.Contains(Digits(query), StringComparison.Ordinal);
    }

    static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());
}

sealed class PhoneNumberWindow : Window
{
    public const string ChooseLabel = "Välj sparat telefonnummer";
    internal const string PhoneIconGeometry = "M 3,2 L 7,1 L 9,6 L 6,8 Q 8,12 12,14 L 14,11 L 19,13 L 18,17 Q 17,20 14,18 Q 5,15 2,6 Q 1,3 3,2 Z";
    readonly SavedPhoneNumber[] numbers;
    readonly Func<SavedPhoneNumber, Task<string>> insert;
    readonly TextBox search = new()
    {
        FontSize = 13, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Padding = new Thickness(2, 6, 8, 6), VerticalContentAlignment = VerticalAlignment.Center,
        MaxLength = 512
    };
    readonly StackPanel rows = new();
    readonly ScrollViewer results;
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    bool inserting;

    public event Action InputAvailabilityChanged;
    public bool CanType => IsVisible && !inserting;

    public PhoneNumberWindow(SavedPhoneNumber[] numbers, Func<SavedPhoneNumber, Task<string>> insert)
    {
        this.numbers = numbers;
        this.insert = insert;
        Title = "Sparade telefonnummer";
        results = SavedSuggestionPickerDesign.ScrollCards(rows);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var body = new StackPanel { Children = { SearchField(), results, status } };
        SavedSuggestionPickerDesign.Configure(this, body);
        search.TextChanged += (_, _) => RefreshResults();
        Loaded += (_, _) => search.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        RefreshResults();
    }

    public bool FocusSearchInput() => CanType && (search.IsKeyboardFocusWithin || (Activate() && search.Focus()));

    Border SearchField()
    {
        var icon = new Path
        {
            Data = Geometry.Parse("M 11,11 L 15,15 M 12,7 A 5,5 0 1 1 2,7 A 5,5 0 1 1 12,7"),
            Stroke = SoftKeyPopupDesign.ActionText, StrokeThickness = 1.5, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 15, Height = 15, Margin = new Thickness(9, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
        };
        var placeholder = new TextBlock
        {
            Text = "Sök på namn eller nummer", Foreground = SoftKeyPopupDesign.ActionText, FontSize = 13,
            IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0)
        };
        AutomationProperties.SetName(search, placeholder.Text);
        DockPanel.SetDock(icon, Dock.Left);
        var content = new DockPanel { Children = { icon, new Grid { Children = { search, placeholder } } } };
        var field = new Border
        {
            Height = 35, Margin = new Thickness(7, 4, 7, 4), Background = SoftKeyPopupDesign.CardSurface,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            BorderBrush = SoftKeyPopupDesign.ControlOutline, Child = content
        };
        search.GotKeyboardFocus += (_, _) => field.BorderBrush = SoftKeyPopupDesign.Accent;
        search.LostKeyboardFocus += (_, _) => field.BorderBrush = SoftKeyPopupDesign.ControlOutline;
        search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return field;
    }

    void RefreshResults()
    {
        rows.Children.Clear();
        SetStatus(null);
        foreach (var number in numbers.Where(number => number.Matches(search.Text)))
        {
            var button = SavedSuggestionPickerDesign.Card(number.Name, PhoneIcon(16),
                number.Name == number.Number ? null : number.Number);
            button.Click += async (_, _) => await Insert(number);
            rows.Children.Add(button);
        }
        results.ScrollToTop();
        results.Visibility = rows.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Children.Count == 0)
            SetStatus(numbers.Length == 0 ? "Inga sparade telefonnummer. Lägg till nummer under Ordlista → Specifika förslag."
                : "Inga telefonnummer matchar sökningen.");
    }

    async Task Insert(SavedPhoneNumber number)
    {
        if (inserting || !IsVisible) return;
        inserting = true;
        InputAvailabilityChanged?.Invoke();
        SetStatus(null);
        try
        {
            string error = await insert(number);
            if (!IsVisible) return;
            if (error == null) Close();
            else SetStatus(error);
        }
        finally
        {
            inserting = false;
            InputAvailabilityChanged?.Invoke();
        }
    }

    void SetStatus(string message)
    {
        status.Text = message ?? "";
        status.Margin = new Thickness(7, 7, 7, 2);
        status.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static Path PhoneIcon(double size) => new()
    {
        Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Stroke = SoftKeyPopupDesign.ActionText, StrokeThickness = 1.65,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse(PhoneIconGeometry)
    };
}
