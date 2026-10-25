using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Injection;

namespace VNForge.Core.Projects;

/// <summary>O que um teste no jogo colocou na pasta do jogo.</summary>
public enum GameTestKind
{
    /// <summary>A tradução do projeto, inteira ou de uma cena só.</summary>
    Translation,

    /// <summary>Só a frase fixa dos acentos (<see cref="GameTester.AccentPhrase"/>) na primeira fala.</summary>
    AccentTest,
}

/// <summary>Os cinco passos do teste no jogo, na ordem em que acontecem.</summary>
public enum GameTestStep
{
    /// <summary>Conferir a tradução (QA e codificação).</summary>
    Check,

    /// <summary>Preparar os arquivos em <c>build/</c>.</summary>
    Build,

    /// <summary>Guardar a cópia de segurança dos originais.</summary>
    Backup,

    /// <summary>Copiar os arquivos para a pasta do jogo.</summary>
    Inject,

    /// <summary>Abrir o executável do jogo.</summary>
    Open,
}

/// <summary>Quanto um passo do teste levou.</summary>
public sealed record GameTestStepTime(GameTestStep Step, long Milliseconds);

/// <summary>Qual build está na pasta do jogo agora, como o último teste o deixou.</summary>
/// <param name="AppliedAt">Quando o teste gravou o build no jogo.</param>
/// <param name="BackupDirectory">Nome da pasta em <c>backup/</c> com os originais trocados (<c>2026-10-22_2316</c>); nulo quando o jogo já estava igual ao build e nada foi copiado.</param>
/// <param name="ScriptKey">Chave da cena quando o teste foi só dela; nulo para a tradução inteira e para o teste de acentos.</param>
/// <param name="TextFingerprint">Impressão digital dos textos que entraram no build (<see cref="BuildState.FingerprintTexts"/>).</param>
public sealed record GameInGame(GameTestKind Kind, DateTimeOffset AppliedAt, string? BackupDirectory, string? ScriptKey, string? TextFingerprint);

/// <summary>O último teste que chegou ao fim: quando, de que tipo e o tempo de cada passo.</summary>
public sealed record GameLastTest(DateTimeOffset At, GameTestKind Kind, IReadOnlyList<GameTestStepTime> Steps);

/// <summary>
/// O estado do jogo em <c>.cache/game-state.json</c>: o build que está na pasta do jogo (<see cref="InGame"/>) e o último teste (<see cref="LastTest"/>).
/// A faixa de "Testar no jogo" e a barra de status leem este arquivo; quem escreve é <see cref="GameTester"/> e quem devolve o original
/// (<see cref="GameTester.ReturnToOriginal"/>, <c>vnf restore</c>) limpa <see cref="InGame"/>. Apagar o <c>.cache/</c> só faz o app esquecer o estado: o jogo não muda.
/// </summary>
public sealed record GameStateFile(int Version, GameInGame? InGame, GameLastTest? LastTest)
{
    public const int CurrentVersion = 1;

    private const string FileName = "game-state.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static GameStateFile Empty { get; } = new(CurrentVersion, null, null);

    public static string PathFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FileName);
    }

    /// <summary>O estado gravado, ou <see cref="Empty"/> quando não há arquivo ou ele não é legível: a tela não trava por causa dele.</summary>
    public static GameStateFile Load(ProjectFolder project)
    {
        try
        {
            var path = PathFor(project);
            var read = File.Exists(path) ? JsonSerializer.Deserialize<GameStateFile>(File.ReadAllText(path, Encoding.UTF8), Json) : null;
            return read is { Version: CurrentVersion } ? read : Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>Grava o estado de forma atômica (arquivo temporário e troca).</summary>
    public static void Save(ProjectFolder project, GameStateFile state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = PathFor(project);
        Directory.CreateDirectory(project.CacheDirectory);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state with { Version = CurrentVersion }, Json) + "\n", new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Lê, muda e regrava o estado. Uma falha de disco não derruba quem chama: o jogo já foi mexido, e o estado só alimenta a tela;
    /// no pior caso ela mostra o estado antigo até o próximo teste, e o app confere o backup antes de confiar neste arquivo.
    /// </summary>
    /// <param name="change">Devolve o próprio estado recebido para não gravar nada.</param>
    public static void TryUpdate(ProjectFolder project, Func<GameStateFile, GameStateFile> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        try
        {
            var state = Load(project);
            var next = change(state);
            if (!ReferenceEquals(next, state))
            {
                Save(project, next);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O estado se corrige no próximo teste.
        }
    }

    /// <summary>
    /// Injeta o <paramref name="plan"/> e mantém o estado em dia: só uma injeção que copiou algo (com backup) passa a ser "o que está no jogo", e uma que
    /// falhou e foi desfeita devolve o jogo ao original, então o estado antigo deixa de valer. Quem injeta fora do <see cref="GameTester"/> (<c>vnf inject</c>,
    /// o Injetar do app) usa este método para a faixa de "Testar no jogo" não descrever um jogo que já mudou.
    /// </summary>
    /// <param name="scriptKey">A cena, quando o build é de uma cena só.</param>
    public static InjectionResult Inject(ProjectFolder project, GameInjector injector, InjectionPlan plan, GameTestKind kind, string? scriptKey, TimeProvider clock, IProgress<InjectionPhase>? phases = null)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        InjectionResult result;
        try
        {
            result = injector.Inject(plan, phases);
        }
        catch (InjectionException)
        {
            TryClearInGame(project);
            throw;
        }

        if (result.BackupDirectory is { } backup)
        {
            TryUpdate(project, saved => saved with
            {
                InGame = new GameInGame(kind, clock.GetUtcNow(), Path.GetFileName(backup), scriptKey, BuildState.Load(project)?.TextFingerprint),
            });
        }

        return result;
    }

    /// <summary>O jogo voltou ao original: esquece o build que estava nele e guarda o último teste.</summary>
    public static void TryClearInGame(ProjectFolder project) =>
        TryUpdate(project, state => state.InGame is null ? state : state with { InGame = null });
}
