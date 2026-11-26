using System;
using System.Threading.Tasks;

sealed record StartupChoices(bool Login, bool Automatic, bool AfterUnlock = false)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool StartsOnDesktop => Automatic || AfterUnlock;
}
sealed record StartupProgress(int Percent, string Message, bool Complete = false, bool Success = false, StartupChoices State = null);
sealed class StartupRequest
{
    public bool Login { get; set; }
    public bool Automatic { get; set; }
    public bool AfterUnlock { get; set; }
    public int DwellMs { get; set; } = 800;
    public int JitterPx { get; set; } = 10;
    public double WindowWidth { get; set; } = 860;
    public double WindowHeight { get; set; } = 330;
    public KeyboardLayoutSettings? KeyboardLayout { get; set; }
    public KeyboardAreaSize? KeyboardArea { get; set; }
    public AutoclickTimingSettings? AutoclickTiming { get; set; }
    public bool? SmallDragKeyIcons { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public StartupChoices Choices => new(Login, Automatic, AfterUnlock);
}

interface ISoftKeyStartupService
{
    StartupChoices Read();
    Task<StartupChoices> ApplyAsync(StartupChoices choices, IProgress<StartupProgress> progress);
}
