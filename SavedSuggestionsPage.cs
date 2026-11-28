using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

sealed class SavedSuggestionsPage : UserControl
{
    const string EnableSuggestionsLabel = "Aktivera specifika ordförslag";
    readonly SavedTextStore store;
    readonly bool hasTarget, passwordTarget;
    readonly Func<SavedTextEntry, Task<string>> useEntry;
    readonly Func<bool, bool> saveEnabled;
    readonly Action close;
    readonly ListBox entries = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly TextBlock empty = new() { Text = "Inga sparade förslag", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
    readonly TextBox name = new() { MaxLength = 55 };
    readonly TextBox text = new() { MaxLength = 512 };
    readonly PasswordBox secret = new() { MaxLength = 512 };
    readonly TextBlock nameLabel = FieldLabel("Namn i förslagslistan");
    readonly TextBlock valueLabel = FieldLabel("E-postadress");
    readonly TextBlock listFeedback = Feedback(), editFeedback = Feedback();
    readonly TextBlock toggleFeedback = Feedback();
    readonly Border listReveal = new() { ClipToBounds = true };
    readonly SoftKeyCheckBox enableSuggestions = SoftKeyCheckBox.SettingsRow(EnableSuggestionsLabel, "");
    readonly Dictionary<SavedTextKind, RadioButton> kinds = new();
    readonly Button edit = ActionButton("Redigera"), use = ActionButton("Använd", true), remove = ActionButton("Ta bort");
    readonly StackPanel listView, editorView;
    SavedTextEntry editingEntry;
    SavedTextKind selectedKind;
    Control activeField;
    bool usingEntry;
    bool suggestionsEnabled;
    int listTransition;

    public SavedSuggestionsPage(SavedTextStore store, bool hasTarget, bool passwordTarget,
        Func<SavedTextEntry, Task<string>> useEntry, Action close, bool suggestionsEnabled, Func<bool, bool> saveEnabled)
    {
        this.store = store;
        this.hasTarget = hasTarget;
        this.passwordTarget = passwordTarget;
        this.useEntry = useEntry;
        this.close = close;
        this.suggestionsEnabled = suggestionsEnabled;
        this.saveEnabled = saveEnabled;
        listView = BuildList();
        editorView = BuildEditor();
        editorView.Visibility = Visibility.Collapsed;
        var pages = new Grid();
        pages.Children.Add(listView);
        pages.Children.Add(editorView);
        Content = pages;
        SetListVisibility(animate: false);
        Reload();
    }

    public event Action EditingChanged;
    public bool IsEditing => editorView.Visibility == Visibility.Visible;

    public bool FocusInput() => IsEditing && (activeField is { IsVisible: true } ? activeField : name).Focus();
    public void ClearSecret() => secret.Clear();

    StackPanel BuildList()
    {
        var add = ActionButton("Nytt förslag");
        add.Width = 105;
        add.Click += (_, _) => OpenEditor(null);
        edit.Click += (_, _) => { if (entries.SelectedItem is SavedTextEntry entry) OpenEditor(entry); };
        use.Click += async (_, _) => await UseEntry();
        ToolTipService.SetShowOnDisabled(use, true);
        entries.SelectionChanged += (_, _) => { ShowFeedback(listFeedback); RefreshListActions(); };
        var done = ActionButton("Stäng");
        done.Click += (_, _) => close();
        var heading = FieldLabel("Sparade förslag");
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.Margin = new Thickness(0);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(add, Dock.Right);
        top.Children.Add(add);
        top.Children.Add(heading);

        enableSuggestions.Content = new TextBlock
        {
            Text = EnableSuggestionsLabel, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        };
        enableSuggestions.MinHeight = 44;
        enableSuggestions.Margin = new Thickness(16, 12, 16, 3);
        enableSuggestions.IsChecked = suggestionsEnabled;
        AutomationProperties.SetName(enableSuggestions, EnableSuggestionsLabel);
        enableSuggestions.Checked += ChangeSuggestionsEnabled;
        enableSuggestions.Unchecked += ChangeSuggestionsEnabled;
        toggleFeedback.Margin = new Thickness(16, 0, 16, 8);

        var separator = SoftKeyPopupDesign.Separator(new Thickness(16, 0, 16, 0));
        separator.Opacity = 0.85;
        var body = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Child = new StackPanel { Children = { top, EntryList(), listFeedback } }
        };
        listReveal.Child = new StackPanel { Children = { separator, body } };
        return SoftKeyPopupDesign.Sections(double.NaN,
            new StackPanel { Children = { enableSuggestions, toggleFeedback, listReveal } }, SoftKeyButtonDesign.Row(done, edit, use), new Thickness(0));
    }

