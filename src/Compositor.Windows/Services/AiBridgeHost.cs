using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Compositor.Windows.Services;

internal sealed class AiBridgeHost : IAsyncDisposable
{
    private readonly Func<AiBridgeRequest, Task<object?>> _handler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public AiBridgeHost(Func<AiBridgeRequest, Task<object?>> handler)
    {
        _handler = handler;
    }

    public bool IsRunning => _listenTask is not null && !_listenTask.IsCompleted;
    public string? PipeName { get; private set; }
    public string? Token { get; private set; }
    public string DiscoveryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CompositorWindows",
        "ai-bridge.json");

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        PipeName = $"CompositorWindows.AiBridge.{Environment.ProcessId}.{Guid.NewGuid():N}";
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        _cts = new CancellationTokenSource();
        WriteDiscoveryFile();
        _listenTask = ListenLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            if (_listenTask is not null)
            {
                await _listenTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _listenTask = null;
            DeleteDiscoveryFileIfOwned();
            PipeName = null;
            Token = null;
        }
    }

    public void UpdateProject(string? packagePath, string? documentId, bool modified)
    {
        if (!IsRunning)
        {
            return;
        }

        WriteDiscoveryFile(packagePath, documentId, modified);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && PipeName is not null)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);

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

                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AiBridgeResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<AiBridgeRequest>(line, JsonOptions)
                        ?? throw new InvalidDataException("Invalid AI bridge request.");

                    if (!CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(request.Token ?? string.Empty),
                            Encoding.UTF8.GetBytes(Token ?? string.Empty)))
                    {
                        response = AiBridgeResponse.Fail("Unauthorized AI bridge request.");
                    }
                    else
                    {
                        var result = await _handler(request);
                        response = AiBridgeResponse.Ok(result);
                    }
                }
                catch (Exception ex)
                {
                    response = AiBridgeResponse.Fail(ex.Message);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                StartupLog.Write("AI bridge listener error: " + ex);
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private void WriteDiscoveryFile(
        string? packagePath = null,
        string? documentId = null,
        bool modified = false)
    {
        if (PipeName is null || Token is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(DiscoveryPath)!;
        Directory.CreateDirectory(directory);

        var discovery = new
        {
            protocol = 1,
            transport = "named-pipe",
            pipeName = PipeName,
            token = Token,
            processId = Environment.ProcessId,
            projectPath = packagePath,
            documentId,
            modified,
            updatedAt = DateTimeOffset.UtcNow
        };

        var temp = DiscoveryPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(discovery, JsonOptions));
        File.Move(temp, DiscoveryPath, overwrite: true);
    }

    private void DeleteDiscoveryFileIfOwned()
    {
        try
        {
            if (!File.Exists(DiscoveryPath) || PipeName is null)
            {
                return;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(DiscoveryPath));
            if (json.RootElement.TryGetProperty("pipeName", out var pipeElement) &&
                string.Equals(pipeElement.GetString(), PipeName, StringComparison.Ordinal))
            {
                File.Delete(DiscoveryPath);
            }
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

internal sealed class AiBridgeRequest
{
    public string? Token { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonElement Parameters { get; set; }
}

internal sealed class AiBridgeResponse
{
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }

    public static AiBridgeResponse Ok(object? result)
        => new() { Success = true, Result = result };

    public static AiBridgeResponse Fail(string error)
        => new() { Success = false, Error = error };
}
