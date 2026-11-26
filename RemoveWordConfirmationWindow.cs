using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

class RemoveWordConfirmationWindow : Window
{
    public RemoveWordConfirmationWindow(string word)
    {
        Title = "Ta bort ord";
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;
        UseLayoutRounding = true;

        var message = new ScrollViewer
        {
            MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = $"Är du säker på att du vill ta bort ”{word}”?",
                FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap
            }
        };

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = SoftKeyButtonDesign.Action("Ta bort", true);
        confirm.Click += (_, _) => DialogResult = true;
        var body = SoftKeyPopupDesign.Sections(350, message, SoftKeyButtonDesign.Row(cancel, confirm), new Thickness(16, 18, 16, 18));

        Content = SoftKeyPopupDesign.Frame("Ta bort ord", body, Close, () => DragMove());
        Loaded += (_, _) => cancel.Focus();
    }
}
