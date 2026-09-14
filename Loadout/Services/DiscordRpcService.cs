using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Loadout.Services;

/// <summary>
/// Cliente IPC nativo y silencioso para Discord Rich Presence.
/// No utiliza librerías externas ni añade dependencias pesadas.
/// </summary>
public sealed class DiscordRpcService : IDisposable
{
    private const string DefaultClientId = "1548875517409632346";
    private readonly string _clientId;
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    private bool _connected;

    public DiscordRpcService(string clientId = DefaultClientId)
    {
        _clientId = clientId;
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_connected && _pipe != null && _pipe.IsConnected) return true;
        _connected = false;
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;

        for (int i = 0; i < 10; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeoutCts = new CancellationTokenSource(250);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                await pipe.ConnectAsync(linkedCts.Token);

                // Handshake (Opcode 0)
                string handshake = JsonSerializer.Serialize(new { v = 1, client_id = _clientId });
                await SendPacketAsync(pipe, 0, handshake, ct);

                // Leer respuesta READY
                var (op, _) = await ReadPacketAsync(pipe, ct);
                if (op == 1)
                {
                    _pipe = pipe;
                    _connected = true;
                    return true;
                }
                pipe.Dispose();
            }
            catch
            {
                // Discord no está en este índice o no está abierto
            }
        }
        return false;
    }

    public async Task UpdatePresenceAsync(string state, string details, DateTime? startTime = null)
    {
        if (_disposed) return;
        await _lock.WaitAsync();
        try
        {
            if (!await EnsureConnectedAsync()) return;
            if (_pipe == null) return;

            long? startUnix = startTime.HasValue ? new DateTimeOffset(startTime.Value.ToUniversalTime()).ToUnixTimeSeconds() : null;

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = new
                    {
                        state = string.IsNullOrWhiteSpace(state) ? "En partida" : state,
                        details = string.IsNullOrWhiteSpace(details) ? "Loadout Booster" : details,
                        timestamps = startUnix.HasValue ? new { start = startUnix.Value } : null,
                        assets = new
                        {
                            large_image = "loadout",
                            large_text = "Loadout Game Booster"
                        }
                    }
                },
                nonce = Guid.NewGuid().ToString()
            };

            string json = JsonSerializer.Serialize(payload);
            await SendPacketAsync(_pipe, 1, json);
        }
        catch (Exception ex)
        {
            Log.Warn($"DiscordRpc.UpdatePresence: {ex.Message}");
            _connected = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearPresenceAsync()
    {
        if (_disposed) return;
        await _lock.WaitAsync();
        try
        {
            if (!_connected || _pipe == null || !_pipe.IsConnected) return;

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = (object?)null
                },
                nonce = Guid.NewGuid().ToString()
            };

            string json = JsonSerializer.Serialize(payload);
            await SendPacketAsync(_pipe, 1, json);
        }
        catch { }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task SendPacketAsync(Stream stream, int opcode, string json, CancellationToken ct = default)
    {
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[8];
        BitConverter.GetBytes(opcode).CopyTo(header, 0);
        BitConverter.GetBytes(payloadBytes.Length).CopyTo(header, 4);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payloadBytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<(int Opcode, string Payload)> ReadPacketAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[8];
        int read = await stream.ReadAsync(header, ct);
        if (read < 8) return (-1, string.Empty);

        int opcode = BitConverter.ToInt32(header, 0);
        int length = BitConverter.ToInt32(header, 4);
        if (length <= 0 || length > 65536) return (opcode, string.Empty);

        byte[] payloadBytes = new byte[length];
        int total = 0;
        while (total < length)
        {
            int r = await stream.ReadAsync(payloadBytes.AsMemory(total, length - total), ct);
            if (r <= 0) break;
            total += r;
        }
        return (opcode, Encoding.UTF8.GetString(payloadBytes, 0, total));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ClearPresenceAsync().Wait(200); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _connected = false;
        _lock.Dispose();
    }
}
