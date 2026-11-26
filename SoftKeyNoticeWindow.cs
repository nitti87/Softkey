using System.Windows;
using System.Windows.Controls;

sealed class SoftKeyNoticeWindow : Window
{
    public SoftKeyNoticeWindow(string title, string message)
    {
        Title = title;
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;
        var text = new TextBlock { Text = message, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
        var ok = SoftKeyButtonDesign.Action("OK", true);
        ok.IsDefault = true; ok.IsCancel = true; ok.Click += (_, _) => Close();
        Content = SoftKeyPopupDesign.Frame(title,
            SoftKeyPopupDesign.Sections(420, text, SoftKeyButtonDesign.Row(ok), new Thickness(16, 18, 16, 18)), Close, () => DragMove());
    }
}
