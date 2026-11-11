using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>A tradução que a IA escreveu fica guardada em <see cref="TextUnit.AiTranslation"/> quando a pessoa a edita (#473).</summary>
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

    [Fact]
    public void SetTranslation_que_devolve_o_texto_da_IA_ou_limpa_a_linha_apaga_o_campo()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem."), DaIa(2, "Oi.")]);
        Editor().SetTranslation("a", "a:000001", "Ô, vem.");
        Editor().SetTranslation("a", "a:000002", "Alô.");

        Editor().SetTranslation("a", "a:000001", "Ei, vem.");
        Editor().SetTranslation("a", "a:000002", "  ");

        Assert.Null(Gravada(1).AiTranslation);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void Restore_devolve_o_campo_de_antes()
    {
        var antes = DaIa(1, "Ei, vem.");
        _project.SaveText("a", [antes]);
        var gravada = Editor().SetTranslation("a", antes.Id, "Ô, vem.");

        Editor().Restore("a", antes, gravada);

        Assert.Null(Gravada(1).AiTranslation);
        Assert.Equal("Ei, vem.", Gravada(1).Translation);
    }

    [Fact]
    public void Importar_revisao_guarda_o_texto_da_IA_so_quando_a_traducao_muda()
    {
        _project.SaveText("a", [DaIa(1, "Ei vem."), DaIa(2, "Ei anda.")]);
        var csv = Path.Combine(_temp.Path, "revisao.csv");
        ReviewExporter.Export(_project, ReviewFormats.Csv, csv);
        File.WriteAllText(csv, File.ReadAllText(csv).Replace("Ei vem.", "Ô vem.", StringComparison.Ordinal).Replace("Ei anda.,machine,", "Ei anda.,approved,", StringComparison.Ordinal));

        ReviewImporter.Import(_project, csv);

        Assert.Equal("Ô vem.", Gravada(1).Translation);
        Assert.Equal("Ei vem.", Gravada(1).AiTranslation);
        Assert.Equal(TranslationStatus.Approved, Gravada(2).Status);
        Assert.Null(Gravada(2).AiTranslation);
    }
}
