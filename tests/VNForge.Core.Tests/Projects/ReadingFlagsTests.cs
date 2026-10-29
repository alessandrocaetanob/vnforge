using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>A marca "soa estranho" do leitor: gravada em <c>Flags</c> sem tocar no resto da linha, e o filtro que a encontra.</summary>
public class ReadingFlagsTests
{
    private static TextUnit Linha(int n, params string[] marcas) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", "Oi.", TranslationStatus.Machine) { Flags = marcas };

    private static (ProjectFolder Projeto, LineEditor Editor) Preparar(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00001", linhas);
        return (projeto, new LineEditor(projeto, new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero))));
    }

    [Fact]
    public void WithOdd_poe_e_tira_a_marca_sem_mexer_nas_outras()
    {
        var linha = Linha(1, "marcador-diferente");

        var marcada = linha.WithOdd(true);

        Assert.True(marcada.IsOdd());
        Assert.Equal(["marcador-diferente", ReadingFlags.SoundsOdd], marcada.Flags);
        Assert.Equal(["marcador-diferente"], marcada.WithOdd(false).Flags);
    }

    [Fact]
    public void WithOdd_sem_mudanca_devolve_a_mesma_linha()
    {
        var linha = Linha(1);

        Assert.Same(linha, linha.WithOdd(false));
        var marcada = linha.WithOdd(true);
        Assert.Same(marcada, marcada.WithOdd(true));
    }

    [Fact]
    public void SetOdd_grava_a_marca_no_arquivo_e_deixa_traducao_e_status()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1), Linha(2));

        var salva = editor.SetOdd("yst00001", "yst00001:000001", true);

        Assert.True(salva.IsOdd());
        var lidas = projeto.LoadText("yst00001");
        Assert.True(lidas[0].IsOdd());
        Assert.Equal("Oi.", lidas[0].Translation);
        Assert.Equal(TranslationStatus.Machine, lidas[0].Status);
        Assert.False(lidas[1].IsOdd());
    }

    [Fact]
    public void SetOdd_falso_tira_a_marca_e_repetir_nao_regrava()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, ReadingFlags.SoundsOdd));
        var caminho = projeto.TextPath("yst00001");

        editor.SetOdd("yst00001", "yst00001:000001", false);
        Assert.False(projeto.LoadText("yst00001")[0].IsOdd());

        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        editor.SetOdd("yst00001", "yst00001:000001", false);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
    }

    [Fact]
    public void SetOdd_numa_linha_que_nao_existe_falha()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1));

        Assert.Throws<KeyNotFoundException>(() => editor.SetOdd("yst00001", "yst00001:000099", true));
    }

    [Fact]
    public void O_filtro_Odd_aceita_so_as_linhas_marcadas()
    {
        var marcada = new LineEntry("yst00001", 1, Linha(1, ReadingFlags.SoundsOdd), []);
        var outra = new LineEntry("yst00001", 2, Linha(2, "marcador-diferente"), []);

        var filtro = new LineFilter(LineStatusFilter.Odd);

        Assert.True(filtro.Matches(marcada));
        Assert.False(filtro.Matches(outra));
        Assert.Equal([marcada], filtro.Apply([marcada, outra]));
    }
}
