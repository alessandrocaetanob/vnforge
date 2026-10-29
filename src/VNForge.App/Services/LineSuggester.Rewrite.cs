using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>As opções de uma reescrita de fala.</summary>
/// <param name="Options">De uma a duas versões da tradução, com os marcadores do original e diferentes da tradução de hoje.</param>
/// <param name="FromFakeProvider">Veio do provedor falso (app sem chave de API), não do Claude.</param>
/// <param name="Model">O id do modelo que respondeu; nulo no provedor falso.</param>
/// <param name="Effort">O esforço pedido ao modelo, em português; nulo no provedor falso.</param>
public sealed record LineRewrite(IReadOnlyList<string> Options, bool FromFakeProvider, string? Model = null, string? Effort = null);

/// <summary>Pede à IA uma reescrita dirigida da tradução de uma fala. Atrás de uma interface para os painéis rodarem nos testes sem rede.</summary>
public interface ILineRewriter
{
    /// <summary>Duas versões da tradução <paramref name="current"/> na direção pedida.</summary>
    /// <param name="batch">Exatamente a linha a reescrever, com o contexto que vale para ela: o falante com o "Como fala", o glossário, o estilo do projeto e as falas vizinhas.</param>
    /// <param name="box">A caixa do jogo; nula sem perfil de formatação ou com a quebra desligada.</param>
    /// <exception cref="LineSuggestionException">Sem chave de API (só em <see cref="RewriteDirection.Shorter"/>), recusa do modelo ou resposta sem uma versão aproveitável.</exception>
    Task<LineRewrite> RewriteAsync(TranslationBatch batch, RewriteDirection direction, string current, WrapRule? box, CancellationToken ct);
}

public sealed partial class LineSuggester : ILineRewriter
{
    /// <summary>
    /// Pede as duas versões pelo mesmo provedor da sugestão. Sem chave de API usa o <see cref="FakeTranslationProvider"/>, como a sugestão, menos em
    /// <see cref="RewriteDirection.Shorter"/>: a versão de teste não encurta nada, então a mensagem manda configurar a chave. As versões que perdem ou
    /// trocam um marcador do original, repetem a tradução de hoje ou repetem uma à outra saem da lista.
    /// </summary>
    public async Task<LineRewrite> RewriteAsync(TranslationBatch batch, RewriteDirection direction, string current, WrapRule? box, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(current);
        if (batch.Lines.Count != 1)
        {
            throw new ArgumentException("A reescrita trabalha com uma linha por vez.", nameof(batch));
        }

        var real = await Task.Run(openProvider, ct).ConfigureAwait(false);
        if (real is null && direction == RewriteDirection.Shorter)
        {
            throw new LineSuggestionException("Encurtar com a IA precisa da chave do Claude. Informe a chave em Ajustes.");
        }

        var rewriter = ChooseRewriter(real);
        var result = await rewriter.RewriteAsync(batch, RequestFor(direction, current, box), ct).ConfigureAwait(false);
        if (result.Refused)
        {
            throw new LineSuggestionException("A IA recusou reescrever esta fala. Edite o texto à mão.");
        }

        var options = KeepUsable(batch, result, current);
        if (options.Count == 0)
        {
            throw new LineSuggestionException("A IA não devolveu uma versão aproveitável. Tente de novo ou edite o texto à mão.");
        }

        return real is null
            ? new LineRewrite(options, FromFakeProvider: true)
            : new LineRewrite(options, FromFakeProvider: false, ModelName(), Effort: "baixo");
    }

    /// <summary>
    /// A versão mais curta da tradução, pelo mesmo caminho do chip "Mais curta": das duas opções da IA, a primeira que cabe na caixa; se nenhuma cabe,
    /// a primeira, e quem chama (o aviso "Não cabe na caixa do jogo") confere e avisa.
    /// </summary>
    public async Task<string> ShortenAsync(TextUnit unit, string current, WrapRule box, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(box);

        var rewrite = await RewriteAsync(new TranslationBatch([unit]), RewriteDirection.Shorter, current, box, ct).ConfigureAwait(false);
        var profile = FormattingProfile.Default with { Wrap = box };
        return rewrite.Options.FirstOrDefault(option => Fits(profile, unit, option)) ?? rewrite.Options[0];
    }

    private static IRewriteProvider ChooseRewriter(ITranslationProvider? real)
    {
        if (real is null)
        {
            return new FakeTranslationProvider();
        }

        return real as IRewriteProvider ?? throw new LineSuggestionException("O provedor de IA atual não sabe reescrever falas.");
    }

    /// <summary>A caixa só vai ao prompt quando a quebra está ligada e há limite de linhas: sem isso o modelo encurtaria à toa.</summary>
    private static RewriteRequest RequestFor(RewriteDirection direction, string current, WrapRule? box) =>
        box is { Enabled: true, MaxLines: { } maxLines }
            ? new RewriteRequest(direction, current, maxLines, box.MaxWidth)
            : new RewriteRequest(direction, current);

    private string? ModelName() => currentModel?.Invoke() is { Length: > 0 } model ? model : null;

    private static bool Fits(FormattingProfile profile, TextUnit unit, string text)
    {
        try
        {
            return LinePreviewer.Create(profile, unit, text).Boxes.Select(box => box.Count).DefaultIfEmpty().Max() <= (profile.Wrap.MaxLines ?? int.MaxValue);
        }
        catch (VNForgeException)
        {
            return false;
        }
    }

    private static List<string> KeepUsable(TranslationBatch batch, RewriteResult result, string current)
    {
        var kept = new List<string>();
        foreach (var option in result.Options)
        {
            var marked = TranslationResponseValidator.Validate(batch, [new TranslationResult(result.Id, option)]);
            if (marked.Valid.Count == 1 && option != current && !kept.Contains(option))
            {
                kept.Add(option);
            }
        }

        return kept;
    }
}
