using System.Text.Json.Serialization;

namespace TymosPill.Models;

public sealed class LiveSessionState
{
    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("remainingSecs")]
    public int RemainingSecs { get; set; }

    [JsonPropertyName("totalSecs")]
    public int TotalSecs { get; set; }

    [JsonPropertyName("isBreak")]
    public bool IsBreak { get; set; }

    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }

    public static LiveSessionState SampleRunning { get; } = new()
    {
        Running = true,
        RemainingSecs = 1472,
        TotalSecs = 1500,
        IsBreak = false,
        TaskTitle = "Ship floating pill",
        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    public string FormatTime()
    {
        var secs = Math.Max(0, RemainingSecs);
        var m = secs / 60;
        var s = secs % 60;
        return $"{m:00}:{s:00}";
    }

    public string PhaseLabel()
    {
        if (!Running) return "Idle";
        return IsBreak ? "Break" : "Focus";
    }
}
