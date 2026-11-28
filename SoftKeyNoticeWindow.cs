using System.Windows;
using System.Windows.Controls;

sealed class SoftKeyNoticeWindow : Window
{
    const string InfoShieldOutline = "M 10,2 L 16.8,4.7 V 10.2 C 16.8,14.3 13.8,17.2 10,18.4 C 6.2,17.2 3.2,14.3 3.2,10.2 V 4.7 Z M 10,7 V 11.2 M 10,14.1 V 14.3";

    public SoftKeyNoticeWindow(string title, string message)
    {
        Title = title;
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;
        var text = new TextBlock
        {
            Text = message, FontSize = 13, Foreground = SoftKeyPopupDesign.PrimaryText,
            LineHeight = 20, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        var card = SoftKeyPopupDesign.PromptCard(SoftKeyPopupDesign.ActionSurface, SoftKeyPopupDesign.Accent, InfoShieldOutline, text);
        var ok = SoftKeyButtonDesign.Action("OK", true);
        ok.IsDefault = true; ok.IsCancel = true; ok.Click += (_, _) => Close();
        Content = SoftKeyPopupDesign.Frame(title,
            SoftKeyPopupDesign.Sections(430, card, SoftKeyButtonDesign.Row(ok)), Close, () => DragMove());
    }
}
