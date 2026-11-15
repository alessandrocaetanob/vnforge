using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary>YPF e YBN inválidos viram mensagem em português com o arquivo, nunca "Erro inesperado" (C4 · #252).</summary>
[Trait("Risco", "R7")]
public sealed class InvalidInputTests : IDisposable
{
    private const string Caminho = @"C:\jogo\ysbin.ypf";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string At(string name) => Path.Combine(_cli.WorkingDirectory, name);

    public static TheoryData<Exception> FormatFailures() => new()
    {
        new EndOfStreamException("Unable to read beyond the end of the stream."),
    };

    [Theory]
    [MemberData(nameof(FormatFailures))]
    public void Falha_de_formato_vira_arquivo_invalido_em_portugues_com_o_caminho(Exception failure)
    {
        var thrown = Assert.Throws<InvalidFileException>(() => InputFileGuard.Read<int>(Caminho, () => throw failure));

        Assert.Contains(Caminho, thrown.Message);
        Assert.Contains("é inválido", thrown.Message);
        Assert.DoesNotContain(failure.Message, thrown.Message);
        Assert.Same(failure, thrown.InnerException);
    }

    [Fact]
    public void Falha_de_leitura_do_disco_vira_nao_foi_possivel_ler_com_o_caminho()
    {
        var thrown = Assert.Throws<VNForgeException>(() => InputFileGuard.Read<int>(Caminho, () => throw new IOException("disk")));

        Assert.Equal($"Não foi possível ler \"{Caminho}\": disk", thrown.Message);
    }
}
