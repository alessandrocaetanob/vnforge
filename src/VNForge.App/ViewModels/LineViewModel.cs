using System.ComponentModel;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// Uma linha da grade. A grade pode ter dezenas de milhares delas, então os membros são getters baratos e a única notificação é a de <see cref="Update"/>,
/// que o Editor chama quando o painel de detalhe grava a linha.
/// </summary>
/// <param name="entry">A linha da grade.</param>
/// <param name="speakerTarget">O nome do falante em português (<c>characters.json</c>); nulo mostra o nome original.</param>
public sealed class LineViewModel(LineEntry entry, string? speakerTarget = null) : INotifyPropertyChanged
{
    private static readonly string[] Changeable =
    [
        nameof(Entry), nameof(Status), nameof(StatusLabel), nameof(HasTranslation), nameof(TranslationLabel), nameof(TranslationPlain),
        nameof(HasWarning), nameof(HasError), nameof(Issue), nameof(IssueLabel), nameof(IssueTip),
        nameof(IsNarration), nameof(SpeakerLabel), nameof(SpeakerName), nameof(IsSpeakerChanged), nameof(SpeakerChangedTip),
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    public LineEntry Entry { get; private set; } = entry;

    /// <summary>Troca a linha pela versão gravada (tradução, status e ocorrências do QA) e avisa a grade.</summary>
    public void Update(LineEntry updated)
    {
        Entry = updated;
        foreach (var name in Changeable)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Como <see cref="Update(LineEntry)"/>, e troca o nome em português do falante (a gravação mudou o falante escolhido da linha).</summary>
    public void Update(LineEntry updated, string? newSpeakerTarget)
    {
        speakerTarget = newSpeakerTarget;
        Update(updated);
    }

    public int Number => Entry.Number;

    public string Id => Entry.Unit.Id;

    public TranslationStatus Status => Entry.Unit.Status;

    public string StatusLabel => ProjectLabels.Status(Status);

    public bool IsNarration => string.IsNullOrEmpty(Entry.Unit.EffectiveSpeaker);

    public string SpeakerLabel => IsNarration ? "Narração" : SpeakerName!;

    /// <summary>O falante como a grade o mostra: o nome em português quando o personagem está cadastrado, o original quando não; nulo na narração.</summary>
    public string? SpeakerName => IsNarration ? null : speakerTarget ?? Entry.Unit.EffectiveSpeaker;

    /// <summary>A linha tem o falante trocado por quem traduz (<see cref="TextUnit.ChosenSpeaker"/>): a grade a marca.</summary>
    public bool IsSpeakerChanged => Entry.Unit.ChosenSpeaker is not null;

    /// <summary>Dica da marca de falante trocado: o que o jogo diz nessa linha; nula quando o falante é o do jogo.</summary>
    public string? SpeakerChangedTip => IsSpeakerChanged
        ? $"Falante trocado nesta linha. No jogo: {(string.IsNullOrEmpty(Entry.Unit.Speaker) ? "Narração" : Entry.Unit.Speaker)}."
        : null;

    public string Source => Entry.Unit.Source;

    /// <summary>O original sem marcadores de pausa, para a coluna cortada com reticências.</summary>
    public string SourcePlain { get; } = LineText.Plain(entry.Unit.Source);

    /// <summary>A tradução sem marcadores de pausa; vazia sem tradução.</summary>
    public string TranslationPlain => LineText.Plain(Entry.Unit.Translation);

    public bool HasTranslation => Entry.Unit.HasTranslation;

    public string TranslationLabel => Entry.Unit.HasTranslation ? Entry.Unit.Translation : Status == TranslationStatus.Keep ? LineRow.KeepLabel : "sem tradução";

    public bool HasWarning => Entry.Severity == QaSeverity.Warning;

    public bool HasError => Entry.Severity == QaSeverity.Error;

    public LineIssue Issue => HasError ? LineIssue.Error : HasWarning ? LineIssue.Warning : LineIssue.None;

    /// <summary>Dica do ícone do QA: uma linha por ocorrência, com o nome da regra e a mensagem; vazia quando a linha não tem ocorrência.</summary>
    public string IssueTip => Entry.Issues.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, LineIssueViewModel.From(Entry.Issues).Select(i => i.Tip));

    /// <summary>Texto do indicador do QA para leitor de tela; vazio quando a linha não tem ocorrência.</summary>
    public string IssueLabel
    {
        get
        {
            var errors = Entry.Issues.Count(i => i.Severity == QaSeverity.Error);
            var warnings = Entry.Issues.Count - errors;
            var parts = new List<string>(2);
            if (errors > 0)
            {
                parts.Add(errors == 1 ? "1 erro" : $"{errors} erros");
            }

            if (warnings > 0)
            {
                parts.Add(warnings == 1 ? "1 aviso" : $"{warnings} avisos");
            }

            return parts.Count == 0 ? string.Empty : $"{string.Join(" e ", parts)} do QA";
        }
    }
}
