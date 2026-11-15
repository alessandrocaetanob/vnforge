using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R7")]
public class RecentProjectsFileBordasTests
{
    [Fact]
    public void Add_recusa_caminho_vazio()
    {
        using var temp = new TempFolder();

        Assert.Throws<ArgumentException>(() => new RecentProjectsFile(temp.Path).Add(" ", DateTimeOffset.UnixEpoch));
    }
}
