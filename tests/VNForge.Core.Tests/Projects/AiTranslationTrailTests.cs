using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>A tradução que a IA escreveu fica guardada em <see cref="TextUnit.AiTranslation"/> quando a pessoa a edita (#473).</summary>
[Trait("Risco", "R5")]
public sealed class AiTranslationTrailTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(Agora);
    private readonly ProjectFolder _project;

    public AiTranslationTrailTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit DaIa(int n, string traducao) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, "健太", $"原文{n}", traducao, TranslationStatus.Machine)
        {
            Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private TextUnit Gravada(int n) => _project.LoadText("a").Single(u => u.Id == $"a:{n:D6}");

    private LineEditor Editor() => new(_project, _clock);

    [Fact]
    public void Json_do_campo_faz_ida_e_volta_e_o_projeto_regrava_igual()
    {
        var unidade = DaIa(1, "Ô, vem.") with { AiTranslation = "Ei, vem." };

        var json = TextUnitJson.Serialize(unidade);

        Assert.Contains("\"aiTranslation\":\"Ei, vem.\"", json, StringComparison.Ordinal);
        Assert.Equal("Ei, vem.", TextUnitJson.Deserialize(json).AiTranslation);
        _project.SaveText("a", [unidade]);
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
        Assert.Equal(TextUnitJson.SerializeList([unidade]), TextUnitJson.SerializeList(_project.LoadText("a")));
    }
}
