namespace VNForge.Core.Projects;

/// <summary>Conteúdo do <c>vnforge.json</c>. Não guarda chave de API: ela vem do ambiente ou do cofre do usuário.</summary>
public sealed record ProjectConfig(
    string Name,
    GameConfig Game,
    EngineConfig Engine,
    LanguagesConfig Languages,
    EncodingConfig Encoding,
    AiConfig Ai)
{
    /// <summary>Escolhas de estilo da tradução; o padrão vale para quem nunca as definiu e não é gravado.</summary>
    public StyleConfig Style { get; init; } = StyleConfig.Default;
}

public sealed record GameConfig(string Path, string Exe)
{
    /// <summary>Título da janela do jogo, lido pelo <c>vnf extract</c>; nulo enquanto o jogo não foi extraído ou não tem título.</summary>
    public GameTitleConfig? Title { get; init; }

    /// <summary>
    /// A versão do jogo de referência (<c>1.00</c>), a que o patch vale. O <c>vnf release</c> a grava ao verificar o patch, com a que o executável traz ou a que o
    /// usuário digitou (<c>--versao-do-jogo</c>, ou o campo do app quando o executável não traz versão); nula enquanto nenhum lançamento a guardou.
    /// </summary>
    public string? Version { get; init; }

    public const int MaxVersionLength = 64;

    /// <summary>
    /// A versão do jogo é texto livre (<c>1.01</c>, <c>Ver 1.2</c>): não pode ser vazia, ter espaço nas pontas, passar de <see cref="MaxVersionLength"/>
    /// nem ter caractere de controle. O que vem do jogador ou do executável passa antes por <see cref="NormalizeVersion"/>; o <c>vnforge.json</c> só aceita
    /// o valor já normalizado.
    /// </summary>
    public static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && version == version.Trim() && version.Length <= MaxVersionLength && !version.Any(char.IsControl);

    /// <summary>A versão sem os espaços das pontas, ou nula quando em branco ou inválida mesmo assim.</summary>
    public static string? NormalizeVersion(string? version)
    {
        var trimmed = version?.Trim();
        return IsValidVersion(trimmed) ? trimmed : null;
    }
}

/// <summary>O título da janela do jogo: o original, lido do jogo, e a tradução que o build grava no lugar dele.</summary>
/// <param name="Original">Título como o jogo o traz (no YU-RIS, o do <c>yscfg.ybn</c>).</param>
/// <param name="Translation">Título traduzido; nulo ou em branco deixa o título original no jogo.</param>
public sealed record GameTitleConfig(string Original, string? Translation);

public sealed record EngineConfig(string Id, int? Version = null, string? ScriptKey = null);

public sealed record LanguagesConfig(string Source, string Target);

public sealed record EncodingConfig(string Strategy);

public sealed record AiConfig(string Provider, string Model, string Effort, int BatchSize)
{
    /// <summary>Quantas falas antes e depois do lote entram no pedido quando o projeto não define a janela.</summary>
    public const int DefaultContextLines = 2;

    /// <summary>O maior número de falas de contexto, de cada lado, que o <c>vnforge.json</c> aceita.</summary>
    public const int MaxContextLines = 30;

    public static AiConfig Default { get; } = new("anthropic", "claude-opus-5-5", "medium", 30);

    /// <summary>Falas anteriores ao lote enviadas como contexto (<c>ai.contextBefore</c>); só as já traduzidas contam.</summary>
    public int ContextBefore { get; init; } = DefaultContextLines;

    /// <summary>Falas seguintes ao lote enviadas como contexto (<c>ai.contextAfter</c>), no original.</summary>
    public int ContextAfter { get; init; } = DefaultContextLines;
}
