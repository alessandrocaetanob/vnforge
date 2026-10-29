using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>A fala do leitor como a caixa do jogo a desenha; <paramref name="Layout"/> é nulo quando o pipeline do build recusa o texto (<paramref name="Problem"/> diz por quê).</summary>
/// <param name="ShowsSource">A fala não tem tradução e a caixa mostra o original.</param>
public sealed record ReadingLine(TextboxLayout? Layout, string? Problem, bool ShowsSource);

/// <summary>O ponto de entrada do leitor de cena (#475) e a marca "soa estranho"; a tela e a navegação estão em <see cref="ReadSceneViewModel"/>.</summary>
public sealed partial class EditorViewModel
{
    /// <summary>O leitor aberto; nulo no Editor normal. A tela do Editor mostra o leitor no lugar da grade enquanto houver um.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadingScene), nameof(IsGridShown))]
    [NotifyCanExecuteChangedFor(nameof(CloseSearchCommand))]
    public partial ReadSceneViewModel? Reading { get; private set; }

    public bool IsReadingScene => Reading is not null;

    /// <summary>O cabeçalho, a barra e a grade do Editor aparecem; o modo revisão e o leitor tomam o lugar deles e os deixam fora da tela e do foco.</summary>
    public bool IsGridShown => !IsReviewing && !IsReadingScene;

    private bool CanStartReading() => SelectedScript is not null && scriptLines.Length > 0;

    /// <summary>Abre o leitor na cena aberta, a partir da linha selecionada (ou da primeira): uma fala por vez, sem nada para aprovar. Sem script ou sem linhas, não faz nada.</summary>
    [RelayCommand(CanExecute = nameof(CanStartReading))]
    public void StartReading()
    {
        if (SelectedScript is null || lineEditor is null || scriptLines.Length == 0 || !FlushPendingEdits())
        {
            return;
        }

        StopReview();
        Reading?.Dispose();
        var start = SelectedLine is { } selected ? Array.IndexOf(scriptLines, selected) : 0;
        Reading = new ReadSceneViewModel(this, [.. scriptLines], Math.Max(0, start));
    }

    /// <summary>
    /// Fecha o leitor e volta à grade. Se a pessoa marcou alguma fala como "soa estranho" nesta leitura, a grade abre com o filtro dessas falas;
    /// sem marca, ela volta como estava.
    /// </summary>
    [RelayCommand]
    public void StopReading()
    {
        if (!EndReading())
        {
            return;
        }

        // A contagem do chip respeita o personagem escolhido: sem voltar a "Todos", uma fala marcada de outro personagem não apareceria.
        SelectedSpeaker = Speakers[0];
        UpdateCounts();
        if (chips.FirstOrDefault(c => c.Filter == LineStatusFilter.Odd) is { Count: > 0 } chip)
        {
            // Com o filtro já ligado, SelectFilter não refaz a grade, e as marcas novas ainda não estariam nela.
            SelectFilter(chip);
            Refresh();
        }
    }

    /// <summary>Fecha o leitor sem mexer no filtro (outra cena, outro projeto, linhas relidas). Devolve se havia marca desta leitura.</summary>
    internal bool EndReading()
    {
        if (Reading is not { } reading)
        {
            return false;
        }

        Reading = null;
        var marked = reading.HasMarkedNow;
        reading.Dispose();
        return marked;
    }

    /// <summary>
    /// Põe ou tira "soa estranho" na fala e leva a linha gravada para os dados, a grade e a contagem do filtro. Devolve a mensagem de falha, ou nulo.
    /// A gravação relê o arquivo da linha, então uma edição do painel gravada antes não se perde.
    /// </summary>
    internal string? SetOdd(LineViewModel line, bool odd)
    {
        if (data is null || lineEditor is null)
        {
            return "Não foi possível marcar a fala: o projeto não está aberto.";
        }

        TextUnit saved;
        try
        {
            saved = lineEditor.SetOdd(line.Entry.ScriptKey, line.Id, odd);
        }
        catch (Exception e) when (e is KeyNotFoundException or VNForgeException or IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível marcar a fala: {e.Message.TrimEnd('.', ' ')}.";
        }

        var updated = line.Entry with { Unit = saved };
        data.Replace(updated);
        savedUnits.Remove(saved.Id);
        var index = updated.Number - 1;
        if (updated.ScriptKey == SelectedScript?.Key && index < scriptLines.Length && ReferenceEquals(scriptLines[index], line))
        {
            line.Update(updated);
            entries[index] = updated;
        }

        UpdateCounts();
        return null;
    }

    /// <summary>A fala como o jogo a mostraria: a tradução (ou o original, se ainda não há) passada pelo mesmo pipeline do build, com o perfil do projeto ou o padrão.</summary>
    internal ReadingLine ReadingLineOf(LineViewModel line)
    {
        var unit = line.Entry.Unit;
        var showsSource = !unit.HasTranslation;
        var text = showsSource ? unit.Source : unit.Translation!;
        var preview = new TextboxPreviewViewModel(unit, CurrentProfile() ?? FormattingProfile.Default, text, CurrentSpeakerMarks(), CurrentSpeakerNaming());
        return new ReadingLine(preview.Layout, preview.Layout is null ? preview.Problem ?? "Não foi possível montar a caixa desta fala." : null, showsSource);
    }
}
