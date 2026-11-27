using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;

sealed class SoftKeyUpdateProgressView
{
    const string UpdateIconOutline = "M 10,3 V 12 M 10,12 L 6.5,8.5 M 10,12 L 13.5,8.5 M 4,15.5 H 16";

    readonly TextBlock status = new()
    {
        Name = "UpdateStatus", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
    };
    readonly TextBlock detail = new()
    {
        Name = "UpdateDetail", FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Foreground = SoftKeyPopupDesign.ActionText, Margin = new Thickness(0, 3, 0, 0)
    };
    readonly ProgressBar bar = new()
    {
        Name = "UpdateBar", Minimum = 0, Maximum = 100, Height = 6, Margin = new Thickness(0, 12, 0, 0),
        Background = SoftKeyPopupDesign.ActionSurface, Foreground = SoftKeyPopupDesign.Accent,
        Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ProgressBar}">
                <Border x:Name="PART_Track" Background="{TemplateBinding Background}" CornerRadius="3" ClipToBounds="True">
                    <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" HorizontalAlignment="Left" CornerRadius="3"/>
                </Border>
            </ControlTemplate>
            """)
    };

    public Border Content { get; }

    public SoftKeyUpdateProgressView()
    {
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(bar, "Uppdateringens förlopp");

        var glyph = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(UpdateIconOutline),
            Stroke = SoftKeyPopupDesign.Accent, StrokeThickness = 1.75,
            Width = 22, Height = 22, Stretch = System.Windows.Media.Stretch.Uniform,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var icon = SoftKeyUpdateWindow.CreateHeroTile(glyph);
        DockPanel.SetDock(icon, Dock.Left);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { status, detail }
        };
        var header = new DockPanel { Children = { icon, text } };

        Content = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 12, 14, 14), Child = new StackPanel { Children = { header, bar } }
        };
    }

    public void Show(SoftKeyUpdates.Progress progress)
    {
        status.Text = progress.Status;
        detail.Text = progress.Detail;
        bar.Value = progress.Percent;
    }

    public static string InstallationContent(double width)
    {
        var view = new SoftKeyUpdateProgressView();
        view.Show(new SoftKeyUpdates.Progress(60, "Förbereder installationen", "Kontrollerar det signerade paketet."));
        var body = new Border { Width = width, Child = view.Content, Padding = new Thickness(16) };
        return XamlWriter.Save(SoftKeyPopupDesign.Frame("Uppdaterar SoftKey", body, null, null));
    }
}
