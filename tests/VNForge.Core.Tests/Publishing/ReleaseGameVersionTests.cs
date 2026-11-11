using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseGameVersionTests
{
    [Fact]
    public void A_versao_digitada_vale_mais_que_a_do_executavel_e_a_do_projeto()
    {
        Assert.Equal("1.02", ReleaseService.ResolveGameVersion(" 1.02 ", "1.01", "1.00"));
    }

    [Fact]
    public void Sem_versao_digitada_vale_a_do_executavel()
    {
        Assert.Equal("1.01", ReleaseService.ResolveGameVersion(null, "1.01", "1.00"));
        Assert.Equal("1.01", ReleaseService.ResolveGameVersion("  ", "1.01", "1.00"));
    }

    [Fact]
    public void Sem_versao_digitada_e_sem_versao_no_executavel_vale_a_do_projeto()
    {
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, null, "1.00"));
    }

    [Fact]
    public void Sem_nenhuma_das_tres_o_patch_nao_leva_versao()
    {
        Assert.Null(ReleaseService.ResolveGameVersion(null, null, null));
    }

    [Fact]
    public void Versao_digitada_invalida_e_recusada_com_mensagem_em_portugues()
    {
        var error = Assert.Throws<VNForgeException>(() => ReleaseService.ResolveGameVersion("1.0\n2", null, null));

        Assert.Contains("Versão do jogo inválida", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Patch_e_tela_seguem_a_mesma_regra_com_game_version_1_00_e_executavel_1_05()
    {
        // A tela chama ResolveGameVersion(null, executável, projeto): o mesmo valor que o patch leva.
        Assert.Equal("1.05", ReleaseService.ResolveGameVersion(null, "1.05", "1.00"));
        Assert.Equal("1.05", ReleaseService.ResolveGameVersion("  ", " 1.05 ", "1.00"));
    }

    [Fact]
    public void Versao_do_executavel_ou_do_projeto_que_nao_vale_e_ignorada_sem_erro()
    {
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, new string('9', GameConfig.MaxVersionLength + 1), "1.00"));
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, "1.0\u00002", "1.00"));
        Assert.Null(ReleaseService.ResolveGameVersion(null, "1.0\n2", "x\ty\n"));
    }

    [Fact]
    public void Versao_digitada_ou_guardada_com_espaco_nas_pontas_e_aparada()
    {
        Assert.Equal("1.02", ReleaseService.ResolveGameVersion("\t1.02 ", null, null));
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, null, " 1.00 "));
    }

    [Theory]
    [InlineData(" 1.00", false)]
    [InlineData("1.00 ", false)]
    [InlineData("1.00", true)]
    [InlineData("Ver 1.2 (final)", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("1.0\t2", false)]
    public void Versao_do_jogo_e_texto_livre_sem_controle(string? version, bool valid)
    {
        Assert.Equal(valid, GameConfig.IsValidVersion(version));
    }
}
