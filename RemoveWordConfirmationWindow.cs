using System.Windows;
using System.Windows.Controls;

class RemoveWordConfirmationWindow : Window
{
    const string TrashIconOutline = "M 4,6 H 16 M 7.5,6 V 4.2 Q 7.5,3 8.7,3 H 11.3 Q 12.5,3 12.5,4.2 V 6 M 5.5,6 L 6.3,16 Q 6.4,17.2 7.6,17.2 H 12.4 Q 13.6,17.2 13.7,16 L 14.5,6 M 8.5,9.2 V 14.2 M 11.5,9.2 V 14.2";

    public RemoveWordConfirmationWindow(string word)
    {
        Title = "Ta bort ord";
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;
        UseLayoutRounding = true;

        var heading = new ScrollViewer
        {
            MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = $"Ta bort ”{word}”?",
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = SoftKeyPopupDesign.PrimaryText,
                LineHeight = 20, TextWrapping = TextWrapping.Wrap
            }
        };
        var card = SoftKeyPopupDesign.PromptCard(
            SoftKeyPopupDesign.ErrorSurface, SoftKeyPopupDesign.ErrorText, TrashIconOutline,
            heading, "Ordet tas bort från dina sparade ordförslag.");

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = SoftKeyButtonDesign.Destructive("Ta bort");
        confirm.Click += (_, _) => DialogResult = true;
        var body = SoftKeyPopupDesign.Sections(384, card, SoftKeyButtonDesign.Row(cancel, confirm));

        Content = SoftKeyPopupDesign.Frame("Ta bort ord", body, Close, () => DragMove());
        Loaded += (_, _) => cancel.Focus();
    }
}
