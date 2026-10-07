using System.IO.Pipes;
using System.Threading.Channels;
using Goat.Shared.Product;

namespace Goat.Shared.Ipc;

/// <summary>
/// Ponta de comunicação de um componente. Cada lado hospeda um pipe de entrada (o seu) e conecta
/// no pipe do outro para enviar. Funciona em qualquer ordem de inicialização e sem o outro lado.
/// Os eventos chegam em threads do pool: quem usa WPF precisa levar o resultado para a thread de interface.
/// </summary>
public sealed class IpcPeer : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(3);

    private readonly ComponentId _self;
    private readonly ComponentId _other;
    private readonly string _scope;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<IpcPayload> _outbox = Channel.CreateBounded<IpcPayload>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    private Task? _server;
    private Task? _client;
    private volatile bool _peerPresent;

    /// <param name="scope">Isola os pipes (usuário atual por padrão). Testes usam um valor próprio.</param>
    public IpcPeer(ComponentId self, string? scope = null)
    {
        _self = self;
        _other = self == ComponentId.Dock ? ComponentId.Finder : ComponentId.Dock;
        _scope = scope ?? ProductInfo.UserSid;
    }

    /// <summary>Chega uma mensagem do outro componente (nunca <see cref="UnknownMessage"/>).</summary>
    public event Action<IpcPayload>? MessageReceived;

    /// <summary>O outro componente apareceu (true) ou foi embora (false).</summary>
    public event Action<bool>? PeerPresenceChanged;

    public bool IsPeerPresent => _peerPresent;

    public void Start()
    {
        if (_server != null) return;
        _server = Task.Run(() => RunServerAsync(_cts.Token));
        _client = Task.Run(() => RunClientAsync(_cts.Token));
    }

    /// <summary>Enfileira o envio. Sem o outro lado conectado, as mensagens ficam na fila (as mais antigas caem se encher).</summary>
    public void Send(IpcPayload payload) => _outbox.Writer.TryWrite(payload);

    private async Task RunServerAsync(CancellationToken ct)
    {
        var name = IpcProtocol.PipeName(_scope, _self);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                try
                {
                    while (await IpcFraming.ReadAsync(pipe, ct).ConfigureAwait(false) is { } message)
                    {
                        if (message is UnknownMessage) continue;
                        if (message is Hello hello && hello.Component == _other) SetPresence(true);
                        MessageReceived?.Invoke(message);
                    }
                }
                finally
                {
                    SetPresence(false);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                await DelayAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RunClientAsync(CancellationToken ct)
    {
        var target = IpcProtocol.PipeName(_scope, _other);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".", target, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(1000, ct).ConfigureAwait(false);

                await IpcFraming.WriteAsync(pipe, Greeting(), ct).ConfigureAwait(false);
                while (true)
                {
                    // Sem mensagens, reenvia o Hello: assim um outro lado que reiniciou nos reconhece e uma conexão morta é detectada.
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wait.CancelAfter(KeepAlive);
                    try
                    {
                        if (!await _outbox.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false)) return;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await IpcFraming.WriteAsync(pipe, Greeting(), ct).ConfigureAwait(false);
                        continue;
                    }

                    while (_outbox.Reader.TryRead(out var next))
                        await IpcFraming.WriteAsync(pipe, next, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // O outro lado não está aberto (ou fechou): tenta de novo depois.
                await DelayAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private Hello Greeting() => new(_self, ProductInfo.Version, IpcProtocol.Version);

    private static async Task DelayAsync(CancellationToken ct)
    {
        try { await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private void SetPresence(bool present)
    {
        if (_peerPresent == present) return;
        _peerPresent = present;
        PeerPresenceChanged?.Invoke(present);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _outbox.Writer.TryComplete();
        try { Task.WaitAll([_server ?? Task.CompletedTask, _client ?? Task.CompletedTask], TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _cts.Dispose();
    }
}