    void ChangeSuggestionsEnabled(object sender, RoutedEventArgs e)
    {
        bool enabled = enableSuggestions.IsChecked == true;
        if (enabled == suggestionsEnabled) return;
        if (!saveEnabled(enabled))
        {
            enableSuggestions.IsChecked = suggestionsEnabled;
            ShowFeedback(toggleFeedback, "Kunde inte spara inställningen. Försök igen.", true);
            return;
        }
        suggestionsEnabled = enabled;
        ShowFeedback(toggleFeedback);
        if (!enabled) ShowFeedback(listFeedback);
        SetListVisibility(animate: true);
    }

    void SetListVisibility(bool animate)
    {
        bool enabled = suggestionsEnabled;
        int transition = ++listTransition;
        var body = (FrameworkElement)listReveal.Child;
        double startHeight = listReveal.Visibility == Visibility.Visible ? listReveal.ActualHeight : 0;
        double startOpacity = listReveal.Opacity;
        edit.Visibility = use.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        listReveal.IsEnabled = enabled;

        void Finish()
        {
            listReveal.BeginAnimation(HeightProperty, null);
            listReveal.BeginAnimation(OpacityProperty, null);
            body.Height = double.NaN;
            listReveal.Height = double.NaN;
            listReveal.Opacity = enabled ? 1 : 0;
            listReveal.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!animate || !SystemParameters.ClientAreaAnimation || !IsVisible || listView.ActualWidth <= 0)
        {
            Finish();
            return;
        }

        body.Height = double.NaN;
        body.Measure(new Size(listView.ActualWidth, double.PositiveInfinity));
        double expandedHeight = body.DesiredSize.Height;
        body.Height = expandedHeight;
        listReveal.Visibility = Visibility.Visible;
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var height = new DoubleAnimation(startHeight, enabled ? expandedHeight : 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        height.Completed += (_, _) => { if (transition == listTransition) Finish(); };
        listReveal.BeginAnimation(HeightProperty, height);
        listReveal.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, enabled ? 1 : 0, duration));
    }

    StackPanel BuildEditor()
    {
        var form = new StackPanel();
        form.Children.Add(FieldLabel("Typ"));
        var choices = new UniformGrid { Rows = 1 };
        foreach (var (kind, label) in new[] { (SavedTextKind.Email, "E-post"), (SavedTextKind.Password, "Lösenord"),
            (SavedTextKind.Phone, "Telefonnummer") })
        {
            var choice = new RadioButton { Content = label, MinHeight = 37,
                Template = ChoiceTemplate(typeof(RadioButton), ToggleButton.IsCheckedProperty, HorizontalAlignment.Center),
                Margin = new Thickness(0, 0, kind == SavedTextKind.Phone ? 0 : 5, 0), FontSize = 12 };
            choice.Checked += (_, _) => { selectedKind = kind; RefreshValueField(); };
            kinds.Add(kind, choice);
            choices.Children.Add(choice);
        }
        form.Children.Add(choices);
        nameLabel.Margin = new Thickness(0, 12, 0, 5);
        form.Children.Add(nameLabel);
        form.Children.Add(InputFrame(name));
        valueLabel.Margin = new Thickness(0, 12, 0, 5);
        form.Children.Add(valueLabel);
        form.Children.Add(InputFrame(text));
        form.Children.Add(InputFrame(secret));
        AutomationProperties.SetName(secret, "Lösenord");
        foreach (var field in new Control[] { name, text, secret }) field.GotKeyboardFocus += (_, _) => activeField = field;
        name.TextChanged += (_, _) => ShowFeedback(editFeedback);
        text.TextChanged += (_, _) => ShowFeedback(editFeedback);
        secret.PasswordChanged += (_, _) => ShowFeedback(editFeedback);
        form.Children.Add(editFeedback);

        remove.Click += (_, _) => RemoveEntry();
        var cancel = ActionButton("Avbryt");
        cancel.Click += (_, _) => CloseEditor();
        var save = ActionButton("Spara", true);
        save.Click += (_, _) => SaveEntry();
        return SoftKeyPopupDesign.Sections(double.NaN, form, SoftKeyButtonDesign.Row(remove, cancel, save));
    }

    Border EntryList()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var iconTile = new FrameworkElementFactory(typeof(Border));
        iconTile.SetValue(DockPanel.DockProperty, Dock.Left);
        iconTile.SetValue(WidthProperty, 36.0);
        iconTile.SetValue(HeightProperty, 36.0);
        iconTile.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        iconTile.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.ActionSurface);
        iconTile.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        iconTile.SetValue(MarginProperty, new Thickness(0, 0, 12, 0));
        iconTile.SetValue(IsHitTestVisibleProperty, false);

        var icon = new FrameworkElementFactory(typeof(Path));
        icon.SetValue(WidthProperty, 18.0);
        icon.SetValue(HeightProperty, 18.0);
        icon.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        icon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(Shape.StretchProperty, Stretch.Uniform);
        icon.SetValue(Shape.StrokeProperty, SoftKeyPopupDesign.Accent);
        icon.SetValue(Shape.StrokeThicknessProperty, 1.5);
        icon.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        icon.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        icon.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        var iconStyle = new Style(typeof(Path));
        foreach (var (kind, drawing) in new[]
        {
            (SavedTextKind.Email, "M 2.5,5.5 Q 2.5,4 4,4 H 16 Q 17.5,4 17.5,5.5 V 14.5 Q 17.5,16 16,16 H 4 Q 2.5,16 2.5,14.5 Z M 3.2,5.2 L 10,10.5 L 16.8,5.2"),
            (SavedTextKind.Password, SoftKeySettingsDesign.LockGlyph),
            (SavedTextKind.Phone, PhoneNumberWindow.PhoneIconGeometry)
        })
        {
            var state = new DataTrigger { Binding = new Binding(nameof(SavedTextEntry.Kind)), Value = kind };
            state.Setters.Add(new Setter(Path.DataProperty, Geometry.Parse(drawing)));
            iconStyle.Triggers.Add(state);
        }
        icon.SetValue(StyleProperty, iconStyle);
        iconTile.AppendChild(icon);
        row.AppendChild(iconTile);

        var labels = new FrameworkElementFactory(typeof(StackPanel));
        labels.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(SavedTextEntry.Name)));
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        label.SetValue(TextBlock.ForegroundProperty, SoftKeyPopupDesign.PrimaryText);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        labels.AppendChild(label);
        var type = new FrameworkElementFactory(typeof(TextBlock));
        type.SetBinding(TextBlock.TextProperty, new Binding(nameof(SavedTextEntry.KindLabel)));
        type.SetValue(TextBlock.ForegroundProperty, SoftKeyPopupDesign.SecondaryText);
        type.SetValue(TextBlock.FontSizeProperty, 12.0);
        type.SetValue(MarginProperty, new Thickness(0, 1.5, 0, 0));
        labels.AppendChild(type);
        row.AppendChild(labels);
        entries.ItemTemplate = new DataTemplate { VisualTree = row };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(MinHeightProperty, 54.0));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(10, 5, 11, 5)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(3, 2, 3, 2)));
        style.Setters.Add(new Setter(TemplateProperty, ChoiceTemplate(typeof(ListBoxItem), ListBoxItem.IsSelectedProperty)));
        entries.ItemContainerStyle = style;
        entries.FontSize = 13;
        AutomationProperties.SetName(entries, "Sparade specifika förslag");
        ScrollViewer.SetHorizontalScrollBarVisibility(entries, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(entries, ScrollBarVisibility.Auto);
        var body = new Grid { MinHeight = 151, MaxHeight = 208 };
        body.Children.Add(entries);
        body.Children.Add(empty);
        empty.Foreground = SoftKeyPopupDesign.SecondaryText;
        return new Border
        {
            Child = body, Background = SoftKeyPopupDesign.CardSurface,
            BorderBrush = SoftKeyPopupDesign.CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(2, 2, 2, 2)
        };
    }

    static Button ActionButton(string label, bool primary = false)
    {
        var button = SoftKeyButtonDesign.Action(label, primary);
        button.Focusable = false;
        button.Width = 75;
        button.Height = 33;
        return button;
    }

    static TextBlock FieldLabel(string label) => new()
    {
        Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5)
    };

    static TextBlock Feedback()
    {
        var item = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(item, AutomationLiveSetting.Polite);
        return item;
    }

    static Border InputFrame(Control input)
    {
        input.Height = 33;
        input.FontSize = 13;
        input.Padding = new Thickness(8, 3, 8, 3);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.BorderThickness = new Thickness(0);
        input.Background = Brushes.Transparent;
        var frame = new Border { Child = input, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.ControlOutline };
        frame.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = input });
        input.GotKeyboardFocus += (_, _) => frame.BorderBrush = SoftKeyPopupDesign.Accent;
        input.LostKeyboardFocus += (_, _) => frame.BorderBrush = SoftKeyPopupDesign.ControlOutline;
        return frame;
    }

    static ControlTemplate ChoiceTemplate(Type type, DependencyProperty selected,
        HorizontalAlignment alignment = HorizontalAlignment.Stretch)
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetValue(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.CardBorder);
        surface.SetValue(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.CardSurface);
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(SoftKeyPopupDesign.CardCornerRadius));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(MarginProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        content.SetValue(HorizontalAlignmentProperty, alignment);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);
        var template = new ControlTemplate(type) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.CardHoverSurface, "Surface"));
        hover.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.CardHoverBorder, "Surface"));
        template.Triggers.Add(hover);
        var active = new Trigger { Property = selected, Value = true };
        active.Setters.Add(new Setter(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.ActionSurface, "Surface"));
        active.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.ActionBorder, "Surface"));
        template.Triggers.Add(active);
        var activeHover = new MultiTrigger
        {
            Conditions = { new(selected, true), new(IsMouseOverProperty, true), new(IsEnabledProperty, true) }
        };
        activeHover.Setters.Add(new Setter(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.ActionHover, "Surface"));
        template.Triggers.Add(activeHover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.Accent, "Surface"));
        template.Triggers.Add(focus);
        return template;
    }

    void RefreshValueField()
    {
        bool password = selectedKind == SavedTextKind.Password;
        nameLabel.Text = selectedKind == SavedTextKind.Phone ? "Namn i förslagslistan (valfritt)" : "Namn i förslagslistan";
        AutomationProperties.SetName(name, nameLabel.Text);
        valueLabel.Text = selectedKind switch
        {
            SavedTextKind.Password => "Lösenord",
            SavedTextKind.Phone => "Telefonnummer",
            _ => "E-postadress"
        };
        AutomationProperties.SetName(text, valueLabel.Text);
        text.Visibility = password ? Visibility.Collapsed : Visibility.Visible;
        secret.Visibility = password ? Visibility.Visible : Visibility.Collapsed;
        text.Clear();
        ClearSecret();
    }

    void OpenEditor(SavedTextEntry entry)
    {
        editingEntry = entry;
        selectedKind = entry?.Kind ?? SavedTextKind.Email;
        kinds[selectedKind].IsChecked = true;
        RefreshValueField();
        name.Text = entry?.Name ?? "";
        ShowFeedback(editFeedback);
        if (entry is { Kind: not SavedTextKind.Password })
        {
            var value = SavedTextStore.Open(entry);
            if (value == null) ShowFeedback(editFeedback, "Kunde inte öppna förslaget.", true);
            else
            {
                try { text.Text = Encoding.Unicode.GetString(value); }
                finally { CryptographicOperations.ZeroMemory(value); }
            }
        }
        remove.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
        listView.Visibility = Visibility.Collapsed;
        editorView.Visibility = Visibility.Visible;
        activeField = name;
        name.Focus();
        EditingChanged?.Invoke();
    }

    public void CloseEditor()
    {
        ClearSecret();
        text.Clear();
        editingEntry = null;
        editorView.Visibility = Visibility.Collapsed;
        listView.Visibility = Visibility.Visible;
        EditingChanged?.Invoke();
    }

    void Reload(Guid? select = null)
    {
        var items = store.Read();
        entries.ItemsSource = items;
        entries.SelectedItem = Array.Find(items, entry => entry.Id == select);
        empty.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshListActions();
    }

    void RefreshListActions()
    {
        var entry = entries.SelectedItem as SavedTextEntry;
        edit.IsEnabled = !usingEntry && entry != null;
        use.IsEnabled = !usingEntry && hasTarget && entry != null
            && (entry.Kind != SavedTextKind.Password || passwordTarget);
        edit.Opacity = edit.IsEnabled ? 1 : 0.55;
        use.Opacity = use.IsEnabled ? 1 : 0.55;
        use.ToolTip = entry == null ? "Välj ett sparat förslag."
            : !hasTarget || (entry.Kind == SavedTextKind.Password && !passwordTarget)
                ? "Välj rätt fält i programmet och öppna ordlistan igen."
                : "Fyll i förslaget i fältet du valde innan du öppnade ordlistan.";
    }

    async Task UseEntry()
    {
        if (!suggestionsEnabled || !use.IsEnabled || entries.SelectedItem is not SavedTextEntry entry) return;
        usingEntry = true;
        RefreshListActions();
        try
        {
            string error = await useEntry(entry);
            if (error == null) close();
            else ShowFeedback(listFeedback, error, true);
        }
        finally { usingEntry = false; RefreshListActions(); }
    }

    void SaveEntry()
    {
        var type = selectedKind;
        string label = name.Text.Trim();
        if (label.Length == 0)
        {
            if (type != SavedTextKind.Phone) { ShowFeedback(editFeedback, "Ge förslaget ett namn.", true); name.Focus(); return; }
            label = text.Text.Trim();
        }
        var id = editingEntry?.Id ?? Guid.NewGuid();
        if (store.Read().Any(entry => entry.Id != id && entry.Kind == type
            && string.Equals(entry.Name, label, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowFeedback(editFeedback, "Det finns redan ett förslag med det namnet.", true);
            name.Focus();
            return;
        }
        byte[] value;
        if (type == SavedTextKind.Password)
        {
            using var password = secret.SecurePassword;
            if (password.Length == 0) { ShowFeedback(editFeedback, "Skriv lösenordet som ska sparas.", true); secret.Focus(); return; }
            var memory = Marshal.SecureStringToGlobalAllocUnicode(password);
            try
            {
                value = new byte[password.Length * 2];
                Marshal.Copy(memory, value, 0, value.Length);
            }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(memory); }
        }
        else
        {
            string input = text.Text.Trim();
            bool valid = type == SavedTextKind.Email
                ? MailAddress.TryCreate(input, out var address) && address.Address == input
                    : SavedTextStore.IsPhoneNumber(input);
            if (!valid)
            {
                ShowFeedback(editFeedback, type == SavedTextKind.Email ? "Skriv hela e-postadressen." : "Skriv ett telefonnummer.", true);
                text.Focus();
                return;
            }
            value = Encoding.Unicode.GetBytes(input);
        }
        try
        {
            for (int i = 0; i < value.Length; i += 2)
                if (char.IsControl((char)(value[i] | (value[i + 1] << 8))))
                {
                    ShowFeedback(editFeedback, "Använd ett värde på en rad.", true);
                    return;
                }
            if (!store.Save(id, label, type, value))
            {
                ShowFeedback(editFeedback, "Kunde inte spara förslaget. Försök igen.", true);
                return;
            }
            CloseEditor();
            Reload(id);
            ShowFeedback(listFeedback, "Förslaget är sparat.");
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }

    void RemoveEntry()
    {
        if (editingEntry == null) return;
        if (!store.Remove(editingEntry.Id)) { ShowFeedback(editFeedback, "Kunde inte ta bort förslaget. Försök igen.", true); return; }
        CloseEditor();
        Reload();
        ShowFeedback(listFeedback, "Förslaget är borttaget.");
    }

    static void ShowFeedback(TextBlock feedback, string message = null, bool error = false)
    {
        feedback.Text = message ?? "";
        feedback.Foreground = error ? SoftKeyPopupDesign.ErrorText : SoftKeyPopupDesign.PrimaryText;
        feedback.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
