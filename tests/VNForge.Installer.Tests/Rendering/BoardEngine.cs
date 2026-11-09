using Microsoft.Extensions.Time.Testing;
using VNForge.Patching;

namespace VNForge.Installer.Tests.Rendering;

/// <summary>
/// O jogo e o patch do quadro #540 (Amaoto no Hakoniwa, 1.2.0, quatro arquivos, 218 MB), sem disco: os dados que as cinco janelas do quadro mostram.
/// A instalação para no ponto que o teste pede, para a janela "Instalando" ser vista com 63%.
/// </summary>
internal sealed class BoardEngine(FakeTimeProvider? time = null, byte[]? cover = null) : IInstallerEngine
{
    public static readonly string Folder = OperatingSystem.IsWindows() ? @"D:\VNs\Amaoto no Hakoniwa" : "/mnt/d/VNs/Amaoto no Hakoniwa";

    public const string ExpectedSha = "3f9a00000000000000000000000000000000000000000000000000000000c21d";

    public const string FoundSha = "77b000000000000000000000000000000000000000000000000000000000e04a";

    private const long Mib = 1024 * 1024;

    private static readonly string[] Paths = ["script.ypf", "Game.exe", "font.ypf", "system.ypf"];

    private static readonly long[] OriginalSizes = [150 * Mib, 30 * Mib, 20 * Mib, 18 * Mib];

    private static readonly long[] ResultSizes = [60 * Mib, 77 * Mib, 41 * Mib, 40 * Mib];

    public enum Situation
    {
        Ready,
        WrongVersion,
        Installed,
    }

    public Situation State { get; init; } = Situation.Ready;

    /// <summary>Quando a instalação chega a este ponto do andamento, avisa <see cref="Reached"/> e espera <see cref="Release"/>.</summary>
    public Func<PatchProgress, bool>? PauseAt { get; init; }

    public ManualResetEventSlim Reached { get; } = new();

    public ManualResetEventSlim Release { get; } = new();

    public PatchManifest Manifest { get; } = new(
        "Amaoto no Hakoniwa",
        "1.2.0",
        null,
        "Equipe Chuva Mansa",
        "Leia-me",
        [.. Paths.Select((p, i) => new PatchFileEntry(p, p == "Game.exe" ? ExpectedSha : Sha(i), OriginalSizes[i], Sha(10 + i), ResultSizes[i], 0, 1))],
        "1.00",
        cover);

    private static string Sha(int seed) => $"{seed:x2}".PadRight(64, 'a');

    public PatchCheck Check(string folder)
    {
        var files = Manifest.Files.Select(f => State switch
        {
            Situation.Installed => new PatchFileStatus(f.Path, PatchFileState.Patched, f.OriginalSha256, f.ResultSha256),
            Situation.WrongVersion when f.Path == "Game.exe" => new PatchFileStatus(f.Path, PatchFileState.Different, f.OriginalSha256, FoundSha),
            _ => new PatchFileStatus(f.Path, PatchFileState.Original, f.OriginalSha256, f.OriginalSha256),
        }).ToList();
        var state = State switch
        {
            Situation.Installed => PatchCheckState.AlreadyInstalled,
            Situation.WrongVersion => PatchCheckState.WrongVersion,
            _ => PatchCheckState.Ready,
        };
        return new PatchCheck(state, files, State == Situation.Installed ? "1.2.0" : null);
    }

    public PatchUninstallPlan PlanUninstall(string folder) =>
        new("1.2.0", Paths, [], [], [], new DateTimeOffset(2026, 11, 8, 20, 3, 0, TimeSpan.Zero));

    public PatchInstallResult Install(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken)
    {
        Step(progress, new PatchProgress(PatchPhase.Check, null, 0, 4));
        time?.Advance(TimeSpan.FromMilliseconds(200));
        for (var i = 0; i < 4; i++)
        {
            Step(progress, new PatchProgress(PatchPhase.Backup, Paths[i], i, 4));
        }

        for (var i = 0; i < 4; i++)
        {
            Step(progress, new PatchProgress(PatchPhase.Apply, Paths[i], i, 4));
        }

        for (var i = 0; i < 4; i++)
        {
            Step(progress, new PatchProgress(PatchPhase.Verify, Paths[i], i, 4));
        }

        return new PatchInstallResult(Paths, Path.Combine(folder, PatchInstaller.BackupFolderName));
    }

    public PatchUninstallResult Uninstall(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 4; i++)
        {
            Step(progress, new PatchProgress(PatchPhase.Restore, Paths[i], i, 4));
        }

        return new PatchUninstallResult(Paths, [], []);
    }

    private void Step(IProgress<PatchProgress> progress, PatchProgress step)
    {
        progress.Report(step);
        if (PauseAt?.Invoke(step) == true)
        {
            Reached.Set();
            Release.Wait(TimeSpan.FromMinutes(3));
        }
    }
}
