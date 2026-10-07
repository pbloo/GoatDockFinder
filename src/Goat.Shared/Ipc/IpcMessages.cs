using System.Text.Json;
using System.Text.Json.Serialization;
using Goat.Shared.Product;

namespace Goat.Shared.Ipc;

/// <summary>Mensagens trocadas entre GoatDock e GoatFinder. Acrescentar tipos novos é compatível; mudar os existentes exige subir <see cref="IpcProtocol.Version"/>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(DockBounds), "dock-bounds")]
[JsonDerivedType(typeof(OpenFolder), "open-folder")]
public abstract record IpcPayload;

/// <summary>Primeira mensagem de cada lado: diz quem é e qual protocolo fala.</summary>
public sealed record Hello(ComponentId Component, string ProductVersion, int ProtocolVersion) : IpcPayload;

/// <summary>Áreas ocupadas pela dock na tela (pixels físicos). Lista vazia quando a dock some ou fecha.</summary>
public sealed record DockBounds(IReadOnlyList<DockArea> Areas) : IpcPayload;

public sealed record DockArea(int X, int Y, int Width, int Height);

/// <summary>A dock pede ao Finder para abrir uma pasta.</summary>
public sealed record OpenFolder(string Path) : IpcPayload;

public static class IpcProtocol
{
    public const int Version = 1;

    /// <summary>Limite de uma mensagem; evita alocação absurda se o outro lado enviar lixo.</summary>
    public const int MaxFrameBytes = 64 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PipeName(string scope, ComponentId component) =>
        $"{ProductInfo.Name}.{scope}.{ProductInfo.ExecutableName(component)}";
}
