using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

sealed class SoftKeyUpdateWindow : Window
{
    readonly PasswordBox token = new() { Padding = new Thickness(8), MaxLength = 1024 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    readonly Button check = SoftKeyButtonDesign.Action("Sök efter uppdateringar", false);
    readonly Button install = SoftKeyButtonDesign.Action("Installera och starta om", true);
    readonly Button disconnect = SoftKeyButtonDesign.Action("Koppla från", false);
    readonly Func<Task<SoftKeyUpdates.Preview>> fetch;
    readonly Func<SoftKeyUpdates.Preview, Task> apply;
    SoftKeyUpdates.Preview available;
    bool busy;

    public SoftKeyUpdateWindow(SoftKeyUpdates.Preview initial, Func<Task<SoftKeyUpdates.Preview>> fetch,
        Func<SoftKeyUpdates.Preview, Task> apply)
    {
        Title = "Uppdateringar";
        SoftKeyPopupDesign.ConfigureWindow(this);
        available = initial;
        this.fetch = fetch;
        this.apply = apply;
        AutomationProperties.SetName(token, "GitHub-token");
        var label = new Label { Content = "GitHub-token för SoftKey", Target = token, Padding = new Thickness(0, 0, 0, 4) };
        var content = new StackPanel { Children = { status, label, token } };
        var close = SoftKeyButtonDesign.Action("Stäng", false);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        check.Click += async (_, _) => await Check();
        install.Click += async (_, _) => await Install();
        disconnect.Click += (_, _) =>
        {
            try { SoftKeyUpdates.ForgetCredential(); available = null; RefreshStatus(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Det gick inte att koppla från GitHub."; }
        };
        Content = SoftKeyPopupDesign.Frame(Title,
            SoftKeyPopupDesign.Sections(630, content, SoftKeyButtonDesign.Row(disconnect, close, check, install), new Thickness(16, 18, 16, 18)),
            Close, () => DragMove());
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        RefreshStatus();
    }

    void RefreshStatus()
    {
        status.Text = available != null ? $"Build {available.BuildNumber} är hämtad och verifierad."
            : SoftKeyUpdates.HasCredential ? "SoftKey söker efter nya versioner i bakgrunden."
            : "Klistra in ett GitHub-token med läsbehörighet till Contents för det privata SoftKey-repot.";
        SetBusy(false);
    }

    void SetBusy(bool value)
    {
        busy = value;
        token.IsEnabled = check.IsEnabled = !value;
        disconnect.IsEnabled = !value && SoftKeyUpdates.HasCredential;
        install.IsEnabled = !value && available != null && SoftKeyUpdates.CanInstall;
    }

    async Task Check()
    {
        SetBusy(true);
        status.Text = "Letar efter en nyare signerad version …";
        try
        {
            if (token.Password.Length > 0) { SoftKeyUpdates.SetCredential(token.Password); token.Clear(); }
            if (!SoftKeyUpdates.HasCredential) { RefreshStatus(); token.Focus(); return; }
            available = null;
            available = await fetch();
            status.Text = available == null ? "Du har den senaste versionen." : $"Build {available.BuildNumber} är hämtad och verifierad.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or OperationCanceledException)
        { status.Text = ex is ArgumentException or IOException ? ex.Message : "Uppdateringen kunde inte kontrolleras. Försök igen."; }
        finally { SetBusy(false); }
    }

    async Task Install()
    {
        SetBusy(true);
        status.Text = "Väntar på Windows godkännande …";
        try { await apply(available); SetBusy(false); Close(); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { status.Text = "Uppdateringen avbröts i Windows."; }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        { status.Text = "Uppdateringen kunde inte startas. Försök igen från skrivbordet."; }
        finally { SetBusy(false); }
    }
}
