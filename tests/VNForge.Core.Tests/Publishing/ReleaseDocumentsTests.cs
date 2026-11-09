using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseDocumentsTests
{
    private const string Notices =
        "AVISOS\nO instalador usa a biblioteca VCDiff (Apache 2.0) e outras bibliotecas de código aberto. Os textos das licenças estão em LICENCAS.txt e no próprio instalador (instalador --licencas).\n";

    private static readonly DateTimeOffset Date = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static ChangesSinceTest SixScenes() => new(
        ChangesSinceTestState.Changed,
        null,
        false,
        [
            new ChangedScene("yst00001", 1, 30, []),
            new ChangedScene("yst00002", 2, 1, []),
            new ChangedScene("yst00003", 3, 1, []),
            new ChangedScene("yst00004", 4, 5, []),
            new ChangedScene("yst00005", 5, 3, []),
            new ChangedScene("yst00006", 6, 1, []),
        ]);

    [Fact]
    public void O_leia_me_segue_a_redacao_do_quadro_com_cada_papel_dos_creditos_na_sua_linha()
    {
        var documents = new ReleaseDocuments("Amaoto no Hakoniwa", "1.2.0", "1.1.0", Date, "Tradução: Equipe Chuva Mansa · Revisão: Hana", SixScenes());

        Assert.Equal(
            "Amaoto no Hakoniwa · Tradução para português do Brasil\nVersão 1.2.0 · 08/11/2026\n\n"
            + "COMO INSTALAR\n1. Feche o jogo.\n2. Execute o instalador.\n3. Aponte a pasta do jogo e clique em Instalar.\n\n"
            + "COMO DESINSTALAR\nExecute o instalador de novo e escolha\nVoltar ao jogo original.\n\n"
            + "O QUE MUDOU DESDE A 1.1.0\n41 linhas em 6 cenas. Veja MUDANCAS.txt.\n\n"
            + "CRÉDITOS\nTradução: Equipe Chuva Mansa\nRevisão: Hana\n\n"
            + Notices,
            documents.ReadMe().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Sem_creditos_o_leia_me_pula_a_secao_e_mantem_um_so_espaco_antes_dos_avisos()
    {
        var documents = new ReleaseDocuments("Amaoto", "1.0.0", null, Date, null, ChangesSinceTest.None);

        var text = documents.ReadMe().ReplaceLineEndings("\n");

        Assert.DoesNotContain("CRÉDITOS", text, StringComparison.Ordinal);
        Assert.Contains("O QUE HÁ NESTA VERSÃO\nPrimeira versão do patch.\n\nAVISOS\n", text, StringComparison.Ordinal);
    }
}
