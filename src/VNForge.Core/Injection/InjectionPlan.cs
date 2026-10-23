namespace VNForge.Core.Injection;

/// <summary>O que <see cref="GameInjector.Inject"/> vai fazer: os arquivos do build que diferem do jogo. Calculado sem gravar nada.</summary>
/// <param name="GameDirectory">Pasta do jogo (<c>game.path</c> do projeto, relativa à pasta do projeto quando não é absoluta).</param>
/// <param name="Files">Arquivos que serão gravados no jogo.</param>
/// <param name="UnchangedCount">Arquivos do build que já são idênticos aos do jogo e ficam de fora.</param>
public sealed record InjectionPlan(string GameDirectory, string BackupRoot, IReadOnlyList<PlannedFile> Files, int UnchangedCount);

/// <param name="RelativePath">Caminho relativo à pasta do jogo, com <c>/</c>.</param>
/// <param name="BuildPath">Arquivo em <c>build/</c>.</param>
/// <param name="TargetPath">Arquivo correspondente na pasta do jogo.</param>
/// <param name="BuildSha256">SHA-256 do arquivo do build.</param>
/// <param name="OriginalSha256">SHA-256 do original; nulo quando o jogo não tem o arquivo.</param>
/// <param name="TargetSha256">SHA-256 do arquivo no jogo na hora do plano; nulo quando ele não existe. A injeção confere que não mudou antes de gravar.</param>
/// <param name="OriginalSource">De onde sai a cópia do original: o próprio jogo, ou o backup anterior quando o jogo já está com o build de uma injeção passada. Nulo quando não há original.</param>
public sealed record PlannedFile(
    string RelativePath,
    string BuildPath,
    string TargetPath,
    string BuildSha256,
    string? OriginalSha256,
    string? OriginalSource,
    string? TargetSha256)
{
    public bool ReplacesExisting => OriginalSha256 is not null;
}

/// <param name="BackupDirectory">Pasta <c>backup/AAAA-MM-DD_HHmm</c> criada; nula quando não havia o que injetar.</param>
public sealed record InjectionResult(string GameDirectory, string? BackupDirectory, IReadOnlyList<string> Written, int UnchangedCount);

/// <summary>O que <see cref="GameInjector.Restore"/> vai devolver: o backup mais recente.</summary>
public sealed record RestorePlan(string GameDirectory, string BackupDirectory, BackupManifest Manifest);

/// <param name="Kept">Arquivos criados pela injeção que mudaram depois dela: o restore os deixa em paz.</param>
public sealed record RestoreResult(string GameDirectory, string BackupDirectory, IReadOnlyList<string> Restored, IReadOnlyList<string> Removed, IReadOnlyList<string> Kept);
