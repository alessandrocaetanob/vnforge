using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// Uma linha da grade. Só leitura e sem notificação de mudança: a grade pode ter dezenas de milhares delas, então os membros são getters baratos.
/// </summary>
public sealed class LineViewModel(LineEntry entry)
{
    public LineEntry Entry { get; } = entry;

    public int Number => Entry.Number;

    public string Id => Entry.Unit.Id;

    public TranslationStatus Status => Entry.Unit.Status;

    public string StatusLabel => ProjectLabels.Status(Status);

    public bool IsNarration => string.IsNullOrEmpty(Entry.Unit.Speaker);

    public string SpeakerLabel => IsNarration ? "Narração" : Entry.Unit.Speaker!;

    public string Source => Entry.Unit.Source;

    public bool HasTranslation => Entry.Unit.HasTranslation;

    public string TranslationLabel => Entry.Unit.HasTranslation ? Entry.Unit.Translation : "sem tradução";

    public bool HasWarning => Entry.Severity == QaSeverity.Warning;

    public bool HasError => Entry.Severity == QaSeverity.Error;

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
