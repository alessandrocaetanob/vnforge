using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>Por que a troca entre original e traduzido parou, no diálogo da Biblioteca. <paramref name="Differing"/> diz quantos arquivos o injetor achou diferentes, quando a recusa é essa.</summary>
public sealed record LibrarySwitchProblem(string Title, string Message, string? Differing = null, bool CanExtract = false)
{
    public bool HasDiffering => Differing is not null;
}

/// <summary>
/// "Original ou traduzido" na gaveta: a troca do <see cref="LibraryTranslationSwitcher"/> do Core. A volta é a restauração do backup e a ida é o teste no jogo sem abrir o executável,
/// então o app não grava nada na pasta do jogo por conta própria.
/// </summary>
public sealed partial class LibraryViewModel
{
    private LibraryGame? problemGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractAgainCommand))]
    public partial bool IsSwitching { get; private set; }

    [ObservableProperty]
    public partial string SwitchStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSwitchProblem))]
    [NotifyCanExecuteChangedFor(nameof(ExtractAgainCommand))]
    public partial LibrarySwitchProblem? SwitchProblem { get; private set; }

    /// <summary>O diálogo da recusa está aberto; fechá-lo (Fechar, Escape) limpa o problema.</summary>
    public bool HasSwitchProblem
    {
        get => SwitchProblem is not null;
        set
        {
            if (!value)
            {
                SwitchProblem = null;
            }
        }
    }

    [RelayCommand]
    private void CloseSwitchProblem() => SwitchProblem = null;

    /// <summary>A troca que está rodando ou rodou por último.</summary>
    public Task Switching { get; private set; } = Task.CompletedTask;

    internal void StartSwitch(LibraryCardViewModel card, bool toTranslated) => Switching = SwitchAsync(card, toTranslated);

    private async Task SwitchAsync(LibraryCardViewModel card, bool toTranslated)
    {
        if (switcher is null || IsSwitching)
        {
            return;
        }

        var game = card.Game;
        IsSwitching = true;
        SwitchStatus = toTranslated ? "Conferindo a tradução" : "Devolvendo os arquivos originais ao jogo";
        var steps = new Reporter<GameTestStep>(step => post(() => SwitchStatus = step.Label()));
        try
        {
            try
            {
                await Task.Run(() => toTranslated ? switcher.ToTranslated(game, steps, cancellationToken: CancellationToken.None) :switcher.ToOriginal(game), CancellationToken.None);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                problemGame = game;
                SwitchProblem = Describe(e);
            }

            // Com sucesso ou não, o que vale é o que a pasta tem agora: o cartão e a gaveta se refazem dele, e só então a próxima troca é liberada.
            versions[game.Id] = await Task.Run(() => ReadVersion(game), CancellationToken.None);
            Refilter();
        }
        finally
        {
            IsSwitching = false;
        }
    }

    /// <summary>"Extrair de novo" do diálogo do jogo que mudou: refaz a extração do projeto do jogo (o texto traduzido é mantido onde o original não mudou) e relê o estado.</summary>
    [RelayCommand(CanExecute = nameof(CanExtractAgain))]
    private async Task ExtractAgainAsync()
    {
        if (problemGame is not { ProjectPath: { } projectPath } game)
        {
            return;
        }

        IsSwitching = true;
        SwitchStatus = "Extraindo o texto do jogo de novo";
        try
        {
            try
            {
                var result = await Task.Run(() => ProjectExtractor.Extract(ProjectFolder.Open(projectPath), engines, cancellationToken: CancellationToken.None), CancellationToken.None);
                SwitchProblem = new LibrarySwitchProblem("O texto foi extraído de novo", SummaryOf(result.Reextraction));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                SwitchProblem = Describe(e);
            }

            versions[game.Id] = await Task.Run(() => ReadVersion(game), CancellationToken.None);
            Refilter();
        }
        finally
        {
            IsSwitching = false;
        }
    }

    private bool CanExtractAgain() => !IsSwitching && SwitchProblem is { CanExtract: true };

    private static string SummaryOf(ReextractionSummary? summary)
    {
        if (summary is null)
        {
            return "O texto do jogo foi extraído para o projeto.";
        }

        static string N(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        return $"{N(summary.Kept)} mantidas, {N(summary.Outdated)} desatualizadas (original mudou; tradução antiga guardada), {N(summary.Added)} novas, {N(summary.Removed)} removidas.";
    }

    /// <summary>O estado da pasta do jogo; uma pasta que não lê (o HD desligado, por exemplo) deixa só este jogo desconhecido, sem derrubar a Biblioteca.</summary>
    private static LibraryTranslationState ReadVersion(LibraryGame game)
    {
        try
        {
            return LibraryTranslationReader.Read(game);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new LibraryTranslationState(LibraryTranslationKind.Unknown, Reason: $"não foi possível ler a pasta do jogo: {e.Message}");
        }
    }

    internal static LibrarySwitchProblem Describe(Exception e) => e switch
    {
        QaBlockedException blocked => new LibrarySwitchProblem(
            "A Conferência travou a troca",
            $"Há erros que travam a tradução ({QaBlockedException.DescribeBlocking(blocked.Report)}) e nada foi gravado no jogo. Corrija em Conferir a tradução."),
        InjectionException { ChangedFiles.Count: > 0 } changed => new LibrarySwitchProblem(
            "O jogo mudou depois da extração",
            "A pasta do jogo tem arquivos diferentes dos que o VNForge leu. Para não corromper o jogo, o VNForge não grava nada nele.",
            $"{changed.ChangedFiles.Count} {(changed.ChangedFiles.Count == 1 ? "arquivo" : "arquivos")}",
            CanExtract: true),
        _ => new LibrarySwitchProblem("A troca não terminou", e.Message),
    };
}
