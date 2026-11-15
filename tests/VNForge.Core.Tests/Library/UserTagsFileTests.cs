using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R2")]
public sealed class UserTagsFileTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static UserTags Sample()
    {
        var tags = new UserTags();
        tags.Add("gb2", "rejogar");
        tags.Add("ga1", "favorito", "#ff8800");
        tags.Add("ga1", "pra traduzir");
        tags.Add("gb2", "favorito");
        tags.Define("sem uso");
        tags.SavedFilter = LibraryTagFilter.Create(["favorito", "vndb:g1"], any: true, ["minha:rejogar"]);
        return tags;
    }

    [Fact]
    public void Update_devolve_o_resultado_e_com_arquivo_ilegivel_recusa_sem_sobrescrever()
    {
        var file = new UserTagsFile(_dir.Path);
        Assert.Equal(1, file.Update(t => t.Add("g1", "a") is { } ? 1 : 0));
        File.WriteAllText(file.Path, "{ isto não é json");

        Assert.Throws<LibraryException>(() => file.Update(t => t.Add("g1", "b")));

        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }
}
