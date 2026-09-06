using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TymosPill.Models;

namespace TymosPill.Services;

/// <summary>
/// Loopback HTTP bridge for LiveSessionState from web Tymos.
///
/// Raw TcpListener instead of HttpListener: http.sys requires a URL ACL
/// (netsh http add urlacl) for prefixes like http://127.0.0.1:17865/ when the
/// process is not elevated — without one Start() throws AccessDenied and the
/// pill silently freezes on its sample state. A loopback TcpListener needs no
/// reservation, so the bridge works for any user with zero setup.
/// </summary>
public sealed class StateServer : IDisposable
{
    public const int Port = 17865;
    public static string BaseUrl => $"http://127.0.0.1:{Port}";

    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:8080",
        "http://127.0.0.1:8080",
    };

    private readonly TcpListener _listener = new(IPAddress.Loopback, Port);
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
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener.Stop(); } catch { /* ignore */ }
        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = Task.Run(() => HandleClient(client), CancellationToken.None);
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var req = await ReadRequestAsync(stream);
                if (req is not null)
                {
                    await Handle(req, stream);
                }
            }
        }
        catch
        {
            // Client hung up or timed out mid-request; drop the connection.
        }
    }

    private sealed record Request(string Method, string Path, string? Origin, string Body);

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[MaxHeaderBytes];
        var received = 0;
        var headerEnd = -1;
        while (received < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received))
                    .AsTask().WaitAsync(ReadTimeout);
            }
            catch (TimeoutException)
            {
                return null;
            }
            if (read == 0) return null;

            var scanFrom = Math.Max(0, received - 3);
            received += read;
            for (var i = scanFrom; i <= received - 4; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    headerEnd = i;
                    break;
                }
            }
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return null;

        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;
        var method = requestLine[0].ToUpperInvariant();
        var rawPath = requestLine[1];
        var path = rawPath.StartsWith('/') ? rawPath : "/" + rawPath;

        string? origin = null;
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var sep = line.IndexOf(':');
            if (sep <= 0) continue;
            var name = line[..sep].Trim();
            var value = line[(sep + 1)..].Trim();
            if (string.Equals(name, "Origin", StringComparison.OrdinalIgnoreCase)) origin = value;
            else if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(value, out contentLength);
            }
        }

        contentLength = Math.Clamp(contentLength, 0, MaxBodyBytes);
        var body = new byte[contentLength];
        var bodyStart = headerEnd + 4;
        var have = Math.Min(contentLength, received - bodyStart);
        if (have > 0) Array.Copy(buffer, bodyStart, body, 0, have);
        while (have < contentLength)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(body.AsMemory(have, contentLength - have))
                    .AsTask().WaitAsync(ReadTimeout);
            }
            catch (TimeoutException)
            {
                return null;
            }
            if (read == 0) return null;
            have += read;
        }

        return new Request(method, path.TrimEnd('/'), origin, Encoding.UTF8.GetString(body));
    }

    private async Task Handle(Request req, NetworkStream stream)
    {
        if (!string.Equals(req.Path, "/v1/state", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponse(stream, 404, "not found", req.Origin);
            return;
        }

        switch (req.Method)
        {
            case "OPTIONS":
                await WriteResponse(stream, 204, "", req.Origin);
                return;

            case "GET":
                var snap = Current;
                await WriteResponse(
                    stream, 200, JsonSerializer.Serialize(snap, JsonOptions), req.Origin,
                    "application/json; charset=utf-8");
                return;

            case "POST":
                LiveSessionState? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<LiveSessionState>(req.Body, JsonOptions);
                }
                catch (JsonException)
                {
                    await WriteResponse(stream, 400, "invalid json", req.Origin);
                    return;
                }

                if (parsed is null)
                {
                    await WriteResponse(stream, 400, "empty body", req.Origin);
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

                await WriteResponse(stream, 204, "", req.Origin);
                return;

            default:
                await WriteResponse(stream, 405, "method not allowed", req.Origin);
                return;
        }
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

    private static async Task WriteResponse(
        NetworkStream stream,
        int statusCode,
        string body,
        string? origin,
        string? contentType = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(Reason(statusCode)).Append("\r\n");
        if (origin is not null && AllowedOrigins.Contains(origin))
        {
            sb.Append("Access-Control-Allow-Origin: ").Append(origin).Append("\r\n");
            sb.Append("Vary: Origin\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
        }
        if (body.Length > 0)
        {
            if (contentType is not null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n");
        }
        sb.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        if (body.Length > 0)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(body));
        }
        await stream.FlushAsync();
    }

    private static string Reason(int code) => code switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        _ => "OK",
    };
}
