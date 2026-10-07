using System.Buffers.Binary;
using System.Text.Json;

namespace Goat.Shared.Ipc;

/// <summary>Quadro de mensagem: 4 bytes (little-endian) com o tamanho, seguidos do JSON em UTF-8.</summary>
public static class IpcFraming
{
    public static async Task WriteAsync(Stream stream, IpcPayload payload, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, IpcProtocol.Json);
        if (json.Length > IpcProtocol.MaxFrameBytes) throw new InvalidOperationException("Mensagem IPC grande demais.");

        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Lê uma mensagem. Retorna null no fim do fluxo. Tipos desconhecidos viram <see cref="UnknownMessage"/>.</summary>
    public static async Task<IpcPayload?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEofAsync(stream, header, ct).ConfigureAwait(false)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > IpcProtocol.MaxFrameBytes) throw new InvalidDataException("Tamanho de quadro IPC inválido.");

        var body = new byte[length];
        if (!await ReadExactlyOrEofAsync(stream, body, ct).ConfigureAwait(false)) return null;

        try { return JsonSerializer.Deserialize<IpcPayload>(body, IpcProtocol.Json) ?? UnknownMessage.Instance; }
        catch (JsonException) { return UnknownMessage.Instance; }
        catch (NotSupportedException) { return UnknownMessage.Instance; }
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}

/// <summary>Mensagem de uma versão mais nova que não conhecemos: é ignorada, não derruba a conexão.</summary>
public sealed record UnknownMessage : IpcPayload
{
    public static readonly UnknownMessage Instance = new();
}
