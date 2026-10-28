using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um item da lista de scripts do Editor: a chave, o nome do capítulo, o percentual concluído e a barra de distribuição.</summary>
/// <param name="progress">O progresso do script.</param>
/// <param name="label">O nome legível do script; nulo mostra só a chave.</param>
public sealed partial class ScriptItemViewModel(ScriptProgress progress, string? label = null) : ObservableObject
{
    /// <summary>O retrato do progresso; <see cref="Update"/> o troca quando as linhas do script mudam sem o projeto ser relido.</summary>
    public ScriptProgress Progress { get; private set; } = progress;

    public string Key => Progress.Key;

    /// <summary>O nome da cena ("Prólogo"); nulo enquanto o projeto não o guarda.</summary>
    public string? Label { get; private set; } = string.IsNullOrWhiteSpace(label) ? null : label;

    /// <summary>O nome da cena, ou a chave quando ele não existe. Só para quem precisa da chave como último recurso; as telas do Editor usam <see cref="Name"/>.</summary>
    public string Display => Label ?? Key;

    /// <summary>A posição da cena no capítulo (no jogo, enquanto a lista não foi agrupada), a partir de 1; é o "cena 13 de 45" da sobrancelha e muda quando a cena troca de capítulo.</summary>
    [ObservableProperty]
    public partial int Position { get; set; } = 1;

    /// <summary>A posição da cena entre todas as cenas do projeto, em ordem de chave, a partir de 1; dá o "Cena 13" das cenas sem nome e não muda quando a cena troca de capítulo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    public partial int Number { get; set; } = 1;

    /// <summary>O que as telas do Editor mostram como nome: o da cena ou "Cena N" (N é o <see cref="Number"/>), nunca a chave.</summary>
    public string Name => Label ?? $"Cena {Number}";

    /// <summary>Troca o nome da cena quando o <c>scripts.json</c> é relido.</summary>
    public void SetLabel(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value;
        if (clean == Label)
        {
            return;
        }

        Label = clean;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Tooltip));
    }

    public int CompletedPercent => Progress.CompletedPercent;

    public string PercentLabel => $"{CompletedPercent}%";

    public bool IsComplete => CompletedPercent == 100;

    /// <summary>A cena não tem linha nenhuma: conta como pronta e a lista a esconde enquanto "esconder cenas vazias" está ligado.</summary>
    public bool IsEmpty => Progress.IsEmpty;

    /// <summary>Há uma tarefa da IA traduzindo este script: o item mostra o ícone girando.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>O item passa pelo filtro da lista de scripts; a lista esconde os que não passam sem tirá-los dela.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    /// <summary>
    /// O item aparece na lista: passa pelo filtro e o capítulo dele está aberto (o primeiro item do capítulo aparece sempre, porque leva o cabeçalho).
    /// <see cref="IsShown"/> continua dizendo só do filtro, para fechar um capítulo não tirar as cenas dele de "Traduzir com IA".
    /// </summary>
    [ObservableProperty]
    public partial bool IsListed { get; set; } = true;

    /// <summary>O item é o primeiro do capítulo na lista e desenha o cabeçalho dele acima de si.</summary>
    [ObservableProperty]
    public partial bool IsGroupStart { get; set; }

    /// <summary>O capítulo a que a cena pertence na lista; nulo antes de a lista ser agrupada.</summary>
    [ObservableProperty]
    public partial ChapterGroupViewModel? Chapter { get; set; }

    public StatusDistribution Distribution { get; private set; } = StatusDistribution.Of(progress.Counts);

    /// <summary>A dica do item, em duas linhas: o nome por inteiro (a lista o corta em uma linha) e o id (a chave do script, que a lista não desenha) com o percentual de IA; o de "concluído" conta Revisadas e Aprovadas.</summary>
    public string Tooltip => $"{Name}\n{Key} · IA {Progress.MachinePercent}%";

    public string LinesLabel => ProjectLabels.Number(Progress.Counts.Total);

    /// <summary>Descrição da barra de distribuição para leitor de tela.</summary>
    public string DistributionDescription =>
        $"Aprovadas {Distribution.Approved}%, revisadas {Distribution.Reviewed}%, traduzidas {Distribution.Translated}%, IA {Distribution.Machine}%, pendentes {Distribution.Pending}%";

    /// <summary>
    /// Troca as contagens do script (uma linha gravada, aprovada ou aplicada por "Usar em todos") e avisa a lista e o rodapé, que não esperam
    /// a próxima leitura do projeto. Contagens iguais às de agora não notificam.
    /// </summary>
    public void Update(StatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (Progress.Counts == counts)
        {
            return;
        }

        Progress = Progress with { Counts = counts };
        Distribution = StatusDistribution.Of(counts);
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(CompletedPercent));
        OnPropertyChanged(nameof(PercentLabel));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Distribution));
        OnPropertyChanged(nameof(DistributionDescription));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(LinesLabel));
    }

    /// <summary>Casa o texto do filtro da lista com a chave ou o nome, sem diferenciar maiúsculas.</summary>
    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Key.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)
        || Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
