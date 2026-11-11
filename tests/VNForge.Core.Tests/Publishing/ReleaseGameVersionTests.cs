using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseGameVersionTests
{

    [Fact]
    public void Versao_digitada_invalida_e_recusada_com_mensagem_em_portugues()
    {
        var error = Assert.Throws<VNForgeException>(() => ReleaseService.ResolveGameVersion("1.0\n2", null, null));

        Assert.Contains("Versão do jogo inválida", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Versao_digitada_ou_guardada_com_espaco_nas_pontas_e_aparada()
    {
        Assert.Equal("1.02", ReleaseService.ResolveGameVersion("\t1.02 ", null, null));
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, null, " 1.00 "));
    }
}
