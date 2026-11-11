using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>#372: gravar uma linha ou mudar o glossário, os personagens e o estilo refaz os avisos sem reler o projeto.</summary>
public class ProjectLinesRecheckTests
{
    private const string Script = "story04";

    private static int writes;

    private static ProjectFolder Projeto(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "VN",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText(Script, linhas);
        return projeto;
    }

    private static TextUnit Kamejima(int n, string traducao) =>
        new($"{Script}:{n:D6}", TextUnitKind.Dialogue, null, "そう言いながら亀島は…", traducao, TranslationStatus.Machine);

    private static TextUnit BomDia(int n, string traducao) =>
        new($"{Script}:{n:D6}", TextUnitKind.Dialogue, null, "おはよう", traducao, TranslationStatus.Translated);

    // O contexto confere a data de gravação e o tamanho; dois arquivos gravados no mesmo instante e do mesmo tamanho não contariam como mudança.
    private static void Distinguir(string arquivo) =>
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref writes)));

    private static void DefinirPersonagem(ProjectFolder projeto, string alvo, CharacterGender genero = CharacterGender.Female)
    {
        projeto.SaveCharacters([new CharacterEntry("亀島", alvo, genero)]);
        Distinguir(projeto.CharactersPath);
    }

    private static IReadOnlyList<LineEntry> Refazer(ProjectLines linhas, bool regrasDoProjeto) =>
        linhas.Apply(linhas.BeginRecheck(regrasDoProjeto).Run());

    private static bool Tem(LineEntry linha, string regra) => linha.Issues.Any(i => i.RuleId == regra);

    [Fact]
    public void Gravar_com_o_termo_do_glossario_tira_o_aviso_de_glossario()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, new TextUnit($"{Script}:000001", TextUnitKind.Dialogue, null, "魔法学院へ行く", "Ir à escola de magia", TranslationStatus.Machine));
        projeto.SaveGlossary([new GlossaryTerm("魔法学院", "Academia de Magia")]);
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();
        Assert.True(Tem(entrada, "glossario-diferente"));

        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Ir à Academia de Magia" });

        Assert.False(Tem(gravada, "glossario-diferente"));
    }

    [Fact]
    public void Trocar_o_nome_do_personagem_acende_o_aviso_nas_linhas_que_usam_o_nome_antigo()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(1, "Ele olhou para Kamejima."), Kamejima(2, "Kameshima sorriu."));
        DefinirPersonagem(projeto, "Kamejima");
        var linhas = ProjectLines.Read(projeto);
        Assert.Equal(1, linhas.LinesOf(Script).Count(l => l.HasIssues));

        DefinirPersonagem(projeto, "Kameshima");
        var mudadas = Refazer(linhas, regrasDoProjeto: false);

        var porLinha = linhas.LinesOf(Script);
        Assert.True(Tem(porLinha[0], "personagem-diferente"));
        Assert.False(Tem(porLinha[1], "personagem-diferente"));
        Assert.Equal([1, 2], mudadas.Select(m => m.Number).Order());
    }

    [Fact]
    public void Mudar_o_estilo_dos_honorificos_refaz_o_aviso_das_linhas()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, new TextUnit($"{Script}:000001", TextUnitKind.Dialogue, null, "美咲ちゃん", "Misaki-chan", TranslationStatus.Machine));
        var config = projeto.LoadConfig();
        projeto.SaveConfig(config with { Style = config.Style with { Honorifics = HonorificsPolicy.Keep } });
        var linhas = ProjectLines.Read(projeto);
        Assert.Empty(linhas.LinesOf(Script).Single().Issues);

        projeto.SaveConfig(config with { Style = config.Style with { Honorifics = HonorificsPolicy.Remove } });
        Distinguir(projeto.ConfigPath);
        Refazer(linhas, regrasDoProjeto: false);

        Assert.True(Tem(linhas.LinesOf(Script).Single(), HonorificsRule.RuleId));
    }

    [Fact]
    public void Aplicar_ignora_a_linha_que_foi_gravada_depois_do_instantaneo()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, BomDia(1, "Bom dia"), BomDia(2, "Bom dia"));
        var linhas = ProjectLines.Read(projeto);
        var segunda = linhas.LinesOf(Script)[1];
        linhas.Replace(linhas.WithUnit(segunda, segunda.Unit with { Translation = "Olá" }));
        var refeita = linhas.BeginRecheck(includeProjectRules: true).Run();

        var gravada = linhas.LinesOf(Script)[1];
        linhas.Replace(linhas.WithUnit(gravada, gravada.Unit with { Translation = "Bom dia" }));
        var aplicadas = linhas.Apply(refeita);

        Assert.Empty(aplicadas);
        Assert.False(Tem(linhas.LinesOf(Script)[1], "traducao-inconsistente"));
    }
}
