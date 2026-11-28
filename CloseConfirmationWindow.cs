using System.Windows;
using System.Windows.Controls;

// Styled confirmation dialog shown before the main SoftKey window closes.
class CloseConfirmationWindow : Window
{
    const string PowerIconOutline = "M 10,2.5 V 9.5 M 5.6,4.8 A 6.5,6.5 0 1 0 14.4,4.8";

    public CloseConfirmationWindow()
    {
        Title = "Stäng SoftKey?";
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = "Vill du stänga SoftKey?",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = SoftKeyPopupDesign.PrimaryText, TextWrapping = TextWrapping.Wrap
        };
        var card = SoftKeyPopupDesign.PromptCard(
            SoftKeyPopupDesign.ActionSurface, SoftKeyPopupDesign.Accent, PowerIconOutline,
            heading, "Tangentbordet och DragKey stängs tills du öppnar programmet igen.");

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Click += (_, _) => DialogResult = false;

        var confirm = SoftKeyButtonDesign.Action("Stäng", true);
        confirm.Click += (_, _) => DialogResult = true;
        var body = SoftKeyPopupDesign.Sections(384, card, SoftKeyButtonDesign.Row(cancel, confirm));

        Content = SoftKeyPopupDesign.Frame("Stäng SoftKey?", body, () => Close(), () => DragMove());
    }
}
