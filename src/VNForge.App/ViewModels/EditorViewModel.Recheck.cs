using System.Diagnostics;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// Os avisos do QA acompanham o que muda sem reabrir a app: gravar a linha refaz as regras dela na hora (<see cref="LineEntry.WithUnit"/>),
/// e esta parte refaz o resto em segundo plano, com o glossário, os personagens e o estilo de agora. As regras que olham o projeto inteiro
/// (<c>traducao-inconsistente</c>, <c>personagem-sem-genero</c>) esperam <see cref="RecheckDelay"/> depois da última gravação, para a digitação não
/// travar.
/// </summary>
public sealed partial class EditorViewModel
{
    /// <summary>Quanto o Editor espera, depois da última gravação ou mudança, para refazer as regras do projeto inteiro.</summary>
    public static readonly TimeSpan RecheckDelay = TimeSpan.FromMilliseconds(1500);

    private readonly Action<Action> uiContext = CaptureUiContext();
    private ITimer? recheckTimer;
    private int recheckVersion;

    /// <summary>A reconferência em andamento (ou a última); os testes a aguardam.</summary>
    public Task Rechecking { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Pede para refazer o QA de todas as linhas carregadas depois de <see cref="RecheckDelay"/> sem novo pedido. A janela chama quando o
    /// glossário, os personagens ou o estilo do projeto mudam; as gravações de linha chamam por conta própria.
    /// </summary>
    public void RequestRecheck()
    {
        if (data is null)
        {
            return;
        }

        if (recheckTimer is null)
        {
            recheckTimer = clock.CreateTimer(_ => (postToUi ?? uiContext)(() => Rechecking = RunRecheckAsync()), null, RecheckDelay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            recheckTimer.Change(RecheckDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Refaz já o QA de todas as linhas carregadas com os arquivos de agora (glossário, personagens, estilo e perfil), fora da thread da tela, e
    /// leva o resultado para a grade, o painel da linha, os contadores e o aviso do menu. Uma reconferência mais nova descarta uma mais antiga, e a
    /// que termina depois de outro projeto ser aberto não toca nas linhas novas.
    /// </summary>
    public Task RecheckAsync()
    {
        recheckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Rechecking = RunRecheckAsync();
    }

    private async Task RunRecheckAsync()
    {
        if (data is not { } lines || Project?.Root is not { } root)
        {
            return;
        }

        var version = ++recheckVersion;
        EditorContext loadedContext;
        RecheckResult result;
        try
        {
            loadedContext = contextLoader(root);
            var snapshot = lines.BeginRecheck(includeProjectRules: true);
            result = await Task.Run(snapshot.Run);
        }
        catch (Exception e)
        {
            // Uma regra que lança (um regex que estoura o tempo, por exemplo) não pode derrubar a tela nem deixar a tarefa sem observador:
            // os avisos ficam como estavam e a próxima gravação tenta de novo.
            Debug.WriteLine(e);
            return;
        }

        if (!ReferenceEquals(data, lines))
        {
            return;
        }

        // O glossário lido vale mesmo que outra reconferência mais nova tenha tomado o lugar desta.
        context = loadedContext;
        if (version != recheckVersion)
        {
            return;
        }

        ShowRechecked(lines.Apply(result), lines);
    }

    /// <summary>Leva as linhas cujas ocorrências mudaram para a grade do script aberto, o painel da linha e os contadores.</summary>
    private void ShowRechecked(IReadOnlyList<LineEntry> changed, ProjectLines lines)
    {
        if (changed.Count == 0)
        {
            return;
        }

        IssueCount = CountIssues(lines);
        var openKey = SelectedScript?.Key;
        var touchedOpenScript = false;
        foreach (var updated in changed)
        {
            var index = updated.Number - 1;
            if (!string.Equals(updated.ScriptKey, openKey, StringComparison.Ordinal) || index >= scriptLines.Length)
            {
                continue;
            }

            touchedOpenScript = true;
            scriptLines[index].Update(updated);
            entries[index] = updated;
            if (Detail is { } open && string.Equals(open.Id, updated.Unit.Id, StringComparison.Ordinal))
            {
                open.ShowIssues(updated.Issues);
            }
        }

        if (touchedOpenScript)
        {
            UpdateCounts();
        }
    }

    private static int CountIssues(ProjectLines lines) =>
        lines.Scripts.Sum(script => lines.LinesOf(script.Key).Sum(line => line.Issues.Count));

    private static Action<Action> CaptureUiContext()
    {
        var captured = SynchronizationContext.Current;
        return captured is null ? action => action() : action => captured.Post(_ => action(), null);
    }
}
