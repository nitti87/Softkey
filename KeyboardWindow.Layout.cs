using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Bygger WPF-gränssnittet från tangentbeskrivningarna i KeyboardLayout.
partial class KeyboardWindow
{
    const double DesktopHeaderGap = 4, SignInHeaderGap = 8, KeyboardInset = 8;
    const double FunctionGroupGap = 0.24, NavigationGroupGap = 4;
    static readonly Brush KeyboardTray = SoftKeyKeyDesign.TraySurface;
    Grid keyboardKeyGrid;
    RowDefinition headerSpacing;

    internal static void SetKeySpacing(Button button, int horizontal, int vertical, int fontSizePercent)
    {
        horizontal = KeyboardLayout.ScaledGap(horizontal, fontSizePercent);
        vertical = KeyboardLayout.ScaledGap(vertical, fontSizePercent);

        // Use part of the visible gap for the inset so the two effects do not add up.
        const int maxFaceInset = 3;
        int fontRange = Settings.MaxKeyFontSizePercent - Settings.MinKeyFontSizePercent;
        int inset = button.Content is string ? maxFaceInset * (Settings.MaxKeyFontSizePercent - fontSizePercent) / fontRange : 0;
        int insetX = Math.Min(inset, (horizontal - Settings.MinKeyGap) / 2);
        int insetY = Math.Min(inset, (vertical - Settings.MinKeyGap) / 2);
        button.SetValue(KeyboardKeySurface.InsetProperty, new Size(insetX, insetY));
        horizontal -= 2 * insetX;
        vertical -= 2 * insetY;

        // Split odd values into whole pixels so rounding does not lose a one-pixel gap.
        int left = horizontal / 2, top = vertical / 2;
        button.Margin = new Thickness(left, top, horizontal - left, vertical - top);
    }

    void ApplyKeyboardSpacing()
    {
        foreach (var button in btn.Values) SetKeySpacing(button, cfg.HorizontalKeyGap, cfg.VerticalKeyGap, cfg.KeyFontSizePercent);
        if (phoneNumberKey != null) SetKeySpacing(phoneNumberKey, cfg.HorizontalKeyGap, cfg.VerticalKeyGap, cfg.KeyFontSizePercent);
    }

    UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(KeyboardInset, 0, KeyboardInset, 0) };
        root.ColumnDefinitions.Add(secureMode ? new ColumnDefinition { Width = new GridLength(0) } : Col(KeyboardLayout.SuggestionColumnWeight));
        root.ColumnDefinitions.Add(Col(KeyboardLayout.KeyAreaWeight));
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerSpacing = new RowDefinition { Height = new GridLength(secureMode ? SignInHeaderGap : DesktopHeaderGap) };
        root.RowDefinitions.Add(headerSpacing);
        root.RowDefinitions.Add(Row1());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StatusRowHeight) });
        var keys = keyboardKeyGrid = BuildKeyDeck(KeyBtn, out var navigation);

        var suggestionList = new StackPanel();
        var suggestionText = new FrameworkElementFactory(typeof(TextBlock));
        suggestionText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        suggestionText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        suggestionText.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        suggestionText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var suggestionTemplate = new DataTemplate { VisualTree = suggestionText };
        var suggestionButtonTemplate = KeyButtonTemplate(HorizontalAlignment.Stretch, cornerRadius: 4);
        for (int i = 0; i < sugg.Length; i++)
        {
            int n = i;
            sugg[i] = Btn("", SoftKeyPopupDesign.PrimaryText, () => PickWord(n));
            sugg[i].HorizontalContentAlignment = HorizontalAlignment.Stretch;
            sugg[i].ContentTemplate = suggestionTemplate;
            sugg[i].Template = suggestionButtonTemplate;
            sugg[i].FontWeight = FontWeights.Normal;
            sugg[i].Margin = new Thickness(0, 1, 0, 1);
            sugg[i].MinHeight = 26;
            sugg[i].Padding = new Thickness(5, 4, 5, 4);
            sugg[i].IsEnabled = false;
            sugg[i].Visibility = Visibility.Collapsed;
            RefreshButtonVisual(sugg[i], false);
            suggestionList.Children.Add(sugg[i]);
        }
        suggestionViewport = new ScrollViewer
        {
            Content = suggestionList, Margin = new Thickness(0, 2, 6, 2), Visibility = Visibility.Collapsed,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false
        };
        var suggestionArea = new Grid();
        suggestionArea.Children.Add(suggestionViewport);
        var emptySuggestionsDrawing = new GeometryDrawing
        {
            Geometry = Geometry.Parse("M 6,4 H 18 A 3,3 0 0 1 21,7 V 14 A 3,3 0 0 1 18,17 H 10 L 5,21 V 16.8 A 3,3 0 0 1 3,14 V 7 A 3,3 0 0 1 6,4 Z M 9,10.5 H 15"),
            Pen = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x76, 0x6E)), 1.4)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            }
        };
        var emptySuggestionsSource = new DrawingImage(emptySuggestionsDrawing);
        emptySuggestionsSource.Freeze();
        emptySuggestionsIcon = new Image
        {
            Source = emptySuggestionsSource, Width = 26, Height = 26,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = suggestionViewport.Margin, IsHitTestVisible = false
        };
        System.Windows.Automation.AutomationProperties.SetName(emptySuggestionsIcon, "Inga ordförslag");
        suggestionArea.Children.Add(emptySuggestionsIcon);
        var emptySuggestionsSeparator = new Border
        {
            Width = 1, Background = SoftKeyKeyDesign.MutedDivider, Opacity = 0.65,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 3, 8), IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        emptySuggestionsSeparator.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(UIElement.Visibility)) { Source = emptySuggestionsIcon });
        suggestionArea.Children.Add(emptySuggestionsSeparator);
        if (!secureMode)
        {
            var caption = new TextBlock
            {
                Text = PasswordSuggestionWindow.ChooseLabel, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0)
            };
            var content = new StackPanel();
            content.Children.Add(PasswordSuggestionWindow.LockIcon(22));
            content.Children.Add(caption);
            passwordSuggestionAction = SoftKeyButtonDesign.Action(PasswordSuggestionWindow.ChooseLabel, primary: true,
                contentAlignment: HorizontalAlignment.Stretch, animateHover: false);
            passwordSuggestionAction.Content = content;
            passwordSuggestionAction.Width = passwordSuggestionAction.Height = double.NaN;
            passwordSuggestionAction.MinHeight = 76;
            passwordSuggestionAction.FontWeight = FontWeights.SemiBold;
            passwordSuggestionAction.Padding = new Thickness(4, 8, 4, 8);
            passwordSuggestionAction.Margin = suggestionViewport.Margin;
            passwordSuggestionAction.VerticalAlignment = VerticalAlignment.Top;
            passwordSuggestionAction.Focusable = false;
            passwordSuggestionAction.Visibility = Visibility.Collapsed;
            passwordSuggestionAction.Click += (_, _) => OpenPasswordPicker();
            System.Windows.Automation.AutomationProperties.SetName(passwordSuggestionAction, PasswordSuggestionWindow.ChooseLabel);
            suggestionArea.Children.Add(passwordSuggestionAction);
        }
        suggestionArea.SizeChanged += (_, e) =>
        {
            double iconSize = Math.Clamp(Math.Min(e.NewSize.Width, e.NewSize.Height) * 0.3, 18, 30);
            emptySuggestionsIcon.Width = emptySuggestionsIcon.Height = iconSize;
            double inset = suggestionViewport.Margin.Top + suggestionViewport.Margin.Bottom;
            double rowHeight = Math.Max(0, (e.NewSize.Height - inset) / sugg.Length);
            foreach (var button in sugg)
                button.MinHeight = Math.Max(26, Math.Floor(rowHeight - button.Margin.Top - button.Margin.Bottom));
        };
        Grid.SetColumn(suggestionArea, 0); Grid.SetRow(suggestionArea, 2);
        root.Children.Add(suggestionArea);

        // Build the shared menu and status areas.
        var menu = TopMenu();
        // Both hosts share the key layout. Only the small sign-in shell differs.
        var topBar = secureMode ? BuildSignInMoveGrip() : BuildDesktopHeader(menu);

        var status = StatusRow();
        Grid.SetColumn(status, 0); Grid.SetColumnSpan(status, root.ColumnDefinitions.Count); Grid.SetRow(status, 3);
        root.Children.Add(status);

        wordMenuAnchor = new Border { Background = Brushes.Transparent };
        wordMenuAnchor.MouseLeftButtonDown += (_, e) =>
        {
            if (!CanSaveCurrentWord) return;
            ToggleWordMenu(e.GetPosition(wordMenuAnchor));
            e.Handled = true;
        };
        Grid.SetColumn(wordMenuAnchor, 1); Grid.SetRow(wordMenuAnchor, 0); Grid.SetRowSpan(wordMenuAnchor, KeyboardLayout.Rows.Length);
        keys.Children.Add(wordMenuAnchor);

        BuildPhoneNumberKeySlot(navigation);

        Grid.SetColumn(topBar, 0); Grid.SetColumnSpan(topBar, root.ColumnDefinitions.Count); Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        var keyLayout = BuildDragKeyLayout(keys);
        Grid.SetColumn(keyLayout, 1); Grid.SetRow(keyLayout, 2);
        root.Children.Add(keyLayout);
        keys.SizeChanged += (_, _) =>
        {
            UpdateKeyboardTextForSize();
            RememberDesktopKeyboardArea();
        };
        return root;
    }

    KeyboardAreaSize? CurrentKeyboardArea() => KeyboardAreaSize.Measure(keyboardKeyGrid.ActualWidth, keyboardKeyGrid.ActualHeight);

    void RememberDesktopKeyboardArea()
    {
        if (secureMode || !useInstalledLayout || !positionTrackingReady || desktopSessionSuspended || keyboardHidden
            || WindowState != WindowState.Normal || CurrentKeyboardArea() is not { } measured || cfg.KeyboardArea == measured) return;
        cfg.KeyboardArea = measured;
        positionSaveTimer.Stop();
        positionSaveTimer.Start();
    }

    internal static Grid BuildKeyDeck(Func<KeyDef, Button> createKey, out Grid navigation)
    {
        var deck = new Grid();
        deck.ColumnDefinitions.Add(Col(KeyboardLayout.KeyboardColumnWeight));
        deck.ColumnDefinitions.Add(Col(KeyboardLayout.NavigationGapWeight));
        deck.ColumnDefinitions.Add(Col(KeyboardLayout.NavigationColumnWeight));
        var tray = new Border
        {
            Background = KeyboardTray, CornerRadius = new CornerRadius(10),
            Margin = new Thickness(-2, 0, -2, 0), IsHitTestVisible = false, SnapsToDevicePixels = true
        };
        Grid.SetColumnSpan(tray, 3);
        Grid.SetRowSpan(tray, KeyboardLayout.Rows.Length);
        deck.Children.Add(tray);
        for (int i = 0; i < KeyboardLayout.Rows.Length; i++)
        {
            deck.RowDefinitions.Add(Row1());
            var row = BuildKeyRow(KeyboardLayout.Rows[i], createKey, groupFunctions: i == 0);
            Grid.SetRow(row, i);
            deck.Children.Add(row);
        }

        navigation = new Grid();
        for (int i = 0; i < KeyboardLayout.NavigationKeys.Length; i++)
        {
            if (i > 0 && i % 2 == 0)
                navigation.RowDefinitions.Add(new RowDefinition { Height = new GridLength(NavigationGroupGap) });
            var button = createKey(KeyboardLayout.NavigationKeys[i]);
            Grid.SetRow(button, navigation.RowDefinitions.Count);
            navigation.RowDefinitions.Add(Row1());
            navigation.Children.Add(button);
        }
        // The desktop phone shortcut has a reserved row so the other keys keep their positions.
        navigation.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
        navigation.RowDefinitions.Add(Row1());
        Grid.SetColumn(navigation, 2);
        Grid.SetRowSpan(navigation, KeyboardLayout.Rows.Length);
        deck.Children.Add(navigation);
        return deck;
    }

    static Grid BuildKeyRow(KeyDef[] keys, Func<KeyDef, Button> createKey, bool groupFunctions)
    {
        var grid = new Grid();
        for (int i = 0; i < keys.Length; i++)
        {
            if (groupFunctions && i > 0 && i % 4 == 0) grid.ColumnDefinitions.Add(Col(FunctionGroupGap));
            var button = createKey(keys[i]);
            Grid.SetColumn(button, grid.ColumnDefinitions.Count);
            grid.ColumnDefinitions.Add(Col(keys[i].W));
            grid.Children.Add(button);
        }
        return grid;
    }
}
