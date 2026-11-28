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
using System.Windows.Shapes;
using System.Windows.Threading;

class WordListWindow : Window
{
    const double PreferredBodyHeight = 418;
    const string SearchIconOutline = "M 11.2,11.2 L 15,15 M 12.2,7.1 A 5.1,5.1 0 1 1 2,7.1 A 5.1,5.1 0 1 1 12.2,7.1";
    const string ClearIconOutline = "M 4.5,4.5 L 13.5,13.5 M 13.5,4.5 L 4.5,13.5";
    const string PencilIconOutline = "M 11.8,3.2 L 14.8,6.2 L 6.2,14.8 H 3.2 V 11.8 Z M 10.2,4.8 L 13.2,7.8";
    const string TrashIconOutline = "M 3.5,5.5 H 14.5 M 6.8,5.5 V 3.8 Q 6.8,2.8 7.8,2.8 H 10.2 Q 11.2,2.8 11.2,3.8 V 5.5 M 4.8,5.5 L 5.5,14.5 Q 5.6,15.5 6.6,15.5 H 11.4 Q 12.4,15.5 12.5,14.5 L 13.2,5.5";
    const string CheckIconOutline = "M 3.5,9.2 L 7.5,13.2 L 14.5,5.2";

    static readonly Brush MutedText = SoftKeyPopupDesign.SecondaryText;
    static readonly Brush Accent = SoftKeyPopupDesign.Accent;
    static readonly Brush SubtleInputSurface = FrozenBrush(0xF7, 0xF5, 0xF0);
    static readonly Brush RowDividerBrush = FrozenBrush(0xEC, 0xE9, 0xE1);
    static readonly Brush RowHoverBrush = FrozenBrush(0xF5, 0xF3, 0xEE);
    static readonly Brush RowSelectedBrush = FrozenBrush(0xEE, 0xF3, 0xF8);

    readonly Func<IEnumerable<string>> getWords;
    readonly Func<string, string> addWord, removeWord;
    readonly Func<string, string, string> renameWord;
    readonly ListBox wordList = new();
    readonly TextBox wordInput = new();
    readonly TextBox searchInput = new();
    readonly Border searchPanel = new() { Height = 0, Opacity = 0, Visibility = Visibility.Collapsed, ClipToBounds = true };
    readonly Button searchButton = SoftKeyButtonDesign.Action("", primary: false, contentAlignment: HorizontalAlignment.Left);
    readonly TextBlock searchButtonLabel = new() { Text = "Sök bland ord…", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock listTitle = new() { Text = "Alla ord", FontSize = 13.5, FontWeight = FontWeights.SemiBold };
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

    static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

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

        var body = new Grid { Height = PreferredBodyHeight };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Top card: "Lägg till nytt ord"
        var addHeading = new TextBlock
        {
            Text = "Lägg till nytt ord", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = SoftKeyPopupDesign.PrimaryText, Margin = new Thickness(1, 0, 0, 7)
        };
        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wordInput.FontSize = 13;
        wordInput.BorderThickness = new Thickness(0);
        wordInput.Background = Brushes.Transparent;
        wordInput.Padding = new Thickness(9, 5, 9, 5);
        wordInput.VerticalContentAlignment = VerticalAlignment.Center;
        wordInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        AutomationProperties.SetName(wordInput, "Nytt ord");
        var inputContent = new Grid();
        inputContent.Children.Add(wordInput);
        var placeholder = new TextBlock
        {
            Text = "Skriv nytt ord…", FontSize = 13, Foreground = MutedText,
            Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        inputContent.Children.Add(placeholder);
        var inputBorder = new Border
        {
            Background = SubtleInputSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), MinHeight = 34, Child = inputContent
        };
        wordInput.GotKeyboardFocus += (_, _) =>
        {
            editingInputActive = false;
            searchInputActive = false;
            inputBorder.BorderBrush = Accent;
            inputBorder.Background = SoftKeyPopupDesign.CardSurface;
        };
        wordInput.LostKeyboardFocus += (_, _) =>
        {
            inputBorder.BorderBrush = SoftKeyPopupDesign.CardBorder;
            inputBorder.Background = SubtleInputSurface;
        };
        void RefreshAddButtonState()
        {
            add.IsEnabled = !string.IsNullOrWhiteSpace(wordInput.Text);
            add.Opacity = add.IsEnabled ? 1.0 : 0.55;
        }
        wordInput.TextChanged += (_, _) =>
        {
            RefreshAddButtonState();
            placeholder.Visibility = wordInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        wordInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddEnteredWord();
            e.Handled = true;
        };
        inputRow.Children.Add(inputBorder);
        add.Width = 92;
        add.Height = 34;
        add.FontWeight = FontWeights.SemiBold;
        add.Margin = new Thickness(8, 0, 0, 0);
        add.Focusable = false;
        RefreshAddButtonState();
        add.Click += (_, _) => AddEnteredWord();
        Grid.SetColumn(add, 1);
        inputRow.Children.Add(add);

        var addCard = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(12, 10, 12, 11), Margin = new Thickness(0, 0, 0, 11),
            Child = new StackPanel { Children = { addHeading, inputRow } }
        };
        body.Children.Add(addCard);

