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
    public void Gravar_com_o_nome_do_personagem_tira_o_aviso_sem_reler_o_projeto()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(99, "Ele olhou para Kamejima."));
        DefinirPersonagem(projeto, "Kameshima");
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();
        var aviso = Assert.Single(entrada.Issues, i => i.RuleId == "personagem-diferente");
        Assert.Contains("“亀島” deveria ser traduzido como “Kameshima”", aviso.Message, StringComparison.Ordinal);

        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Ele olhou para Kameshima." });

        Assert.False(Tem(gravada, "personagem-diferente"));
    }

    [Fact]
    public void Gravar_um_nome_diferente_do_personagem_acende_o_aviso_na_hora()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(1, "Ele olhou para Kameshima."));
        DefinirPersonagem(projeto, "Kameshima");
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();
        Assert.Empty(entrada.Issues);

        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Ele olhou para Kamejima." });

        Assert.True(Tem(gravada, "personagem-diferente"));
    }

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
    public void Gravar_vale_com_o_personagem_gravado_depois_da_leitura()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(1, "Ele olhou para Kamejima."));
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();
        Assert.Empty(entrada.Issues);

        DefinirPersonagem(projeto, "Kameshima");
        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Ele olhou para Kamejima!" });

        Assert.True(Tem(gravada, "personagem-diferente"));
    }

    [Fact]
    public void Gravar_com_o_personagem_ilegivel_mantem_o_ultimo_contexto_bom()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(1, "Ele olhou para Kameshima."));
        DefinirPersonagem(projeto, "Kameshima");
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();

        File.WriteAllText(projeto.CharactersPath, "{ isto não é json");
        Distinguir(projeto.CharactersPath);
        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Ele olhou para Kamejima." });

        Assert.True(Tem(gravada, "personagem-diferente"));
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
    public void Nada_muda_quando_o_contexto_e_o_mesmo()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, Kamejima(1, "Ele olhou para Kamejima."));
        DefinirPersonagem(projeto, "Kameshima");
        var linhas = ProjectLines.Read(projeto);

        Assert.Empty(Refazer(linhas, regrasDoProjeto: true));
    }

    [Fact]
    public void Gravar_duas_linhas_com_o_mesmo_original_e_traducoes_diferentes_acende_a_inconsistencia_sem_reler()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, BomDia(1, "Bom dia"), BomDia(2, "Bom dia"));
        var linhas = ProjectLines.Read(projeto);
        Assert.All(linhas.LinesOf(Script), l => Assert.False(Tem(l, "traducao-inconsistente")));

        var segunda = linhas.LinesOf(Script)[1];
        linhas.Replace(linhas.WithUnit(segunda, segunda.Unit with { Translation = "Olá" }));
        Refazer(linhas, regrasDoProjeto: true);

        Assert.True(Tem(linhas.LinesOf(Script)[1], "traducao-inconsistente"));

        var corrigida = linhas.LinesOf(Script)[1];
        linhas.Replace(linhas.WithUnit(corrigida, corrigida.Unit with { Translation = "Bom dia" }));
        Refazer(linhas, regrasDoProjeto: true);

        Assert.All(linhas.LinesOf(Script), l => Assert.False(Tem(l, "traducao-inconsistente")));
    }

    [Fact]
    public void Sem_as_regras_do_projeto_a_inconsistencia_fica_como_estava()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, BomDia(1, "Bom dia"), BomDia(2, "Bom dia"));
        var linhas = ProjectLines.Read(projeto);
        var segunda = linhas.LinesOf(Script)[1];
        linhas.Replace(linhas.WithUnit(segunda, segunda.Unit with { Translation = "Olá" }));

        Refazer(linhas, regrasDoProjeto: false);

        Assert.False(Tem(linhas.LinesOf(Script)[1], "traducao-inconsistente"));
    }

    [Fact]
    public void Definir_o_genero_do_personagem_tira_o_aviso_de_personagem_sem_genero()
    {
        using var pasta = new TempFolder();
        var projeto = Projeto(pasta.Path, new TextUnit($"{Script}:000001", TextUnitKind.Dialogue, "亀島", "…", "Estou cansado.", TranslationStatus.Machine));
        DefinirPersonagem(projeto, "Kameshima", CharacterGender.Unknown);
        var linhas = ProjectLines.Read(projeto);
        Assert.True(Tem(linhas.LinesOf(Script).Single(), GenderRule.RuleId));

        DefinirPersonagem(projeto, "Kameshima", CharacterGender.Male);
        Refazer(linhas, regrasDoProjeto: true);

        Assert.False(Tem(linhas.LinesOf(Script).Single(), GenderRule.RuleId));
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
