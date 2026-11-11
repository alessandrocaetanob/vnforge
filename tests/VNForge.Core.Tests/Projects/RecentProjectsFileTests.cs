using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class RecentProjectsFileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Remove_tira_o_projeto_da_lista()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        file.Add(@"D:\A", T0);
        file.Add(@"D:\B", T0);

        file.Remove(@"D:\A");

        Assert.Equal([@"D:\B"], file.Load().Select(r => r.Path));
    }
}

public class RecentProjectsFileBordasTests
{
    [Fact]
    public void Pasta_padrao_e_a_VNForge_dentro_da_pasta_de_configuracao_do_usuario() =>
        Assert.Equal("VNForge", Path.GetFileName(RecentProjectsFile.DefaultDirectory()));

    [Fact]
    public void Json_null_e_entradas_com_caminho_vazio_sao_ignorados()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        File.WriteAllText(file.Path, "null");
        Assert.Empty(file.Load());

        File.WriteAllText(file.Path, """[{"Path":"  ","OpenedAt":"2026-10-24T10:00:00+00:00"},{"Path":"D:/A","OpenedAt":"2026-10-24T10:00:00+00:00"}]""");
        Assert.Equal(["D:/A"], file.Load().Select(r => r.Path));
    }

    [Fact]
    public void Add_recusa_caminho_vazio()
    {
        using var temp = new TempFolder();

        Assert.Throws<ArgumentException>(() => new RecentProjectsFile(temp.Path).Add(" ", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Load_de_arquivo_ilegivel_continua_devolvendo_lista_vazia()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        File.WriteAllText(file.Path, "{ quebrado");

        Assert.Empty(file.Load());
    }
}
