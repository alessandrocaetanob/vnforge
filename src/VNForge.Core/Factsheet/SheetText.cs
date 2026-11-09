using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>
/// Um texto da ficha com o original do VNDB (<see cref="Source"/>, em geral inglês) e a tradução, que passa pelas mesmas etapas de uma fala:
/// <see cref="TranslationStatus.Machine"/> (traduzido pela IA), <see cref="TranslationStatus.Translated"/> (escrito ou corrigido à mão),
/// <see cref="TranslationStatus.Reviewed"/> e <see cref="TranslationStatus.Approved"/>. Só estes e <see cref="TranslationStatus.Untranslated"/> valem aqui.
/// Quando o VNDB muda o original, a tradução fica e <see cref="Outdated"/> avisa (ver <see cref="WithSource"/>).
/// </summary>
public sealed record SheetText(string Source, string? Target = null, TranslationStatus Status = TranslationStatus.Untranslated, bool Outdated = false)
{
    /// <summary>Existe uma tradução para mostrar.</summary>
    public bool HasTarget => Target is not null && Status != TranslationStatus.Untranslated;

    /// <summary>A tradução quando existe, senão o original.</summary>
    public string Shown => HasTarget ? Target! : Source;

    /// <summary>Um texto novo, ainda sem tradução.</summary>
    public static SheetText Pending(string source) => new(source);

    /// <summary>A tradução da IA. O texto precisa ter os mesmos marcadores de spoiler do original.</summary>
    /// <exception cref="LibraryException">O texto está vazio ou perdeu ou ganhou um marcador de spoiler.</exception>
    public SheetText TranslatedByAi(string target) => With(target, TranslationStatus.Machine);

    /// <summary>Uma tradução escrita ou corrigida pela pessoa. Volta para <see cref="TranslationStatus.Translated"/>, que pede nova revisão.</summary>
    /// <exception cref="LibraryException">O texto está vazio ou perdeu ou ganhou um marcador de spoiler.</exception>
    public SheetText EditedByPerson(string target) => With(target, TranslationStatus.Translated);

    /// <summary>Revisada. O que já está revisado ou aprovado não muda.</summary>
    /// <exception cref="LibraryException">Ainda não há tradução para revisar.</exception>
    public SheetText Reviewed()
    {
        RequireTarget("revisar");
        return Status >= TranslationStatus.Reviewed ? this : this with { Status = TranslationStatus.Reviewed, Outdated = false };
    }

    /// <summary>Aprovada. Só o que foi revisado se aprova.</summary>
    /// <exception cref="LibraryException">A tradução ainda não foi revisada.</exception>
    public SheetText Approved()
    {
        RequireTarget("aprovar");
        return Status switch
        {
            TranslationStatus.Approved => this,
            TranslationStatus.Reviewed => this with { Status = TranslationStatus.Approved },
            _ => throw new LibraryException("Só se aprova o que já foi revisado. Revise antes."),
        };
    }

    /// <summary>
    /// O original que o VNDB tem agora. Igual ao guardado, nada muda. Diferente, a tradução da IA que ninguém tocou é descartada (volta a pendente);
    /// a que a pessoa escreveu, revisou ou aprovou fica, vira <see cref="TranslationStatus.Translated"/> e é marcada como <see cref="Outdated"/>.
    /// </summary>
    public SheetText WithSource(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        if (string.Equals(source, Source, StringComparison.Ordinal))
        {
            return this;
        }

        return Status is TranslationStatus.Untranslated or TranslationStatus.Machine || Target is null
            ? Pending(source)
            : new SheetText(source, Target, TranslationStatus.Translated, Outdated: true);
    }

    private SheetText With(string target, TranslationStatus status)
    {
        var text = target?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new LibraryException("A tradução não pode ficar vazia.");
        }

        if (SheetMarkup.SpoilerMarkers(text) != SheetMarkup.SpoilerMarkers(Source))
        {
            throw new LibraryException("A tradução precisa manter os mesmos trechos [spoiler]...[/spoiler] do original.");
        }

        return this with { Target = text, Status = status, Outdated = false };
    }

    private void RequireTarget(string verb)
    {
        if (!HasTarget)
        {
            throw new LibraryException($"Não há tradução para {verb}.");
        }
    }
}
