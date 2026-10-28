namespace VNForge.Core.Text;

/// <summary>
/// Escolhe o <see cref="ITextEncoder"/> pelo id da estratégia (<c>encoding.strategy</c> do <c>vnforge.json</c>). Cada estratégia entra com uma
/// linha em <see cref="Default"/>; id conhecido sem encoder pronto é recusado, nunca trocado por outra estratégia em silêncio.
/// </summary>
public sealed class TextEncoderRegistry
{
    /// <summary>Ids que o <c>vnforge.json</c> aceita, com ou sem encoder pronto (o contrato do doc 02).</summary>
    public static readonly IReadOnlyList<string> KnownIds = ["sjis", "fold", "remap", "cp1252"];

    private readonly Dictionary<string, Func<SpeakerDelimiterStyle, ITextEncoder>> _factories = new(StringComparer.Ordinal);

    /// <summary>Registro com as estratégias que esta versão do vnf implementa.</summary>
    public static TextEncoderRegistry Default { get; } = new TextEncoderRegistry()
        .Register("sjis", () => new ShiftJisTextEncoder())
        .Register("fold", () => new FoldTextEncoder())
        .Register("cp1252", speakerDelimiter => new Cp1252TextEncoder(speakerDelimiter));

    /// <summary>Ids com encoder pronto, na ordem em que entraram.</summary>
    public IReadOnlyCollection<string> AvailableIds => _factories.Keys;

    /// <summary>Acrescenta (ou troca) o encoder do <paramref name="id"/>.</summary>
    public TextEncoderRegistry Register(string id, Func<ITextEncoder> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Register(id, _ => factory());
    }

    /// <summary>Acrescenta (ou troca) o encoder do <paramref name="id"/>, que recebe o <see cref="SpeakerDelimiterStyle"/> do projeto (a <c>cp1252</c> o usa).</summary>
    public TextEncoderRegistry Register(string id, Func<SpeakerDelimiterStyle, ITextEncoder> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[id] = factory;
        return this;
    }

    /// <summary>Cria o encoder da estratégia <paramref name="id"/>.</summary>
    /// <param name="id">Id da estratégia.</param>
    /// <param name="speakerDelimiter">Estilo do delimitador da fala (<c>style.speakerDelimiter</c> do <c>vnforge.json</c>); só as estratégias que não gravam <c>「 」</c> o usam.</param>
    /// <exception cref="VNForgeException">Estratégia desconhecida, ou conhecida mas ainda sem encoder nesta versão. Mensagem em português.</exception>
    public ITextEncoder Create(string id, SpeakerDelimiterStyle speakerDelimiter = SpeakerDelimiterStyle.None)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_factories.TryGetValue(id, out var factory))
        {
            return factory(speakerDelimiter);
        }

        var available = string.Join(", ", _factories.Keys);
        if (KnownIds.Contains(id, StringComparer.Ordinal))
        {
            throw new VNForgeException(
                $"A estratégia de codificação '{id}' ainda não está disponível nesta versão do vnf; as disponíveis são: {available}. Troque o encoding.strategy do vnforge.json.");
        }

        throw new VNForgeException($"A estratégia de codificação '{id}' não é conhecida; use uma destas: {string.Join(", ", KnownIds)}.");
    }
}
