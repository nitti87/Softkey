using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

class WordListWindow : Window
{
    const double PreferredBodyHeight = 418;
    static readonly Brush MutedText = SoftKeyPopupDesign.SecondaryText;
    static readonly Brush Accent = SoftKeyPopupDesign.Accent;
    readonly Func<IEnumerable<string>> getWords;
    readonly Func<string, string> addWord, removeWord;
    readonly Func<string, string, string> renameWord;
    readonly ListBox wordList = new();
    readonly TextBox wordInput = new();
    readonly TextBox searchInput = new();
    readonly Border searchPanel = new() { Height = 0, Opacity = 0, Visibility = Visibility.Collapsed, ClipToBounds = true };
    readonly Button searchButton = SoftKeyButtonDesign.Action("", true);
    readonly TextBlock listTitle = new() { Text = "Alla ord", FontSize = 13, FontWeight = FontWeights.SemiBold };
    readonly TextBlock wordCount = new(), feedback = new(), emptyMessage = new();
    readonly DispatcherTimer feedbackTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly Button add = SoftKeyButtonDesign.Action("Lägg till", true);
    WordListItem editingItem;
    WordListItem[] allWords = Array.Empty<WordListItem>();
    bool editingInputActive, searchInputActive, searchOpen;
    string searchQuery = "";
    public Window RemovalConfirmation { get; private set; }
    internal StackPanel DialogBody { get; } = new();
    internal StackPanel WordsPage { get; }

