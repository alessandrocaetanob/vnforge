using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma linha do projeto como o build a gravaria, com a contagem de linhas e de colunas por caixa.</summary>
/// <param name="Id">Id da linha.</param>
/// <param name="Source">Texto original.</param>
/// <param name="Translation">Tradução salva em <c>text/*.json</c>, antes da formatação.</param>
/// <param name="Text">Tradução depois do pipeline de formatação (igual à <paramref name="Translation"/> se o papel da linha não é formatado).</param>
/// <param name="Formatted">Falso quando o build não formata a linha (papel fora de <see cref="FormattingProfile.Kinds"/> ou tradução igual ao original): ela é gravada como está.</param>
/// <param name="Boxes">Linhas de cada caixa, com o texto e as colunas de cada uma, medidas com o <c>wrap.charWidth</c> do perfil.</param>
/// <param name="MaxWidth">Largura máxima do perfil (<c>wrap.maxWidth</c>); só vale com a quebra ligada.</param>
/// <param name="MaxLines">Limite de linhas por caixa (<c>wrap.maxLines</c>); nulo sem limite.</param>
/// <param name="WrapEnabled">Se a quebra de linha está ligada no perfil.</param>
/// <param name="Warnings">Avisos das etapas do pipeline, como caixa acima do limite de linhas.</param>
public sealed record LinePreview(
    string Id,
    string Source,
    string Translation,
    string Text,
    bool Formatted,
    IReadOnlyList<IReadOnlyList<MeasuredLine>> Boxes,
    int MaxWidth,
    int? MaxLines,
    bool WrapEnabled,
    IReadOnlyList<string> Warnings)
{
    /// <summary>O que <c>vnf preview</c> e o app dizem quando o build não formata a linha.</summary>
    public const string NotFormattedNotice =
        "O build não formata esta linha (papel fora de kinds do formatting.json ou tradução igual ao original); ela é gravada como está.";

    /// <summary>Nome do falante da linha; nulo sem falante. A caixa do jogo o mostra na placa.</summary>
    public string? Speaker { get; init; }

    /// <summary>Colunas de cada linha, por caixa.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Columns { get; } = [.. Boxes.Select(box => (IReadOnlyList<int>)[.. box.Select(line => line.Columns)])];

    /// <summary>Total de linhas de todas as caixas.</summary>
    public int LineCount { get; } = Boxes.Sum(box => box.Count);

    /// <summary><see cref="NotFormattedNotice"/> quando o build grava a linha como está; nulo quando ela é formatada.</summary>
    public string? Notice => Formatted ? null : NotFormattedNotice;
}

/// <summary><c>vnf preview</c>: aplica à tradução de uma linha o mesmo pipeline do build e mede o resultado. Só lê; não grava nada.</summary>
public static class LinePreviewer
{
    /// <exception cref="VNForgeException">Id inexistente, linha sem tradução ou texto que o pipeline recusa.</exception>
    public static LinePreview Create(ProjectFolder project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var unit = project.LoadAllText().Values.SelectMany(units => units).FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.Ordinal))
            ?? throw new VNForgeException($"Não existe linha com o id '{id}' nos text/*.json do projeto.");
        if (!unit.HasTranslation)
        {
            throw new VNForgeException($"A linha {id} ainda não tem tradução para mostrar.");
        }

        return Create(project.LoadFormatting(), unit, unit.Translation);
    }

    /// <summary>
    /// A mesma prévia para uma tradução que ainda não foi gravada (o texto em edição no Editor). Trata a linha como o build a trataria depois de gravada:
    /// com tradução, e formatada se o papel está em <see cref="FormattingProfile.Kinds"/> e o texto difere do original.
    /// </summary>
    /// <param name="profile">Perfil de formatação do projeto.</param>
    /// <param name="unit">A linha; só o id, o original e o papel são usados.</param>
    /// <param name="translation">Tradução a mostrar, no lugar da gravada em <paramref name="unit"/>.</param>
    /// <exception cref="VNForgeException">Texto que o pipeline recusa.</exception>
    public static LinePreview Create(FormattingProfile profile, TextUnit unit, string translation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(translation);

        var formatted = !string.IsNullOrEmpty(translation)
            && !string.Equals(translation, unit.Source, StringComparison.Ordinal)
            && profile.Kinds.Contains(unit.Kind);
        try
        {
            var result = formatted ? FormattingPipeline.Create(profile).Format(translation) : new FormattedText(translation, []);
            var width = profile.Wrap.ToOptions().Width;
            return new LinePreview(
                unit.Id,
                unit.Source,
                translation,
                result.Text,
                formatted,
                LineMeasure.Lines(result.Text, width),
                profile.Wrap.MaxWidth,
                profile.Wrap.MaxLines,
                profile.Wrap.Enabled,
                result.Warnings)
            {
                Speaker = string.IsNullOrEmpty(unit.Speaker) ? null : unit.Speaker,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormattingException)
        {
            throw new VNForgeException($"Não foi possível formatar a linha {unit.Id} com o {ProjectFolder.FormattingFileName}: {ex.Message}", ex);
        }
    }
}
