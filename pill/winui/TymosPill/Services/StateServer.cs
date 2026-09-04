using System.Net;
using System.Text;
using System.Text.Json;
using TymosPill.Models;

namespace TymosPill.Services;

/// <summary>Loopback HTTP bridge for LiveSessionState from web Tymos.</summary>
public sealed class StateServer : IDisposable
{
    public const int Port = 17865;
    public static string BaseUrl => $"http://127.0.0.1:{Port}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:8080",
        "http://127.0.0.1:8080",
    };

    private readonly HttpListener _listener = new();
    private readonly object _gate = new();
    private LiveSessionState _state = LiveSessionState.SampleRunning;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<LiveSessionState>? StateChanged;

    public LiveSessionState Current
    {
        get { lock (_gate) return Clone(_state); }
    }

    public void Start()
    {
        if (_loop != null) return;
        _listener.Prefixes.Add($"{BaseUrl}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => Handle(ctx), CancellationToken.None);
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            ApplyCors(req, res);

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            var path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (!string.Equals(path, "/v1/state", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = 404;
                await WriteText(res, "not found");
                return;
            }

            if (req.HttpMethod == "GET")
            {
                var snap = Current;
                res.StatusCode = 200;
                res.ContentType = "application/json; charset=utf-8";
                await WriteBytes(res, JsonSerializer.SerializeToUtf8Bytes(snap, JsonOptions));
                return;
            }

            if (req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                LiveSessionState? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<LiveSessionState>(body, JsonOptions);
                }
                catch (JsonException)
                {
                    res.StatusCode = 400;
                    await WriteText(res, "invalid json");
                    return;
                }

                if (parsed is null)
                {
                    res.StatusCode = 400;
                    await WriteText(res, "empty body");
                    return;
                }

                if (parsed.UpdatedAt == 0)
                {
                    parsed.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                parsed.TaskTitle ??= "";
                parsed.RemainingSecs = Math.Max(0, parsed.RemainingSecs);
                parsed.TotalSecs = Math.Max(0, parsed.TotalSecs);

                lock (_gate) _state = parsed;
                StateChanged?.Invoke(Clone(parsed));

                res.StatusCode = 204;
                res.Close();
                return;
            }

            res.StatusCode = 405;
            await WriteText(res, "method not allowed");
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* ignore */ }
        }
    }

    private static void ApplyCors(HttpListenerRequest req, HttpListenerResponse res)
    {
        var origin = req.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) && AllowedOrigins.Contains(origin))
        {
            res.Headers["Access-Control-Allow-Origin"] = origin;
            res.Headers["Vary"] = "Origin";
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }
    }

    private static async Task WriteText(HttpListenerResponse res, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.ContentType = "text/plain; charset=utf-8";
        await WriteBytes(res, bytes);
    }

    private static async Task WriteBytes(HttpListenerResponse res, byte[] bytes)
    {
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static LiveSessionState Clone(LiveSessionState s) => new()
    {
        Running = s.Running,
        RemainingSecs = s.RemainingSecs,
        TotalSecs = s.TotalSecs,
        IsBreak = s.IsBreak,
        TaskTitle = s.TaskTitle,
        UpdatedAt = s.UpdatedAt,
    };
}