        // Main card: "Alla ord"
        var listContent = new Grid();
        listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var listHeading = new DockPanel { Margin = new Thickness(13, 8, 10, 8) };

        wordCount.FontSize = 11.5;
        wordCount.FontWeight = FontWeights.SemiBold;
        wordCount.Foreground = SoftKeyPopupDesign.PrimaryText;
        wordCount.VerticalAlignment = VerticalAlignment.Center;
        var countBadge = new Border
        {
            Background = SoftKeyPopupDesign.DialogFooter, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1.5, 8, 2), Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = wordCount
        };

        searchButtonLabel.Foreground = MutedText;
        var searchButtonContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        searchButtonContent.Children.Add(new Path
        {
            Data = Geometry.Parse(SearchIconOutline),
            Stroke = MutedText, StrokeThickness = 1.45, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Width = 13.5, Height = 13.5,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center
        });
        searchButtonContent.Children.Add(searchButtonLabel);
        searchButton.Content = searchButtonContent;
        searchButton.Width = double.NaN;
        searchButton.MinWidth = 148;
        searchButton.Height = 40;
        searchButton.Margin = new Thickness(0);
        searchButton.Padding = new Thickness(11, 0, 12, 0);
        searchButton.Background = SubtleInputSurface;
        searchButton.BorderBrush = SoftKeyPopupDesign.CardBorder;
        searchButton.Focusable = false;
        searchButton.ToolTip = "Sök i ordlistan";
        AutomationProperties.SetName(searchButton, "Sök i ordlistan");
        searchButton.Click += (_, _) => SetSearchOpen(!searchOpen);
        DockPanel.SetDock(searchButton, Dock.Right);
        listHeading.Children.Add(searchButton);

        listTitle.VerticalAlignment = VerticalAlignment.Center;
        var titleGroup = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleGroup.Children.Add(listTitle);
        titleGroup.Children.Add(countBadge);
        listHeading.Children.Add(titleGroup);

        var listHeader = new StackPanel();
        listHeader.Children.Add(listHeading);
        searchPanel.Child = SearchField();
        listHeader.Children.Add(searchPanel);
        listContent.Children.Add(new Border
        {
            Background = SoftKeyPopupDesign.CardSurface,
            BorderBrush = RowDividerBrush, BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius, SoftKeyPopupDesign.CardCornerRadius, 0, 0),
            Child = listHeader
        });

        wordList.Background = Brushes.Transparent;
        wordList.BorderThickness = new Thickness(0);
        wordList.Padding = new Thickness(4, 2, 4, 4);
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
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Child = listContent
        };
        Grid.SetRow(listCard, 1);
        body.Children.Add(listCard);

        feedback.Foreground = MutedText;
        feedback.FontSize = 11.5;
        feedback.Margin = new Thickness(0, 0, 14, 0);
        feedback.VerticalAlignment = VerticalAlignment.Center;
        feedback.Visibility = Visibility.Collapsed;
        feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);

        var close = SoftKeyButtonDesign.Action("Stäng");
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
            Content = new Path
            {
                Data = Geometry.Parse(ClearIconOutline), Stroke = MutedText, StrokeThickness = 1.5,
                Width = 11, Height = 11, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            },
            Width = 26, Height = 26,
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
            Margin = new Thickness(10, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false
        };
        inputContent.Children.Add(placeholder);
        content.Children.Add(inputContent);
        var field = new Border
        {
            Height = 33, Margin = new Thickness(12, 0, 12, 10), Background = SubtleInputSurface,
            BorderBrush = SoftKeyPopupDesign.CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = content, RenderTransform = new TranslateTransform()
        };
        searchInput.GotKeyboardFocus += (_, _) =>
        {
            searchInputActive = true;
            editingInputActive = false;
            field.BorderBrush = Accent;
            field.Background = SoftKeyPopupDesign.CardSurface;
        };
        searchInput.LostKeyboardFocus += (_, _) =>
        {
            field.BorderBrush = SoftKeyPopupDesign.CardBorder;
            field.Background = SubtleInputSurface;
        };
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
        searchButtonLabel.Text = open ? "Stäng sökning" : "Sök bland ord…";
        searchButtonLabel.Foreground = open ? SoftKeyPopupDesign.ActionText : MutedText;
        searchButton.ToolTip = open ? "Stäng sökning" : "Sök i ordlistan";
        AutomationProperties.SetName(searchButton, (string)searchButton.ToolTip);
        searchPanel.Visibility = Visibility.Visible;
        searchPanel.IsHitTestVisible = open;
        var duration = new Duration(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 160 : 0));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var height = new DoubleAnimation(searchPanel.ActualHeight, open ? 43 : 0, duration) { EasingFunction = easing };
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
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        surface.SetValue(Border.PaddingProperty, new Thickness(2, 3, 2, 3));
        surface.SetValue(SoftKeyHoverTransition.BorderBrushProperty, RowDividerBrush);
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

        var showIndicator = new MultiDataTrigger
        {
            Conditions =
            {
                new System.Windows.Condition(new Binding("IsSelected") { RelativeSource = RelativeSource.Self }, true),
                new System.Windows.Condition(new Binding(nameof(WordListItem.IsEditing)), false)
            }
        };
        showIndicator.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "SelectionIndicator"));
        template.Triggers.Add(showIndicator);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(5, 1, 5, 0)));
        style.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(WordListItem.Word))));
        var hovered = new Trigger { Property = IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(BackgroundProperty, RowHoverBrush));
        style.Triggers.Add(hovered);
        var selectedNotEditing = new MultiDataTrigger
        {
            Conditions =
            {
                new System.Windows.Condition(new Binding("IsSelected") { RelativeSource = RelativeSource.Self }, true),
                new System.Windows.Condition(new Binding(nameof(WordListItem.IsEditing)), false)
            }
        };
        selectedNotEditing.Setters.Add(new Setter(BackgroundProperty, RowSelectedBrush));
        style.Triggers.Add(selectedNotEditing);
        return style;
    }

    DataTemplate WordItemTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var actions = new FrameworkElementFactory(typeof(StackPanel), "NormalActions");
        actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        actions.SetValue(DockPanel.DockProperty, Dock.Right);
        actions.SetValue(VisibilityProperty, Visibility.Collapsed);
        var edit = RowIconAction(PencilIconOutline, MutedText, "Redigera ordet", "Redigera {0}", BeginEditing);
        edit.Name = "EditAction";
        actions.AppendChild(edit);
        actions.AppendChild(RowIconAction(TrashIconOutline, MutedText, "Ta bort ordet", "Ta bort {0}", RemoveSelectedWord));
        row.AppendChild(actions);

        var content = new FrameworkElementFactory(typeof(Grid));
        var text = new FrameworkElementFactory(typeof(TextBlock), "WordLabel");
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListItem.Word)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(MarginProperty, new Thickness(11, 6, 8, 6));
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        content.AppendChild(text);

        var editRow = new FrameworkElementFactory(typeof(DockPanel), "EditRow");
        editRow.SetValue(VisibilityProperty, Visibility.Collapsed);
        editRow.SetValue(MarginProperty, new Thickness(4, 2, 2, 2));

        var editButtons = new FrameworkElementFactory(typeof(StackPanel));
        editButtons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        editButtons.SetValue(DockPanel.DockProperty, Dock.Right);
        editButtons.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var save = RowIconAction(CheckIconOutline, SoftKeyPopupDesign.OnAccentText,
            "Spara ändringen (Enter). Avbryt med Escape.", "Spara ändring av {0}", SaveEditing, primary: true);
        save.SetValue(WidthProperty, 30.0);
        save.SetValue(HeightProperty, 30.0);
        save.SetValue(MarginProperty, new Thickness(6, 0, 2, 0));
        editButtons.AppendChild(save);
        var cancelEdit = RowIconAction(ClearIconOutline, MutedText,
            "Avbryt redigeringen (Escape).", "Avbryt redigering av {0}", () => CancelEditing());
        cancelEdit.SetValue(WidthProperty, 28.0);
        cancelEdit.SetValue(HeightProperty, 28.0);
        cancelEdit.SetValue(MarginProperty, new Thickness(2, 0, 0, 0));
        editButtons.AppendChild(cancelEdit);
        editRow.AppendChild(editButtons);

        var editField = new FrameworkElementFactory(typeof(Border), "EditField");
        editField.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.CardSurface);
        editField.SetValue(Border.BorderBrushProperty, Accent);
        editField.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        editField.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        editField.SetValue(HeightProperty, 32.0);
        var input = new FrameworkElementFactory(typeof(TextBox), "EditInput");
        input.SetValue(BorderThicknessProperty, new Thickness(0));
        input.SetValue(BackgroundProperty, Brushes.Transparent);
        input.SetValue(PaddingProperty, new Thickness(8, 4, 6, 4));
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
        editField.AppendChild(input);
        editRow.AppendChild(editField);
        content.AppendChild(editRow);
        row.AppendChild(content);

        var template = new DataTemplate { VisualTree = row };
        var showHoveredActions = new DataTrigger
        {
            Binding = new Binding("IsMouseOver") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) },
            Value = true
        };
        showHoveredActions.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "NormalActions"));
        template.Triggers.Add(showHoveredActions);

        var showSelectedActions = new DataTrigger
        {
            Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) },
            Value = true
        };
        showSelectedActions.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "NormalActions"));
        template.Triggers.Add(showSelectedActions);

        var editing = new DataTrigger { Binding = new Binding(nameof(WordListItem.IsEditing)), Value = true };
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed, "WordLabel"));
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed, "NormalActions"));
        editing.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "EditRow"));
        template.Triggers.Add(editing);
        return template;
    }

    FrameworkElementFactory RowIconAction(string outline, Brush stroke, string tooltip, string accessibleName,
        Action action, bool primary = false)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(WidthProperty, 28.0);
        button.SetValue(HeightProperty, 28.0);
        button.SetValue(MarginProperty, new Thickness(3, 0, 3, 0));
        button.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        button.SetValue(FocusableProperty, false);
        button.SetValue(ToolTipProperty, tooltip);
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(WordListItem.Word)) { StringFormat = accessibleName });
        button.SetValue(StyleProperty, RowActionStyle(primary));

        var glyph = new FrameworkElementFactory(typeof(Path));
        glyph.SetValue(Path.DataProperty, Geometry.Parse(outline));
        glyph.SetValue(Shape.StrokeProperty, stroke);
        glyph.SetValue(Shape.StrokeThicknessProperty, primary ? 1.8 : 1.45);
        glyph.SetValue(Shape.StretchProperty, Stretch.Uniform);
        glyph.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        glyph.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        glyph.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        glyph.SetValue(WidthProperty, 13.5);
        glyph.SetValue(HeightProperty, 13.5);
        button.AppendChild(glyph);

        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (sender is Button { DataContext: WordListItem item })
            {
                // Hover can display actions on a row before the ListBox selects it.
                wordList.SelectedItem = item;
                if (wordList.SelectedItem == item) action();
            }
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
        style.Setters.Add(new Setter(BackgroundProperty, primary ? SoftKeyKeyDesign.EnterFace : Brushes.Transparent));
        style.Setters.Add(new Setter(BorderBrushProperty, primary ? SoftKeyKeyDesign.EnterBorder : Brushes.Transparent));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(primary ? 1 : 0)));
        var hovered = new Trigger { Property = IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(BackgroundProperty, primary ? SoftKeyKeyDesign.EnterHover : SoftKeyPopupDesign.CardHoverSurface));
        style.Triggers.Add(hovered);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(BackgroundProperty, primary ? SoftKeyKeyDesign.EnterPressed : SoftKeyPopupDesign.ActionPressed));
        style.Triggers.Add(pressed);
        return style;
    }
}
