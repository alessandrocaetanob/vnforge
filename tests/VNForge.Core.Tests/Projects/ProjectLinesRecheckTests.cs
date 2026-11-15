using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>#372: gravar uma linha ou mudar o glossário, os personagens e o estilo refaz os avisos sem reler o projeto.</summary>
[Trait("Risco", "R6")]
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
}
