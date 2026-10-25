using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>A janela "Traduzir com IA", sobre o Editor.</summary>
    public TranslateDialogViewModel Translate { get; } = new();

    /// <summary>Monta o provedor da IA para (modelo, esforço). O app passa o Claude com a chave do usuário; os testes passam o provedor falso.</summary>
    public Func<string, ClaudeEffort, ITranslationProvider> TranslationProviderFactory { get; init; } =
        static (_, _) => throw new InvalidOperationException("Nenhum provedor de IA configurado.");

    /// <summary>Termina quando a janela abriu e leu o projeto.</summary>
    public Task TranslateOpening { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando a última tradução pedida acabou e o resultado foi mostrado.</summary>
    public Task TranslationRun { get; private set; } = Task.CompletedTask;

    private const string ToastKey = "translate";

    private bool translating;

    private void HookTranslateDialog()
    {
        Translate.StartRequested += (_, request) => TranslationRun = StartTranslation(request);
        Translate.GlossaryRequested += (_, _) =>
        {
            Glossary.SelectedTab = GlossaryTab.Characters;
            SelectedNav = glossaryItem;
        };
        Translate.SettingsRequested += (_, _) => SelectedNav = settingsItem;
    }

    private void OnEditorTranslateRequested(object? sender, string scriptKey)
    {
        if (CurrentProject is not { } project)
        {
            return;
        }

        try
        {
            var folder = ProjectFolder.Open(project.Root);
            TranslateOpening = Translate.OpenAsync(
                folder,
                scriptKey,
                new TranslateDialogSettings(Settings.Rate, Settings.Budget, Settings.DefaultQuality, ApiKeyConfigured));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Toasts.Show($"Não foi possível abrir a janela de tradução: {e.Message}", ToastKind.Error, key: ToastKey);
        }
    }

    private async Task StartTranslation(TranslationRequest request)
    {
        if (CurrentProject is not { } project)
        {
            return;
        }

        // Duas execuções somariam o gasto cada uma por conta própria e poderiam passar do limite juntas.
        if (translating)
        {
            Toasts.Show("Já há uma tradução em andamento. Espere ela terminar para pedir outra.", ToastKind.Warning, key: ToastKey);
            return;
        }

        ProjectFolder folder;
        try
        {
            folder = ProjectFolder.Open(project.Root);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Toasts.Show($"Não foi possível começar a tradução: {e.Message}", ToastKind.Error, key: ToastKey);
            return;
        }

        // A janela já mostrou o preço e a pessoa confirmou: a pergunta do valor de aviso, se viesse, seria a mesma.
        var job = new TranslationJob(
            folder,
            TranslationProviderFactory,
            new TranslationJobSettings(Settings.Rate, Settings.Budget, Settings.Concurrency),
            (_, _) => Task.FromResult(true));
        TranslationJobResult? result = null;
        var canceled = false;
        string? failure = null;
        translating = true;
        var task = TaskService.Start(TranslationTitle(request.Scope), async (reporter, ct) =>
        {
            try
            {
                result = await job.RunAsync(request, reporter, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                canceled = true;
                throw;
            }
            catch (Exception e)
            {
                failure = e is IOException or UnauthorizedAccessException ? e.Message : ClaudeErrorMessages.Describe(e);
                throw new InvalidOperationException(failure, e);
            }
        });

        await task.Completion;
        translating = false;
        Editor.Reload();

        // O estado da tarefa chega por post depois do Completion: o que decide o aviso é o que a própria tarefa guardou.
        ShowTranslationOutcome(canceled, failure, result);
    }

    private static string TranslationTitle(TranslationScope scope) => scope switch
    {
        TranslationScope.Scene => "Traduzindo esta cena",
        TranslationScope.Chapter => "Traduzindo o capítulo",
        _ => "Traduzindo o jogo",
    };

    private void ShowTranslationOutcome(bool canceled, string? failure, TranslationJobResult? result)
    {
        if (canceled)
        {
            Toasts.Show("Tradução cancelada. O que já voltou ficou salvo.", ToastKind.Warning, key: ToastKey);
        }
        else if (failure is not null || result is null)
        {
            Toasts.Show(failure ?? "A tradução não terminou.", ToastKind.Error, key: ToastKey);
        }
        else
        {
            var (text, kind) = DescribeOutcome(result);
            Toasts.Show(text, kind, key: ToastKey);
        }
    }

    public static (string Text, ToastKind Kind) DescribeOutcome(TranslationJobResult result)
    {
        var done = Lines(result.Translated, "linha traduzida pela IA", "linhas traduzidas pela IA");
        var stoppedAtLimit = result.Stopped == BudgetStop.LimitReached;
        var parts = new List<string> { stoppedAtLimit ? $"A tradução parou no limite de gasto: {done}." : $"{done}." };
        if (stoppedAtLimit)
        {
            parts.Add("As outras seguem pendentes.");
        }

        if (result.Failed > 0)
        {
            parts.Add($"{Lines(result.Failed, "linha ficou", "linhas ficaram")} sem tradução porque o lote não voltou. {result.Problem}".TrimEnd());
        }

        if (result.Refused > 0)
        {
            parts.Add($"{Lines(result.Refused, "linha não passou", "linhas não passaram")} na validação e {(result.Refused == 1 ? "segue" : "seguem")} sem tradução.");
        }

        if (result.Skipped > 0)
        {
            parts.Add($"{Lines(result.Skipped, "linha mudou", "linhas mudaram")} durante a execução e {(result.Skipped == 1 ? "ficou" : "ficaram")} como você deixou.");
        }

        var needsAttention = stoppedAtLimit || result.Failed > 0 || result.Refused > 0;
        if (result.Translated == 0 && !needsAttention && result.Skipped == 0)
        {
            return ("Nenhuma linha foi traduzida.", ToastKind.Info);
        }

        if (!needsAttention && result.Translated > 0)
        {
            parts.Add("Elas aparecem como IA no Editor, para você revisar.");
        }

        return (string.Join(' ', parts), needsAttention ? ToastKind.Warning : ToastKind.Success);
    }

    private static string Lines(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{ProjectLabels.Number(count)} {plural}";
}
