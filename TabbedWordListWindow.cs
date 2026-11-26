using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

sealed class TabbedWordListWindow : WordListWindow
{
    readonly TabControl tabs = SoftKeyTabDesign.Headers("Vanliga ord", "Specifika förslag");
    readonly SavedSuggestionsPage specific;

    public TabbedWordListWindow(Func<IEnumerable<string>> getWords, Func<string, string> addWord,
        Func<string, string> removeWord, Func<string, string, string> renameWord, SavedTextStore savedText,
        bool hasTarget, bool passwordTarget, Func<SavedTextEntry, Task<string>> useEntry,
        bool specificSuggestionsEnabled, Func<bool, bool> saveSpecificSuggestionsEnabled)
        : base(getWords, addWord, removeWord, renameWord)
    {
        specific = new SavedSuggestionsPage(savedText, hasTarget, passwordTarget, useEntry, Close,
            specificSuggestionsEnabled, saveSpecificSuggestionsEnabled) { Visibility = Visibility.Collapsed };
        specific.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(Width)) { Source = WordsPage });
        tabs.Margin = new Thickness(16, 14, 16, 0);
        DialogBody.Children.Remove(WordsPage);
        var pageViewport = new StackPanel { ClipToBounds = true, Children = { WordsPage, specific } };
        DialogBody.Children.Add(tabs);
        DialogBody.Children.Add(pageViewport);
        tabs.SelectedIndex = 0;
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            WordsPage.Visibility = IsSpecific ? Visibility.Collapsed : Visibility.Visible;
            specific.Visibility = IsSpecific ? Visibility.Visible : Visibility.Collapsed;
            SoftKeyTabDesign.ResizePage(pageViewport, IsSpecific ? specific : WordsPage, WordsPage.Width);
            InputAvailabilityChanged?.Invoke();
        };
        SoftKeyPopupDesign.KeepResizeInWorkArea(this);
        specific.EditingChanged += () => InputAvailabilityChanged?.Invoke();
        Closed += (_, _) => specific.ClearSecret();
    }

    public event Action InputAvailabilityChanged;
    public bool IsSpecific => tabs.SelectedIndex == 1;
    public bool CanType => ((UIElement)Content).IsEnabled && (!IsSpecific || specific.IsEditing);

    public bool FocusInputFromKeyboard()
        => CanType && (IsSpecific ? Activate() && specific.FocusInput() : FocusWordInput());

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (IsSpecific && e.Key == Key.Escape)
        {
            e.Handled = true;
            if (specific.IsEditing) specific.CloseEditor();
            else Close();
        }
        base.OnPreviewKeyDown(e);
    }
}
