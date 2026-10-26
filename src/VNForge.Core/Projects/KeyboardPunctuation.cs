using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// A opção do projeto "Trocar pontuação japonesa pela do teclado" (<see cref="StyleConfig.KeyboardPunctuation"/>) pronta para uso: troca a pontuação
/// japonesa (<c>。 ～ （</c>) pela do teclado no texto que a IA devolve e no que a pessoa grava, só quando a estratégia do projeto grava o substituto.
/// </summary>
public sealed class KeyboardPunctuation
{
    private readonly ITextEncoder encoder;

    private KeyboardPunctuation(ITextEncoder encoder) => this.encoder = encoder;

    /// <summary>A troca do projeto, ou nulo quando a opção está desligada.</summary>
    /// <exception cref="ProjectConfigException">O <c>vnforge.json</c> é inválido.</exception>
    public static KeyboardPunctuation? For(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return For(project.LoadConfig());
    }

    /// <summary>A troca descrita por <paramref name="config"/>, ou nulo quando a opção está desligada.</summary>
    /// <exception cref="VNForgeException">A estratégia de codificação é desconhecida ou ainda não tem encoder; o projeto também não abre nem compila nesse estado.</exception>
    public static KeyboardPunctuation? For(ProjectConfig config, TextEncoderRegistry? encoders = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.Style.KeyboardPunctuation)
        {
            return null;
        }

        return new KeyboardPunctuation((encoders ?? TextEncoderRegistry.Default).Create(config.Encoding.Strategy));
    }

    /// <summary>O <paramref name="text"/> com a pontuação japonesa trocada pela do teclado.</summary>
    public string Apply(string text) => JapanesePunctuation.ToKeyboard(text, encoder);
}
