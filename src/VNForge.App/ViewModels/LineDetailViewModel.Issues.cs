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

    /// <summary>O texto da medida quando o build não formata a linha: o perfil não se aplica a ela.</summary>
    public const string NotFormattedText = "O build grava esta linha como está";

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

    /// <summary>
    /// O que dizer no lugar de "cabe" quando a medida do perfil não vale para a linha: o build não a formata (papel fora de <c>kinds</c> ou tradução igual
    /// ao original) ou a quebra está desligada. Nulo quando a medida vale, quando passa do limite (isso é "Passa") ou sem tradução.
    /// </summary>
    private string? NeutralFitText
    {
        get
        {
            if (Preview?.Layout is null || string.IsNullOrWhiteSpace(Translation) || OverflowLines > 0)
            {
                return null;
            }

            if (Preview.Notice is not null)
            {
                return NotFormattedText;
            }

            return WrapIsOff ? WrapOffText : null;
        }
    }

    /// <summary>A medida diz que o perfil não vale para a linha: vai em tom neutro, sem o verde de "cabe" nem o vermelho de "passa".</summary>
    public bool FitIsNeutral => NeutralFitText is not null;

    /// <summary>A medida da caixa tem algo a dizer em verde ou vermelho (cabe, passa ou sem tradução).</summary>
    public bool FitIsShown => FitText.Length > 0 && !FitIsNeutral;

    /// <summary>
    /// Troca os avisos mostrados pelos da linha como estão agora; erros primeiro, depois avisos, na ordem do QA. A caixa acima do limite fica de fora: o "Passa 1 linha"
    /// e o aviso "Não cabe na caixa do jogo" já a explicam em linguagem comum, e "caixa 1 com 4 linhas (máximo 3)." só repetiria. A grade e a Conferência a seguem mostrando.
    /// </summary>
    public void ShowIssues(IReadOnlyList<QaIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        QaIssues = LineIssueViewModel.From(issues.Where(i => i.RuleId != BoxLineLimitRule.RuleId));
    }
}
