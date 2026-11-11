using Spectre.Console;
using VNForge.Cli;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class CliViewsTests
{
    // Pendente, IA, traduzida, revisada, aprovada: a ordem do StatusCounts do Core.
    private static readonly StatusCounts Counts = new(Untranslated: 124, Machine: 89, Translated: 70, Reviewed: 36, Approved: 34);

    [Theory]
    [InlineData(10)]
    public void Barra_ocupa_exatamente_a_largura(int width)
    {
        var cells = DistributionBar.Allocate([34, 36, 70, 89, 124], width);

        Assert.Equal(width, cells.Sum());
    }

}
