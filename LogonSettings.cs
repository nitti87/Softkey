using System;
using System.IO;
using System.Text.Json;

// An intentionally small data-only configuration, installed under Program Files by an administrator.
// No user profile paths, words, commands, plug-ins or executable names are accepted.
sealed class LogonSettings
{
    public int DwellMs { get; set; } = 800;
    public int JitterPx { get; set; } = 10;
    public double WindowWidth { get; set; } = 860;
    public double WindowHeight { get; set; } = 330;

    public Settings ToSettings()
    {
        var settings = new Settings
        {
            AutoClick = true, Repeat = false, AlwaysOnTop = true,
            DwellMs = DwellMs, JitterPx = JitterPx,
            WindowWidth = Math.Clamp(double.IsFinite(WindowWidth) ? WindowWidth : 860, 600, 1600),
            WindowHeight = Math.Clamp(double.IsFinite(WindowHeight) ? WindowHeight : 330, 240, 700),
            DragKeyPlacement = DragKeyPlacement.Top
        };
        settings.Normalize();
        return settings;
    }

    public static Settings Load(string installDirectory)
    {
        var settings = LoadFile(installDirectory).ToSettings();
        KeyboardLayoutSettings.ReadInstalled()?.ApplyTo(settings);
        settings.KeyboardArea = KeyboardAreaSize.ReadInstalled();
        settings.SmallDragKeyIcons = SignInDragKeySettings.ReadSmallSize();
        AutoclickTimingSettings.ReadInstalled()?.ApplyTo(settings);
        return settings;
    }

    internal static LogonSettings LoadFile(string installDirectory)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(installDirectory, "logon-settings.json"));
            if (stream.Length > 4096) return new LogonSettings();
            return JsonSerializer.Deserialize<LogonSettings>(stream) ?? new LogonSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LogonSettings();
        }
    }
}
