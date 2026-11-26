using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Styled confirmation dialog shown before the main SoftKey window closes.
class CloseConfirmationWindow : Window
{
    public CloseConfirmationWindow()
    {
        Title = "Stäng SoftKey?";
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;

        var message = new TextBlock
        {
            Text = "Är du säker på att du vill stänga programmet?",
            FontSize = 13, Foreground = SoftKeyPopupDesign.PrimaryText,
            TextWrapping = TextWrapping.Wrap, LineHeight = 20
        };

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Click += (_, _) => DialogResult = false;

        var confirm = SoftKeyButtonDesign.Action("Stäng", true);
        confirm.Click += (_, _) => DialogResult = true;
        var body = SoftKeyPopupDesign.Sections(338, message, SoftKeyButtonDesign.Row(cancel, confirm), new Thickness(16, 18, 16, 18));

        Content = SoftKeyPopupDesign.Frame("Stäng SoftKey?", body, () => Close(), () => DragMove());
    }
}
