using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma linha do projeto como o build a gravaria, com a contagem de linhas e de colunas por caixa.</summary>
/// <param name="Id">Id da linha.</param>
/// <param name="Source">Texto original.</param>
/// <param name="Translation">Tradução salva em <c>text/*.json</c>, antes da formatação.</param>
/// <param name="Text">Tradução depois do pipeline de formatação (igual à <paramref name="Translation"/> se o papel da linha não é formatado).</param>
/// <param name="Formatted">Falso quando o papel da linha não está em <see cref="FormattingProfile.Kinds"/>: o build grava a tradução como está.</param>
/// <param name="Columns">Colunas de cada linha, por caixa, medidas com o <c>wrap.charWidth</c> do perfil.</param>
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
    IReadOnlyList<IReadOnlyList<int>> Columns,
    int MaxWidth,
    int? MaxLines,
    bool WrapEnabled,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Total de linhas de todas as caixas.</summary>
    public int LineCount => Columns.Sum(box => box.Count);
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

        var profile = project.LoadFormatting();
        var translation = unit.Translation!;
        var formatted = profile.Kinds.Contains(unit.Kind);
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
                LineMeasure.Columns(result.Text, width),
                profile.Wrap.MaxWidth,
                profile.Wrap.MaxLines,
                profile.Wrap.Enabled,
                result.Warnings);
        }
        catch (Exception ex) when (ex is ArgumentException or FormattingException)
        {
            throw new VNForgeException($"Não foi possível formatar a linha {id} com o {ProjectFolder.FormattingFileName}: {ex.Message}", ex);
        }
    }
}
