using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Tudo que uma regra pode consultar: as traduções do projeto inteiro (para regras entre linhas, como a inconsistência), o glossário,
/// os personagens, o perfil de formatação e o encoder da estratégia atual. Só leitura.
/// </summary>
/// <param name="Texts">Unidades por chave de script, como <see cref="ProjectFolder.LoadAllText"/> devolve.</param>
/// <param name="Glossary">Termos de <c>glossary.json</c>.</param>
/// <param name="Characters">Personagens de <c>characters.json</c>.</param>
/// <param name="Formatting">Perfil de <c>formatting.json</c>.</param>
/// <param name="Encoder">Encoder da <c>encoding.strategy</c>; nulo quando o contexto não vem de um projeto (testes de regra).</param>
public sealed record QaContext(
    IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts,
    IReadOnlyList<GlossaryTerm> Glossary,
    IReadOnlyList<CharacterEntry> Characters,
    FormattingProfile Formatting,
    ITextEncoder? Encoder = null)
{
    /// <summary>Escolhas de estilo do <c>vnforge.json</c>, que as regras de honorífico consultam; o padrão quando o contexto não vem de um projeto.</summary>
    public StyleConfig Style { get; init; } = StyleConfig.Default;

    /// <summary>Tratamentos entre personagens de <c>address-terms.json</c>, que a <see cref="AddressTermRule"/> confere; vazio quando o projeto não os tem ou o contexto não vem de um projeto.</summary>
    public IReadOnlyList<AddressTerm> AddressTerms { get; init; } = [];

    /// <summary>Título do jogo do <c>vnforge.json</c>, que a <see cref="GameTitleEncodingRule"/> confere; nulo quando o projeto não tem título ou o contexto não vem de um projeto.</summary>
    public GameTitleConfig? GameTitle { get; init; }

    /// <summary>Contexto só com textos, para regras que não precisam do resto.</summary>
    public static QaContext ForTexts(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts) =>
        new(texts, [], [], FormattingProfile.Default);

    /// <summary>Contexto só com o encoder do projeto, o que as correções do <see cref="QaFixer"/> consultam; os textos e o resto ficam vazios.</summary>
    /// <exception cref="VNForgeException">Arquivo do projeto inválido ou estratégia de codificação desconhecida.</exception>
    public static QaContext ForFixing(ProjectFolder project, TextEncoderRegistry? encoders = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var config = project.LoadConfig();
        var encoder = (encoders ?? TextEncoderRegistry.Default).Create(config.Encoding.Strategy, config.Style.SpeakerDelimiter);
        return new QaContext(new Dictionary<string, IReadOnlyList<TextUnit>>(), [], [], FormattingProfile.Default, encoder);
    }

    /// <summary>Lê do <paramref name="project"/> os textos, o glossário, os personagens, o perfil e o encoder.</summary>
    /// <exception cref="VNForgeException">Arquivo do projeto inválido ou estratégia de codificação desconhecida.</exception>
    public static QaContext FromProject(ProjectFolder project, TextEncoderRegistry? encoders = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var config = project.LoadConfig();
        var encoder = (encoders ?? TextEncoderRegistry.Default).Create(config.Encoding.Strategy, config.Style.SpeakerDelimiter);
        return new QaContext(project.LoadAllText(), project.LoadGlossary(), project.LoadCharacters(), project.LoadFormatting(), encoder) { Style = config.Style, GameTitle = config.Game.Title, AddressTerms = project.LoadAddressTerms() };
    }
}
