using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseGameVersionTests
{
    private static ProjectConfig Config(string? version = null) => new(
        "amaoto",
        new GameConfig("jogo", "game.exe") { Version = version },
        new EngineConfig("yuris"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("cp1252"),
        AiConfig.Default);

    [Fact]
    public void A_versao_digitada_vale_mais_que_a_do_executavel_e_a_do_projeto()
    {
        Assert.Equal("1.02", ReleaseService.ResolveGameVersion(" 1.02 ", "1.01", Config("1.00")));
    }

    [Fact]
    public void Sem_versao_digitada_vale_a_do_executavel()
    {
        Assert.Equal("1.01", ReleaseService.ResolveGameVersion(null, "1.01", Config("1.00")));
        Assert.Equal("1.01", ReleaseService.ResolveGameVersion("  ", "1.01", Config("1.00")));
    }

    [Fact]
    public void Sem_versao_digitada_e_sem_versao_no_executavel_vale_a_do_projeto()
    {
        Assert.Equal("1.00", ReleaseService.ResolveGameVersion(null, null, Config("1.00")));
    }

    [Fact]
    public void Sem_nenhuma_das_tres_o_patch_nao_leva_versao()
    {
        Assert.Null(ReleaseService.ResolveGameVersion(null, null, Config()));
    }

    [Fact]
    public void Versao_digitada_invalida_e_recusada_com_mensagem_em_portugues()
    {
        var error = Assert.Throws<VNForgeException>(() => ReleaseService.ResolveGameVersion("1.0\n2", null, Config()));

        Assert.Contains("Versão do jogo inválida", error.Message, StringComparison.Ordinal);
    }

    [Theory]
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
