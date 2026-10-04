using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Compositor.Mcp;

internal static class BridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> CallAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var discovery = ReadDiscovery();

        using var pipe = new NamedPipeClientStream(
            ".",
            discovery.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(3000, cancellationToken);

        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024,
            leaveOpen: true);

        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            bufferSize: 16 * 1024,
            leaveOpen: true)
        {
            AutoFlush = true
        };

        var request = new
        {
            token = discovery.Token,
            method,
            parameters = parameters ?? new { }
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));

        var responseLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new InvalidOperationException("Compositor closed the AI bridge without a response.");
        }

        using var response = JsonDocument.Parse(responseLine);
        var root = response.RootElement;

        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : "Unknown Compositor AI bridge error.";
            throw new InvalidOperationException(error);
        }

        if (!root.TryGetProperty("result", out var result))
        {
            return "{}";
        }

        return result.GetRawText();
    }

    public static string GetConnectionSummary()
    {
        var discovery = ReadDiscovery();
        return JsonSerializer.Serialize(new
        {
            connected = true,
            discovery.ProcessId,
            discovery.ProjectPath,
            discovery.DocumentId,
            discovery.Modified
        }, JsonOptions);
    }

    private static DiscoveryState ReadDiscovery()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompositorWindows",
            "ai-bridge.json");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Compositor AI Bridge is not enabled. Open Compositor for Windows and enable AI Bridge first.");
        }

        var state = JsonSerializer.Deserialize<DiscoveryState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The Compositor AI bridge discovery file is invalid.");

        if (string.IsNullOrWhiteSpace(state.PipeName) || string.IsNullOrWhiteSpace(state.Token))
        {
            throw new InvalidDataException("The Compositor AI bridge discovery file is incomplete.");
        }

        return state;
    }

    private sealed class DiscoveryState
    {
        public string PipeName { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string? ProjectPath { get; set; }
        public string? DocumentId { get; set; }
        public bool Modified { get; set; }
    }
}
