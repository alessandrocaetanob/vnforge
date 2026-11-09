using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Review;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string EditorPassToastKey = "editor-pass";

    /// <summary>Monta o provedor da passada de editor (#532) para (modelo, esforço). O app passa o Claude com a chave do usuário; os testes passam o provedor falso.</summary>
    public Func<string, ClaudeEffort, IEditorPassProvider> EditorPassProviderFactory { get; init; } =
        static (_, _) => throw new InvalidOperationException("Nenhum provedor de IA configurado.");

    /// <summary>
    /// As cenas do pedido que tinham linha pendente antes de traduzir: são as que a passada de editor lê depois. Uma cena que já estava toda traduzida fica de fora, para
    /// "traduzir o jogo" num jogo pela metade não pagar a passada de novo nas cenas antigas.
    /// </summary>
    private static IReadOnlyList<string> ScenesToEdit(ProjectFolder folder, TranslationRequest request)
    {
        if (!request.EditorPass)
        {
            return [];
        }

        var wanted = request.ScriptKeys.ToHashSet(StringComparer.Ordinal);
        return [.. folder.LoadAllText().Where(pair => wanted.Contains(pair.Key) && pair.Value.Any(TranslationQuoter.IsPending)).Select(pair => pair.Key)];
    }

    /// <summary>Roda a passada de editor nas cenas que acabaram de ser traduzidas e mostra o resultado. Só deixa sugestões: nenhuma tradução é gravada.</summary>
    private async Task RunEditorPassAsync(ProjectSummary project, ProjectFolder folder, TranslationRequest request, IReadOnlyList<string> sceneKeys)
    {
        var job = NewEditorPassJob(project, folder);
        EditorPassJobResult? result = null;
        var canceled = false;
        string? failure = null;
        var task = TaskService.Start("Passada de editor", async (reporter, ct) =>
        {
            try
            {
                result = await job.RunAsync(sceneKeys, request.Quality, reporter, ct).ConfigureAwait(false);
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
        }, usesAi: true);

        await task.Completion;
        Editor.RefreshEditorSuggestions();
        var (text, kind) = DescribeEditorPass(canceled, failure, result);
        Toasts.Show(text, kind, key: EditorPassToastKey);
    }

    // Como na tradução, a cena de exemplo usa a IA falsa: sem chave, sem rede e sem gasto.
    private EditorPassJob NewEditorPassJob(ProjectSummary project, ProjectFolder folder) => new(
        folder,
        project.IsSample ? static (_, _) => new FakeEditorPassProvider() : EditorPassProviderFactory,
        new TranslationJobSettings(Settings.Rate, project.IsSample ? BudgetLimits.None : Settings.Budget, Settings.Concurrency),
        project.IsSample ? static _ => SampleAi.NoUsage : null);

    /// <summary>"Outra" no cartão da sugestão: uma proposta nova para a linha, na qualidade padrão dos Ajustes. Sem projeto aberto ou sem chave, não há o que pedir.</summary>
    private Task<EditorSuggestion?> AnotherEditorSuggestionAsync(EditorSuggestion rejected, CancellationToken ct)
    {
        if (CurrentProject is not { } project)
        {
            return Task.FromResult<EditorSuggestion?>(null);
        }

        if (!ApiKeyConfigured && !project.IsSample)
        {
            throw new LineSuggestionException("Falta a chave da IA. Informe-a em Ajustes, na aba IA e gasto.");
        }

        return NewEditorPassJob(project, ProjectFolder.Open(project.Root)).AnotherAsync(rejected, Settings.DefaultQuality, ct);
    }

    public static (string Text, ToastKind Kind) DescribeEditorPass(bool canceled, string? failure, EditorPassJobResult? result)
    {
        if (canceled)
        {
            return ("Passada de editor cancelada. As sugestões que já voltaram ficaram salvas.", ToastKind.Warning);
        }

        if (failure is not null || result is null)
        {
            return ($"A passada de editor não terminou. {failure} A tradução não mudou.".Replace("  ", " ", StringComparison.Ordinal).TrimEnd(), ToastKind.Error);
        }

        var scenes = result.Scenes == 1 ? "1 cena" : $"{ProjectLabels.Number(result.Scenes)} cenas";
        var parts = new List<string>
        {
            result.Suggestions == 0
                ? $"A passada de editor leu {scenes} e não achou ajuste."
                : $"A passada de editor deixou {(result.Suggestions == 1 ? "1 sugestão" : $"{ProjectLabels.Number(result.Suggestions)} sugestões")} em {scenes}. Elas aparecem na linha, com Aceitar e Ignorar; nada foi gravado.",
        };
        if (result.Stopped == BudgetStop.LimitReached)
        {
            parts.Add("Parou no limite de gasto; as outras cenas ficaram sem passada.");
        }

        if (result.Failed > 0)
        {
            parts.Add($"{(result.Failed == 1 ? "1 cena ficou" : $"{ProjectLabels.Number(result.Failed)} cenas ficaram")} sem passada. {result.Problem}".TrimEnd());
        }

        var needsAttention = result.Stopped is not null || result.Failed > 0;
        return (string.Join(' ', parts), needsAttention ? ToastKind.Warning : result.Suggestions == 0 ? ToastKind.Info : ToastKind.Success);
    }
}
