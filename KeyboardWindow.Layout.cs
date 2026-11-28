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
            Content = suggestionList, Margin = new Thickness(3, 3, 9, 3), Visibility = Visibility.Collapsed,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false
        };
        var suggestionArea = new Grid();
        if (!secureMode)
        {
            var suggestionTrayBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xDF, 0xD7));
            suggestionTrayBrush.Freeze();
            suggestionArea.Children.Add(new Border
            {
                Background = suggestionTrayBrush,
                BorderBrush = SoftKeyKeyDesign.MutedDivider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 6, 0),
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            });
        }
        suggestionArea.Children.Add(suggestionViewport);
        var emptyStrokeBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3A, 0x35));
        emptyStrokeBrush.Freeze();
        var emptySuggestionsDrawing = new GeometryDrawing
        {
            Geometry = Geometry.Parse("M 6.2,3.5 V 7.5 M 4.2,5.5 H 8.2 M 11,5.5 H 19 M 5,11 H 19 M 5,16.5 H 15.5"),
            Pen = new Pen(emptyStrokeBrush, 1.55)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            }
        };
        var emptySuggestionsSource = new DrawingImage(emptySuggestionsDrawing);
        emptySuggestionsSource.Freeze();
        emptySuggestionsIcon = new Image
        {
            Source = emptySuggestionsSource, Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        System.Windows.Automation.AutomationProperties.SetName(emptySuggestionsIcon, "Inga ordförslag");

        var slotBarBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0xCF, 0xC5));
        slotBarBrush.Freeze();
        Border PlaceholderBar(double widthFraction, double bottomMargin) => new()
        {
            Height = 4, Width = widthFraction, CornerRadius = new CornerRadius(2),
            Background = slotBarBrush, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, bottomMargin), IsHitTestVisible = false
        };
        var placeholderSlots = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 14, 16, 0),
            IsHitTestVisible = false,
            Children = { PlaceholderBar(46, 7), PlaceholderBar(38, 7), PlaceholderBar(42, 0) }
        };
        var emptyBadgeSurface = new SolidColorBrush(Color.FromRgb(0xF6, 0xF4, 0xEF));
        emptyBadgeSurface.Freeze();
        var emptyIconBadge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(8),
            Background = emptyBadgeSurface, BorderBrush = SoftKeyKeyDesign.MutedDivider,
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            Child = emptySuggestionsIcon, IsHitTestVisible = false
        };
        var emptySuggestionsLabel = new TextBlock
        {
            Text = "Ordförslag", FontSize = 10.5, FontWeight = FontWeights.Medium,
            Foreground = SoftKeyPopupDesign.SecondaryText, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            IsHitTestVisible = false
        };
        var emptyCenterStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 16, 8, 4), IsHitTestVisible = false,
            Children = { emptyIconBadge, emptySuggestionsLabel }
        };
        var emptyStateOverlay = new Grid { IsHitTestVisible = false };
        emptyStateOverlay.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(UIElement.Visibility)) { Source = emptySuggestionsIcon });
        emptyStateOverlay.Children.Add(placeholderSlots);
        emptyStateOverlay.Children.Add(emptyCenterStack);
        suggestionArea.Children.Add(emptyStateOverlay);

        if (!secureMode)
        {
            var caption = new TextBlock
            {
                Text = PasswordSuggestionWindow.ChooseLabel, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0)
            };
            var content = new StackPanel();
            content.Children.Add(PasswordSuggestionWindow.LockIcon(22, SoftKeyPopupDesign.OnAccentText));
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
            double usableWidth = Math.Max(0, e.NewSize.Width - 6);
            double badgeSize = Math.Clamp(Math.Min(usableWidth * 0.52, e.NewSize.Height * 0.24), 24, 38);
            emptyIconBadge.Width = emptyIconBadge.Height = badgeSize;
            emptySuggestionsIcon.Width = emptySuggestionsIcon.Height = Math.Clamp(badgeSize * 0.5, 13, 19);
            bool showLabel = usableWidth >= 56 && e.NewSize.Height >= 115;
            emptySuggestionsLabel.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            bool showSlots = usableWidth >= 56 && e.NewSize.Height >= 185;
            placeholderSlots.Visibility = showSlots ? Visibility.Visible : Visibility.Collapsed;
            emptyCenterStack.Margin = new Thickness(2, showSlots ? 18 : 2, 8, 2);
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
