using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Glossary;

public class SpeakerNamesTests
{
    [Theory]
    [InlineData("？？？")]
    [InlineData("???")]
    [InlineData("！？")]
    [InlineData("…")]
    [InlineData("・・・")]
    [InlineData("  ？？？　")]
    public void Falante_feito_so_de_sinais_nao_e_nome(string speaker)
    {
        Assert.True(SpeakerNames.IsSignsOnly(speaker));
    }

    [Theory]
    [InlineData("美咲")]
    [InlineData("ミサキ")]
    [InlineData("Misaki")]
    [InlineData("A")]
    [InlineData("？？？の声")]
    [InlineData("生徒1")]
    public void Falante_com_letra_ou_digito_e_nome(string speaker)
    {
        Assert.False(SpeakerNames.IsSignsOnly(speaker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_falante_nao_conta_como_so_sinais(string? speaker)
    {
        Assert.False(SpeakerNames.IsSignsOnly(speaker));
    }
}
