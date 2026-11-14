using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Patching;

namespace VNForge.Core.Tests.Library;

public class AtomicFileTests
{
    private sealed class FakeDisk(bool local, Action<string, string, bool> move) : IProjectDisk
    {
        public bool IsLocal(string path) => local;

        public void Move(string source, string destination, bool overwrite) => move(source, destination, overwrite);
    }

    // O que o rclone com cache em modo full faz: o rename "dá certo" e o destino fica com 0 byte.
    private static void MoveAndZero(string source, string destination, bool overwrite)
    {
        File.Move(source, destination, overwrite);
        File.WriteAllBytes(destination, []);
    }

    [Fact]
    public void Gravacao_cujo_conteudo_relido_nao_bate_vira_erro_em_vez_de_sucesso()
    {
        using var temp = new TempFolder();
        var destino = Path.Combine(temp.Path, "vnforge.json");
        using var disco = ProjectDisk.Use(new FakeDisk(local: true, MoveAndZero));

        var erro = Assert.Throws<WriteVerificationException>(() => AtomicFile.WriteAllText(destino, "{\"novo\":true}"));

        Assert.Contains("0 bytes", erro.Message);
    }

    [Fact]
    public void Em_volume_nao_local_o_bak_some_no_sucesso_e_devolve_o_conteudo_antigo_quando_o_move_falha()
    {
        using var temp = new TempFolder();
        var destino = Path.Combine(temp.Path, "state.json");
        File.WriteAllText(destino, "antigo");
        var falhar = false;
        using var disco = ProjectDisk.Use(new FakeDisk(local: false, (origem, alvo, sobrescrever) =>
        {
            if (falhar)
            {
                throw new IOException("Acesso negado.");
            }

            File.Move(origem, alvo, sobrescrever);
        }));

        AtomicFile.WriteAllText(destino, "novo");

        Assert.Equal("novo", File.ReadAllText(destino));
        Assert.False(File.Exists(destino + ".bak"));

        falhar = true;
        var erro = Assert.Throws<WriteVerificationException>(() => AtomicFile.WriteAllText(destino, "outro"));

        Assert.Equal("novo", File.ReadAllText(destino));
        Assert.Equal("novo", File.ReadAllText(destino + ".bak"));
        Assert.Contains(".bak", erro.Message);
    }
}
