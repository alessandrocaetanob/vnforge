namespace VNForge.Core.Projects;

/// <summary>Conteúdo do <c>vnforge.json</c>. Não guarda chave de API: ela vem do ambiente ou do cofre do usuário.</summary>
public sealed record ProjectConfig(
    string Name,
    GameConfig Game,
    EngineConfig Engine,
    LanguagesConfig Languages,
    EncodingConfig Encoding,
    AiConfig Ai);

public sealed record GameConfig(string Path, string Exe);

public sealed record EngineConfig(string Id, int? Version = null, string? ScriptKey = null);

public sealed record LanguagesConfig(string Source, string Target);

public sealed record EncodingConfig(string Strategy);

public sealed record AiConfig(string Provider, string Model, string Effort, int BatchSize)
{
    public static AiConfig Default { get; } = new("anthropic", "claude-opus-5-5", "medium", 30);
}
