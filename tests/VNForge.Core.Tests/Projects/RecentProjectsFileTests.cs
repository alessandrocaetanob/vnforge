using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class RecentProjectsFileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sem_arquivo_a_lista_e_vazia()
    {
        using var temp = new TempFolder();

        Assert.Empty(new RecentProjectsFile(temp.Path).Load());
    }

    [Fact]
    public void Add_grava_e_Load_devolve_o_mais_recente_primeiro()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);

        file.Add(@"D:\A", T0);
        file.Add(@"D:\B", T0.AddMinutes(5));

        var lista = new RecentProjectsFile(temp.Path).Load();
        Assert.Equal([@"D:\B", @"D:\A"], lista.Select(r => r.Path));
        Assert.Equal(T0.AddMinutes(5), lista[0].OpenedAt);
    }

    [Fact]
    public void Abrir_de_novo_move_para_o_topo_sem_duplicar()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        file.Add(@"D:\A", T0);
        file.Add(@"D:\B", T0.AddMinutes(1));

        file.Add(@"d:\a\", T0.AddMinutes(2));

        var lista = file.Load();
        Assert.Equal(2, lista.Count);
        Assert.Equal(@"d:\a", lista[0].Path, ignoreCase: true);
    }

    [Fact]
    public void Guarda_so_os_dez_mais_recentes()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        for (var i = 0; i < 12; i++)
        {
            file.Add($@"D:\P{i}", T0.AddMinutes(i));
        }

        var lista = file.Load();
        Assert.Equal(10, lista.Count);
        Assert.Equal(@"D:\P11", lista[0].Path);
    }

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

    [Fact]
    public void Arquivo_corrompido_vira_lista_vazia_e_o_proximo_Add_o_substitui()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        File.WriteAllText(file.Path, "{ quebrado");

        Assert.Empty(file.Load());
        file.Add(@"D:\A", T0);
        Assert.Single(file.Load());
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
    public void Remove_de_caminho_ausente_nao_muda_a_lista()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path);
        file.Add("D:/A", DateTimeOffset.UnixEpoch);

        file.Remove("D:/Z");

        Assert.Single(file.Load());
    }

    [Fact]
    public void Capacidade_menor_corta_a_lista()
    {
        using var temp = new TempFolder();
        var file = new RecentProjectsFile(temp.Path, capacity: 2);
        for (var i = 0; i < 4; i++)
        {
            file.Add($"D:/P{i}", DateTimeOffset.UnixEpoch.AddMinutes(i));
        }

        Assert.Equal(["D:/P3", "D:/P2"], file.Load().Select(r => r.Path));
    }
}
