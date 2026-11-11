using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>#453: a prévia da caixa de texto mede a abertura da fala como ela aparece no jogo.</summary>
public sealed class LinePreviewSpeakerMarksTests : IDisposable
{
    private const string Id = "a001:000001";

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TextUnit Spoken(string? speaker = "Misaki") =>
        new(Id, TextUnitKind.Dialogue, speaker, "原文", "um dois", TranslationStatus.Translated);

    private ProjectFolder Project(string strategy, SpeakerDelimiterStyle style, params TextUnit[] units)
    {
        var folder = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto-" + Guid.NewGuid().ToString("N")),
            new ProjectConfig("Teste", new GameConfig("D:/Jogos/Teste", "Jogo.exe"), new EngineConfig("yuris", 500, "0xD36FAC96"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig(strategy), AiConfig.Default)
            {
                Style = StyleConfig.Default with { SpeakerDelimiter = style },
            });
        folder.SaveText("a001", units);
        return folder;
    }

    [Fact]
    public void EstrategiaSemEncoderNaoSomaNada()
    {
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For("remap", SpeakerDelimiterStyle.None));
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For("desconhecida", SpeakerDelimiterStyle.Guillemets));
    }
}