    public WordListWindow(Func<IEnumerable<string>> getWords, Func<string, string> addWord,
        Func<string, string> removeWord, Func<string, string, string> renameWord)
    {
        this.getWords = getWords;
        this.addWord = addWord;
        this.removeWord = removeWord;
        this.renameWord = renameWord;
        Title = "Ordlista";
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        feedbackTimer.Tick += (_, _) => ShowFeedback(null, false);
        Closed += (_, _) => feedbackTimer.Stop();

        var buttonStyle = new Style(typeof(Button));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.4));
        buttonStyle.Triggers.Add(disabled);
        Resources.Add(typeof(Button), buttonStyle);

        var body = new Grid { Height = PreferredBodyHeight };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new TextBlock
        {
            Text = "Lägg till ord och hantera dina ordförslag.", FontSize = 11,
            Foreground = MutedText, Margin = new Thickness(0, 0, 0, 12)
        };
        body.Children.Add(heading);

        var listContent = new Grid();
        listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var listHeading = new DockPanel { Margin = new Thickness(12, 6, 8, 6) };
        wordCount.FontSize = 11;
        wordCount.Foreground = MutedText;
        wordCount.VerticalAlignment = VerticalAlignment.Center;
        var counter = new StackPanel { Orientation = Orientation.Horizontal };
        counter.Children.Add(new TextBlock
        {
            Text = "\uE8A5", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        });
        counter.Children.Add(wordCount);
        counter.Children.Add(new Border
        {
            Width = 1, Height = 18, Background = SoftKeyPopupDesign.Border,
            Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        });
        searchButton.Content = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 11,11 L 15,15 M 12,7 A 5,5 0 1 1 2,7 A 5,5 0 1 1 12,7"),
            Stroke = searchButton.Foreground, StrokeThickness = 1.5, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 16
        };
        searchButton.Width = 40;
        searchButton.Height = 40;
        searchButton.Margin = new Thickness(0);
        searchButton.Padding = new Thickness(5);
        searchButton.Focusable = false;
        searchButton.ToolTip = "Sök i ordlistan";
        AutomationProperties.SetName(searchButton, "Sök i ordlistan");
        searchButton.Click += (_, _) => SetSearchOpen(!searchOpen);
        counter.Children.Add(searchButton);
        DockPanel.SetDock(counter, Dock.Right);
        listHeading.Children.Add(counter);
        listTitle.VerticalAlignment = VerticalAlignment.Center;
        listHeading.Children.Add(listTitle);
        var listHeader = new StackPanel();
        listHeader.Children.Add(listHeading);
        searchPanel.Child = SearchField();
        listHeader.Children.Add(searchPanel);
        listContent.Children.Add(new Border
        {
            Background = SoftKeyPopupDesign.DialogFooter,
            BorderBrush = SoftKeyPopupDesign.CardBorder, BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(8, 8, 0, 0), Child = listHeader
        });

        wordList.Background = Brushes.Transparent;
        wordList.BorderThickness = new Thickness(0);
        wordList.Padding = new Thickness(4, 3, 4, 3);
        wordList.FontSize = 13;
        wordList.ItemContainerStyle = WordItemStyle();
        wordList.ItemTemplate = WordItemTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(wordList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(wordList, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(wordList, "Ord i ordlistan");
        wordList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (editingItem == null) return;
            var row = ItemsControl.ContainerFromElement(wordList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (row?.Content is WordListItem item && item != editingItem) CancelEditing(false);
        };
        wordList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete || e.OriginalSource is TextBox || wordList.SelectedItem is not WordListItem) return;
            RemoveSelectedWord();
            e.Handled = true;
        };
        Grid.SetRow(wordList, 1);
        listContent.Children.Add(wordList);
        emptyMessage.Text = "Din ordlista är tom.\nLägg till ditt första ord i fältet ovan.";
        emptyMessage.TextAlignment = TextAlignment.Center;
        emptyMessage.Foreground = MutedText;
        emptyMessage.FontSize = 13;
        emptyMessage.LineHeight = 22;
        emptyMessage.VerticalAlignment = VerticalAlignment.Center;
        emptyMessage.IsHitTestVisible = false;
        Grid.SetRow(emptyMessage, 1);
        listContent.Children.Add(emptyMessage);
        var listCard = new Border
        {
            Background = SoftKeyPopupDesign.DialogSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = listContent
        };
        Grid.SetRow(listCard, 3);
        body.Children.Add(listCard);

        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wordInput.FontSize = 13;
        wordInput.BorderThickness = new Thickness(0);
        wordInput.Background = Brushes.Transparent;
        wordInput.Padding = new Thickness(8, 4, 8, 4);
        wordInput.VerticalContentAlignment = VerticalAlignment.Center;
        wordInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        AutomationProperties.SetName(wordInput, "Nytt ord");
        var inputContent = new Grid();
        inputContent.Children.Add(wordInput);
        var placeholder = new TextBlock
        {
            Text = "Skriv nytt ord…", FontSize = 13, Foreground = MutedText,
            Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        inputContent.Children.Add(placeholder);
        var inputBorder = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.ControlOutline,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), MinHeight = 30, Child = inputContent
        };
        wordInput.GotKeyboardFocus += (_, _) =>
        {
            editingInputActive = false;
            searchInputActive = false;
            inputBorder.BorderBrush = Accent;
        };
        wordInput.LostKeyboardFocus += (_, _) => inputBorder.BorderBrush = SoftKeyPopupDesign.CardBorder;
        wordInput.TextChanged += (_, _) =>
        {
            add.IsEnabled = !string.IsNullOrWhiteSpace(wordInput.Text);
            placeholder.Visibility = wordInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        wordInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddEnteredWord();
            e.Handled = true;
        };
        inputRow.Children.Add(inputBorder);
        add.Margin = new Thickness(8, 0, 0, 0);
        add.Focusable = false;
        add.IsEnabled = false;
        add.Click += (_, _) => AddEnteredWord();
        Grid.SetColumn(add, 1);
        inputRow.Children.Add(add);
        Grid.SetRow(inputRow, 1);
        body.Children.Add(inputRow);

        var sectionSeparator = SoftKeyPopupDesign.Separator(new Thickness(0, 12, 0, 12));
        Grid.SetRow(sectionSeparator, 2);
        body.Children.Add(sectionSeparator);

        feedback.Foreground = MutedText;
        feedback.FontSize = 11;
        feedback.Margin = new Thickness(0, 0, 14, 0);
        feedback.VerticalAlignment = VerticalAlignment.Center;
        feedback.Visibility = Visibility.Collapsed;
        feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);

        var close = SoftKeyButtonDesign.Action("Stäng", true);
        close.Focusable = false;
        close.Click += (_, _) => Close();
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.Children.Add(feedback);
        Grid.SetColumn(close, 1);
        actions.Children.Add(close);
        WordsPage = SoftKeyPopupDesign.Sections(480, body, actions);
        DialogBody.Children.Add(WordsPage);
        var frame = SoftKeyPopupDesign.Frame(Title, DialogBody, Close, DragMove);
        Content = frame;
        SoftKeyPopupDesign.ConfigureWindow(this, available =>
        {
            WordsPage.Width = Math.Clamp(available.Width - 24, 280, 480);
            frame.Measure(new Size(available.Width, double.PositiveInfinity));
            // The tabbed window adds its headers to the same frame before it opens.
            double surroundingHeight = frame.DesiredSize.Height - body.DesiredSize.Height;
            body.Height = Math.Clamp(available.Height - surroundingHeight, 240, PreferredBodyHeight);
        });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (searchOpen && searchInput.IsKeyboardFocusWithin) SetSearchOpen(false);
            else if (editingItem != null) CancelEditing();
            else if (searchOpen) SetSearchOpen(false);
            else Close();
            e.Handled = true;
        };
        Loaded += (_, _) => wordInput.Focus();
        RefreshWords();
    }

    public bool FocusWordInput()
    {
        if (RemovalConfirmation != null || !wordInput.IsEnabled) return false;
        if (!Activate()) return false;
        var target = searchInputActive && searchOpen ? searchInput
            : editingInputActive && editingItem != null ? EditingInput() : wordInput;
        return target.Focus();
    }

    void RefreshWords(string selectWord = null)
    {
        var existing = allWords.ToDictionary(item => item.Word);
        allWords = getWords()
            .Select(word => existing.GetValueOrDefault(word) ?? new WordListItem(word)).ToArray();
        ApplySearch(selectWord);
    }

    void UpdateSearch()
    {
        string query = searchInput.Text.Trim();
        if (query == searchQuery) return;
        searchQuery = query;
        ApplySearch((wordList.SelectedItem as WordListItem)?.Word);
    }

    void ApplySearch(string selectWord = null)
    {
        var byWord = allWords.ToDictionary(item => item.Word);
        var visible = WordListSearch.Find(byWord.Keys, searchQuery).Select(word => byWord[word]).ToArray();
        wordList.ItemsSource = visible;
        wordList.SelectedItem = visible.FirstOrDefault(item => item.Word == selectWord);
        if (wordList.SelectedItem != null) wordList.ScrollIntoView(wordList.SelectedItem);
        bool searching = searchQuery.Length > 0;
        listTitle.Text = searching ? "Sökresultat" : "Alla ord";
        wordCount.Text = searching ? $"{visible.Length} av {allWords.Length} ord" : $"{allWords.Length} ord";
        emptyMessage.Text = allWords.Length == 0 ? "Din ordlista är tom.\nLägg till ditt första ord i fältet ovan."
            : "Inga matchande ord.\nProva färre bokstäver.";
        emptyMessage.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    Border SearchField()
    {
        var content = new DockPanel();
        var clear = new Button
        {
            Content = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 11, Foreground = MutedText, Width = 24, Height = 24,
            Margin = new Thickness(2), Focusable = false, ToolTip = "Rensa sökning",
            Style = RowActionStyle(false), Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(clear, "Rensa sökning");
        clear.Click += (_, _) => { searchInput.Clear(); UpdateSearch(); searchInput.Focus(); };
        DockPanel.SetDock(clear, Dock.Right);
        content.Children.Add(clear);
        searchInput.FontSize = 13;
        searchInput.BorderThickness = new Thickness(0);
        searchInput.Background = Brushes.Transparent;
        searchInput.Padding = new Thickness(9, 5, 5, 5);
        searchInput.VerticalContentAlignment = VerticalAlignment.Center;
        searchInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        AutomationProperties.SetName(searchInput, "Sök efter ordets början");
        var inputContent = new Grid();
        inputContent.Children.Add(searchInput);
        var placeholder = new TextBlock
        {
            Text = "Skriv början av ett ord…", FontSize = 13, Foreground = MutedText,
            Margin = new Thickness(9, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false
        };
        inputContent.Children.Add(placeholder);
        content.Children.Add(inputContent);
        var field = new Border
        {
            Height = 32, Margin = new Thickness(12, 0, 12, 10), Background = SoftKeyPopupDesign.CardSurface,
            BorderBrush = SoftKeyPopupDesign.ControlOutline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Child = content, RenderTransform = new TranslateTransform()
        };
        searchInput.GotKeyboardFocus += (_, _) =>
        {
            searchInputActive = true;
            editingInputActive = false;
            field.BorderBrush = Accent;
        };
        searchInput.LostKeyboardFocus += (_, _) => field.BorderBrush = SoftKeyPopupDesign.ControlOutline;
        searchInput.TextChanged += (_, _) =>
        {
            bool empty = searchInput.Text.Length == 0;
            placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        };
        searchInput.KeyUp += (_, _) => UpdateSearch();
        DataObject.AddPastingHandler(searchInput, (_, _) => Dispatcher.BeginInvoke(new Action(UpdateSearch)));
        return field;
    }

    void SetSearchOpen(bool open)
    {
        searchOpen = open;
        searchButton.ToolTip = open ? "Stäng sökning" : "Sök i ordlistan";
        AutomationProperties.SetName(searchButton, (string)searchButton.ToolTip);
        searchPanel.Visibility = Visibility.Visible;
        searchPanel.IsHitTestVisible = open;
        var duration = new Duration(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 160 : 0));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var height = new DoubleAnimation(searchPanel.ActualHeight, open ? 42 : 0, duration) { EasingFunction = easing };
        height.Completed += (_, _) => { if (!searchOpen) searchPanel.Visibility = Visibility.Collapsed; };
        searchPanel.BeginAnimation(HeightProperty, height);
        searchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, duration));
        searchPanel.Child.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(open ? -4 : 0, open ? 0 : -4, duration) { EasingFunction = easing });
        if (open) searchInput.Focus();
        else
        {
            searchInput.Clear();
            UpdateSearch();
            searchInputActive = false;
            wordInput.Focus();
        }
    }

    TextBox EditingInput()
    {
        wordList.SelectedItem = editingItem;
        wordList.ScrollIntoView(editingItem);
        wordList.UpdateLayout();
        var container = (ListBoxItem)wordList.ItemContainerGenerator.ContainerFromItem(editingItem);
        container.ApplyTemplate();
        var presenter = (ContentPresenter)container.Template.FindName("RowContent", container);
        presenter.ApplyTemplate();
        return (TextBox)wordList.ItemTemplate.FindName("EditInput", presenter);
    }

    void BeginEditing()
    {
        if (wordList.SelectedItem is not WordListItem item) return;
        searchInputActive = false;
        if (editingItem == item)
        {
            editingInputActive = true;
            FocusWordInput();
            return;
        }
        if (editingItem != null) EndEditing();
        editingItem = item;
        item.Draft = item.Word;
        item.IsEditing = true;
        editingInputActive = true;
        if (FocusWordInput()) EditingInput().SelectAll();
    }

    void EndEditing()
    {
        editingItem.IsEditing = false;
        editingItem = null;
        editingInputActive = false;
    }

    void CancelEditing(bool focusInput = true)
    {
        editingItem.Draft = editingItem.Word;
        EndEditing();
        ShowFeedback("Redigeringen avbröts.", false);
        if (focusInput) wordInput.Focus();
    }

    void SaveEditing()
    {
        if (editingItem == null) return;
        string error = renameWord(editingItem.Word, editingItem.Draft);
        if (error != null)
        {
            ShowFeedback(error, true);
            editingInputActive = true;
            searchInputActive = false;
            FocusWordInput();
            return;
        }
        string savedWord = editingItem.Draft.Trim().ToLower();
        bool changed = savedWord != editingItem.Word;
        EndEditing();
        RefreshWords(savedWord);
        ShowFeedback(changed ? "Ordet har uppdaterats." : null, false);
        wordInput.Focus();
    }

    void AddEnteredWord()
    {
        string word = wordInput.Text.Trim();
        if (word.Length == 0) return;
        string error = addWord(word);
        ShowFeedback(error ?? "Ordet har lagts till.", error != null);
        if (error == null)
        {
            RefreshWords(word.ToLower());
            wordInput.Clear();
        }
        wordInput.Focus();
    }

    void RemoveSelectedWord()
    {
        if (RemovalConfirmation != null || wordList.SelectedItem is not WordListItem item) return;
        string word = item.Word;
        var confirmation = new RemoveWordConfirmationWindow(word) { Owner = this, Topmost = Topmost };
        RemovalConfirmation = confirmation;
        var content = (UIElement)Content;
        bool wasEnabled = content.IsEnabled;
        double previousOpacity = content.Opacity;
        bool confirmed;
        content.IsEnabled = false;
        content.Opacity = 0.55;
        try { confirmed = confirmation.ShowDialog() == true; }
        finally
        {
            content.IsEnabled = wasEnabled;
            content.Opacity = previousOpacity;
            RemovalConfirmation = null;
        }
        if (!confirmed) return;
        string error = removeWord(word);
        ShowFeedback(error ?? "Ordet har tagits bort.", error != null);
        if (error == null)
        {
            if (editingItem == item) EndEditing();
            RefreshWords();
        }
    }

    void ShowFeedback(string text, bool error)
    {
        feedbackTimer.Stop();
        feedback.Text = text;
        feedback.ToolTip = text;
        feedback.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        feedback.Foreground = error ? SoftKeyPopupDesign.ErrorText : MutedText;
        if (feedback.Visibility == Visibility.Visible) feedbackTimer.Start();
    }

    static Style WordItemStyle()
    {
        var template = new ControlTemplate(typeof(ListBoxItem));
        var row = new FrameworkElementFactory(typeof(Grid));
        var surface = new FrameworkElementFactory(typeof(Border), "WordSurface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(SoftKeyPopupDesign.CardCornerRadius));
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetValue(Border.PaddingProperty, new Thickness(0, 2, 0, 2));
        surface.SetValue(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.CardBorder);
        surface.SetBinding(SoftKeyHoverTransition.BackgroundProperty, new Binding("Background")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "RowContent");
        surface.AppendChild(content);
        row.AppendChild(surface);
        var indicator = new FrameworkElementFactory(typeof(Border), "SelectionIndicator");
        indicator.SetValue(WidthProperty, 3.0);
        indicator.SetValue(HeightProperty, 16.0);
        indicator.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        indicator.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        indicator.SetValue(MarginProperty, new Thickness(3, 0, 0, 0));
        indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        indicator.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.Accent);
        indicator.SetValue(VisibilityProperty, Visibility.Collapsed);
        indicator.SetValue(IsHitTestVisibleProperty, false);
        row.AppendChild(indicator);
        template.VisualTree = row;
        var hoverBorder = new MultiTrigger { Conditions = { new(IsMouseOverProperty, true), new(IsEnabledProperty, true) } };
        hoverBorder.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.CardHoverBorder, "WordSurface"));
        template.Triggers.Add(hoverBorder);
        var showIndicator = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        showIndicator.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "SelectionIndicator"));
        showIndicator.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.ActionBorder, "WordSurface"));
        template.Triggers.Add(showIndicator);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(BackgroundProperty, SoftKeyPopupDesign.CardSurface));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(WordListItem.Word))));
        var hovered = new Trigger { Property = IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(BackgroundProperty, SoftKeyPopupDesign.CardHoverSurface));
        style.Triggers.Add(hovered);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, SoftKeyPopupDesign.ActionSurface));
        style.Triggers.Add(selected);
        var selectedHover = new MultiTrigger
        {
            Conditions = { new(ListBoxItem.IsSelectedProperty, true), new(IsMouseOverProperty, true), new(IsEnabledProperty, true) }
        };
        selectedHover.Setters.Add(new Setter(BackgroundProperty, SoftKeyPopupDesign.ActionHover));
        style.Triggers.Add(selectedHover);
        return style;
    }

    DataTemplate WordItemTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var actions = new FrameworkElementFactory(typeof(StackPanel));
        actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        actions.SetValue(DockPanel.DockProperty, Dock.Right);
        actions.SetBinding(VisibilityProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
            Converter = new BooleanToVisibilityConverter()
        });
        var edit = RowAction("\uE70F", "Redigera ordet", "Redigera {0}", BeginEditing);
        edit.Name = "EditAction";
        actions.AppendChild(edit);
        actions.AppendChild(RowAction("\uE74D", "Ta bort ordet", "Ta bort {0}", RemoveSelectedWord));
        row.AppendChild(actions);

        var content = new FrameworkElementFactory(typeof(Grid));
        var text = new FrameworkElementFactory(typeof(TextBlock), "WordLabel");
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListItem.Word)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(MarginProperty, new Thickness(12, 6, 8, 6));
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        content.AppendChild(text);

        var editField = new FrameworkElementFactory(typeof(Border), "EditField");
        editField.SetValue(VisibilityProperty, Visibility.Collapsed);
        editField.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.CardSurface);
        editField.SetValue(Border.BorderBrushProperty, SoftKeyPopupDesign.ControlOutline);
        editField.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        editField.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        editField.SetValue(MarginProperty, new Thickness(6, 0, 4, 0));
        editField.SetValue(HeightProperty, 30.0);
        var editContent = new FrameworkElementFactory(typeof(DockPanel));
        var save = RowAction("\uE73E", "Spara ändringen (Enter). Avbryt med Escape.", "Spara ändring av {0}", SaveEditing, true);
        save.SetValue(DockPanel.DockProperty, Dock.Right);
        save.SetValue(WidthProperty, 30.0);
        save.SetValue(HeightProperty, 24.0);
        save.SetValue(MarginProperty, new Thickness(2));
        save.SetValue(FontSizeProperty, 13.0);
        editContent.AppendChild(save);
        var input = new FrameworkElementFactory(typeof(TextBox), "EditInput");
        input.SetValue(BorderThicknessProperty, new Thickness(0));
        input.SetValue(BackgroundProperty, Brushes.Transparent);
        input.SetValue(PaddingProperty, new Thickness(8, 3, 4, 3));
        input.SetValue(VerticalContentAlignmentProperty, VerticalAlignment.Center);
        input.SetBinding(TextBox.TextProperty, new Binding(nameof(WordListItem.Draft))
        {
            Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        input.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(WordListItem.Word)) { StringFormat = "Redigera {0}" });
        input.AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) =>
        {
            editingInputActive = true;
            searchInputActive = false;
        }));
        input.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Enter) return;
            SaveEditing();
            e.Handled = true;
        }));
        editContent.AppendChild(input);
        editField.AppendChild(editContent);
        content.AppendChild(editField);
        row.AppendChild(content);

        var template = new DataTemplate { VisualTree = row };
        var editing = new DataTrigger { Binding = new Binding(nameof(WordListItem.IsEditing)), Value = true };
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed, "WordLabel"));
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed, "EditAction"));
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "EditField"));
        template.Triggers.Add(editing);
        var focused = new Trigger { SourceName = "EditInput", Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "EditField"));
        template.Triggers.Add(focused);
        return template;
    }

    static FrameworkElementFactory RowAction(string icon, string tooltip, string accessibleName, Action action, bool primary = false)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(ContentProperty, icon);
        button.SetValue(WidthProperty, 28.0);
        button.SetValue(HeightProperty, 28.0);
        button.SetValue(FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
        button.SetValue(FontSizeProperty, 14.0);
        button.SetValue(MarginProperty, new Thickness(4, 0, 4, 0));
        button.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        button.SetValue(FocusableProperty, false);
        button.SetValue(ToolTipProperty, tooltip);
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(WordListItem.Word)) { StringFormat = accessibleName });
        button.SetValue(StyleProperty, RowActionStyle(primary));
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            action();
            e.Handled = true;
        }));
        return button;
    }

    static Style RowActionStyle(bool primary)
    {
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        surface.SetBinding(SoftKeyHoverTransition.BackgroundProperty, new Binding("Background")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        surface.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        surface.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);
        template.VisualTree = surface;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(BackgroundProperty, primary ? SoftKeyPopupDesign.ActionSurface : Brushes.Transparent));
        if (primary) style.Setters.Add(new Setter(ForegroundProperty, SoftKeyPopupDesign.ActionText));
        style.Setters.Add(new Setter(BorderBrushProperty, primary ? SoftKeyPopupDesign.ActionBorder : Brushes.Transparent));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(primary ? 1 : 0)));
        var hovered = new Trigger { Property = IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(BackgroundProperty, primary ? SoftKeyPopupDesign.ActionHover : SoftKeyPopupDesign.CardSurface));
        style.Triggers.Add(hovered);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(BackgroundProperty, SoftKeyPopupDesign.ActionPressed));
        style.Triggers.Add(pressed);
        return style;
    }
}
