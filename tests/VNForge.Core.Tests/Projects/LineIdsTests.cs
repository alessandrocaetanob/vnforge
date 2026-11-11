using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class LineIdsTests
{
    private static LineEntry Entrada(string script, int numero, string traducao = "texto") =>
        new(script, numero, new TextUnit($"{script}:{numero * 1000:D6}", TextUnitKind.Dialogue, null, $"原文{numero}", traducao, TranslationStatus.Translated), []);

    private static readonly LineEntry[] Linhas =
    [
        Entrada("yst00112", 1),
        Entrada("yst00112", 33, "Olá, mundo"),
        Entrada("yst00113", 1),
    ];

    [Fact]
    public void Id_completo_ignora_maiusculas_e_espacos_nas_pontas()
    {
        Assert.Single(new LineFilter(Query: "  YST00112:033000 ").Apply(Linhas));
    }

    [Fact]
    public void Texto_comum_procura_no_original_e_na_traducao_sem_acento()
    {
        Assert.Equal(["yst00112:033000"], new LineFilter(Query: "ola, MUNDO").Apply(Linhas).Select(l => l.Unit.Id));
        Assert.Single(new LineFilter(Query: "原文33").Apply(Linhas));
    }

    [Theory]
    [InlineData("yst00112")]
    [InlineData("yst00112:")]
    [InlineData("20:19:08")]
    [InlineData("yst00112:033792 depois")]
    [InlineData("")]
    public void TryParse_recusa_o_que_nao_e_um_id_completo(string texto)
    {
        Assert.False(LineIds.TryParse(texto, out _));
    }

    [Fact]
    public void Find_acha_os_ids_citados_numa_mensagem_de_erro()
    {
        const string mensagem = "Não foi possível reconstruir o script 'yst00112.ybn' com o text/yst00112.json: A tradução da unidade 'yst00112:033792' não pode ser gravada; veja também yst00113:000010.";

        var citados = LineIds.Find(mensagem);

        Assert.Equal(["yst00112:033792", "yst00113:000010"], citados.Select(c => c.UnitId));
        Assert.All(citados, c => Assert.Equal(c.UnitId, mensagem.Substring(c.Start, c.Length)));
        Assert.Equal(["yst00112", "yst00113"], citados.Select(c => c.ScriptKey));
    }

    [Fact]
    public void Find_ignora_horas_e_rotulos_que_nao_sao_ids()
    {
        Assert.Empty(LineIds.Find("20:19:08  Build falhou: 4 caracteres, posição 0"));
        Assert.Empty(LineIds.Find(""));
    }

    [Fact]
    public void Find_acha_o_id_seguido_de_dois_pontos_e_ignora_hora_colada()
    {
        Assert.Equal(["yst00112:033792"], LineIds.Find("yst00112:033792: tradução inválida").Select(c => c.UnitId));
        Assert.Empty(LineIds.Find("ab:12:30"));
    }
}
