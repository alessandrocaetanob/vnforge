using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

public class RealApiModelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_variavel_usa_haiku(string? value) =>
        Assert.Equal(ClaudeModels.Haiku, RealApiModel.Resolve(value));

    [Theory]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-haiku-5-5")]
    [InlineData("claude-fable-5-1")]
    public void Valor_conhecido_e_usado(string value) =>
        Assert.Equal(value, RealApiModel.Resolve(value));

    [Fact]
    public void Valor_com_espacos_nas_pontas_e_aparado() =>
        Assert.Equal(ClaudeModels.Opus, RealApiModel.Resolve("  claude-opus-5-5 "));

    [Fact]
    public void Valor_desconhecido_falha_com_mensagem_clara()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RealApiModel.Resolve("claude-opus-9"));

        Assert.Contains(RealApiModel.VariableName, ex.Message);
        Assert.Contains("claude-opus-9", ex.Message);
        Assert.Contains(ClaudeModels.Opus, ex.Message);
    }
}
