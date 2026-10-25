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
            Toasts.Show($"Não foi possível abrir a janela de tradução: {e.Message}", ToastKind.Error, key: "translate");
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
            Toasts.Show("Já há uma tradução em andamento. Espere ela terminar para pedir outra.", ToastKind.Warning, key: "translate");
            return;
        }

        ProjectFolder folder;
        try
        {
            folder = ProjectFolder.Open(project.Root);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Toasts.Show($"Não foi possível começar a tradução: {e.Message}", ToastKind.Error, key: "translate");
            return;
        }

        // A janela já mostrou o preço e a pessoa confirmou: a pergunta do valor de aviso, se viesse, seria a mesma.
        var job = new TranslationJob(
            folder,
            TranslationProviderFactory,
            new TranslationJobSettings(Settings.Rate, Settings.Budget, Settings.Concurrency),
            (_, _) => Task.FromResult(true));
        TranslationJobResult? result = null;
        translating = true;
        var task = TaskService.Start(TranslationTitle(request.Scope), async (reporter, ct) =>
        {
            try
            {
                result = await job.RunAsync(request, reporter, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not (OperationCanceledException or IOException or UnauthorizedAccessException))
            {
                throw new InvalidOperationException(ClaudeErrorMessages.Describe(e), e);
            }
        });

        await task.Completion;
        translating = false;
        Editor.Reload();
        ShowTranslationOutcome(task, result);
    }

    private static string TranslationTitle(TranslationScope scope) => scope switch
    {
        TranslationScope.Scene => "Traduzindo esta cena",
        TranslationScope.Chapter => "Traduzindo o capítulo",
        _ => "Traduzindo o jogo",
    };

    private void ShowTranslationOutcome(BackgroundTaskItem task, TranslationJobResult? result)
    {
        if (task.State == BackgroundTaskState.Cancelled)
        {
            Toasts.Show("Tradução cancelada. O que já voltou ficou salvo.", ToastKind.Warning, key: "translate");
        }
        else if (task.State == BackgroundTaskState.Failed || result is null)
        {
            Toasts.Show(task.Error ?? "A tradução não terminou.", ToastKind.Error, key: "translate");
        }
        else
        {
            var (text, kind) = DescribeOutcome(result);
            Toasts.Show(text, kind, key: "translate");
        }
    }

    private static (string Text, ToastKind Kind) DescribeOutcome(TranslationJobResult result)
    {
        var done = result.Translated == 1 ? "1 linha traduzida pela IA" : $"{ProjectLabels.Number(result.Translated)} linhas traduzidas pela IA";
        if (result.Stopped == BudgetStop.LimitReached)
        {
            return ($"A tradução parou no limite de gasto: {done}. As outras seguem pendentes.", ToastKind.Warning);
        }

        if (result.Failed > 0)
        {
            var left = result.Failed == 1 ? "1 linha ficou sem tradução" : $"{ProjectLabels.Number(result.Failed)} linhas ficaram sem tradução";
            return ($"{done}, mas {left}. {result.Problem}", ToastKind.Warning);
        }

        return result.Translated == 0
            ? ("Nenhuma linha foi traduzida.", ToastKind.Info)
            : ($"{done}. Elas aparecem como IA no Editor, para você revisar.", ToastKind.Success);
    }
}
