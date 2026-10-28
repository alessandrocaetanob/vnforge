using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>
/// O que decide o nome da placa da caixa do jogo (#464): os personagens do Glossário e a codificação do projeto, as mesmas entradas que o build dá a
/// <see cref="SpeakerText"/> antes de gravar o nome na caixa de nome. Sem eles, a placa mostra o nome do jogo.
/// </summary>
public sealed class SpeakerNaming(CharacterBook characters, ITextEncoder? encoder)
{
    /// <summary>Sem cadastro nem codificação: a placa mostra o falante como o texto o guarda.</summary>
    public static SpeakerNaming None { get; } = new(new CharacterBook([]), null);

    public CharacterBook Characters { get; } = characters ?? throw new ArgumentNullException(nameof(characters));

    /// <summary>A codificação do projeto; nula quando o <c>vnforge.json</c> não abre ou a estratégia é desconhecida.</summary>
    public ITextEncoder? Encoder { get; } = encoder;

    /// <summary>O nome que o build grava na caixa de nome da <paramref name="unit"/>; nulo na narração (<see cref="SpeakerText.WrittenName"/>).</summary>
    public string? NameOf(TextUnit unit) => SpeakerText.WrittenName(unit, Characters, Encoder);

    /// <summary>Lê os personagens e a codificação da pasta; o que não abre fica de fora, sem derrubar a tela.</summary>
    public static SpeakerNaming Read(ProjectFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        IReadOnlyList<CharacterEntry> entries;
        try
        {
            entries = folder.LoadCharacters();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            entries = [];
        }

        return new SpeakerNaming(new CharacterBook(entries), ReadEncoder(folder));
    }

    /// <summary>A codificação do projeto como o build a cria; nula sem <c>vnforge.json</c> legível ou com estratégia desconhecida.</summary>
    public static ITextEncoder? ReadEncoder(ProjectFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        try
        {
            var config = folder.LoadConfig();
            return TextEncoderRegistry.Default.Create(config.Encoding.Strategy, config.Style.SpeakerDelimiter);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
