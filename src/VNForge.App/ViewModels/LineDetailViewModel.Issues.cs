using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>Uma ocorrência do QA na linha aberta: a regra pelo título da tela Conferir e a mensagem dela.</summary>
public sealed record LineIssueViewModel(string Title, string Message, bool IsError)
{
    /// <summary>Uma linha por ocorrência, erros antes de avisos, na ordem do QA dentro de cada gravidade.</summary>
    public static IReadOnlyList<LineIssueViewModel> From(IEnumerable<QaIssue> issues) =>
    [
        .. issues
            .OrderByDescending(i => i.Severity)
            .Select(i => new LineIssueViewModel(ConferenceRuleTexts.Title(i.RuleId), i.Message, i.Severity == QaSeverity.Error)),
    ];

    /// <summary>"Título: mensagem", como a dica do ícone da grade mostra.</summary>
    public string Tip => $"{Title}: {Message}";
}

/// <summary>Os avisos do QA da linha aberta e o estado da quebra de linha do perfil.</summary>
public sealed partial class LineDetailViewModel
{
    /// <summary>O texto que a medida da caixa mostra quando o perfil não quebra linhas.</summary>
    public const string WrapOffText = "Quebra desligada no perfil";

    /// <summary>
    /// As ocorrências do QA desta linha, as mesmas que dão o ícone na grade. O Editor as entrega ao abrir o painel e de novo a cada gravação
    /// (<see cref="ShowIssues"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQaIssues))]
    public partial IReadOnlyList<LineIssueViewModel> QaIssues { get; private set; } = [];

    public bool HasQaIssues => QaIssues.Count > 0;

    /// <summary>A prévia existe e o perfil não quebra linhas (<c>wrap.enabled</c> falso): as colunas e o limite de linhas do perfil não são aplicados.</summary>
    public bool WrapIsOff => Preview is { } preview && !preview.Profile.Wrap.Enabled;

    /// <summary>A medida diz só que a quebra está desligada: vai em tom neutro, sem o verde de "cabe" nem o vermelho de "passa".</summary>
    public bool FitIsNeutral => WrapIsOff && !DoesNotFit && Preview?.Layout is not null && !string.IsNullOrWhiteSpace(Translation);

    /// <summary>A medida da caixa tem algo a dizer em verde ou vermelho (cabe, passa ou sem tradução).</summary>
    public bool FitIsShown => FitText.Length > 0 && !FitIsNeutral;

    /// <summary>Troca os avisos mostrados pelos da linha como estão agora; erros primeiro, depois avisos, na ordem do QA.</summary>
    public void ShowIssues(IReadOnlyList<QaIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        QaIssues = LineIssueViewModel.From(issues);
    }
}
