using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class NewFilePolicyTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "vnf-policy-" + Guid.NewGuid().ToString("N"));

    public NewFilePolicyTests()
    {
        Directory.CreateDirectory(Path.Combine(_game, "Game"));
        File.WriteAllText(Path.Combine(_game, "Game", "Leiame.TXT"), "original");
    }

    public void Dispose() => Directory.Delete(_game, recursive: true);

    [Theory]
    [InlineData("Game/novo.ypf")]
    public void Arquivo_novo_que_nao_e_texto_e_recusado_porque_iria_inteiro_no_patch(string path)
    {
        var error = Assert.Throws<VNForgeException>(() => NewFilePolicy.EnsureAllowed(_game, path));

        Assert.Contains("nunca um arquivo inteiro", error.Message);
    }

    [Theory]
    [InlineData("Game/traducao.txt")]
    public void Arquivo_novo_de_texto_que_o_jogo_nao_tem_passa(string path)
    {
        var error = Record.Exception(() => NewFilePolicy.EnsureAllowed(_game, path));

        Assert.Null(error);
    }

    [Fact]
    public void Nome_que_so_difere_de_um_original_na_caixa_e_recusado()
    {
        var error = Assert.Throws<VNForgeException>(() => NewFilePolicy.EnsureAllowed(_game, "Game/leiame.txt"));

        Assert.Contains("só muda a caixa", error.Message);
    }
}
