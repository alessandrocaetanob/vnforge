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

        var erro = Assert.Throws<FileWriteVerificationException>(() => AtomicFile.WriteAllText(destino, "{\"novo\":true}"));

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
        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak"));

        falhar = true;
        var erro = Assert.Throws<FileWriteVerificationException>(() => AtomicFile.WriteAllText(destino, "outro"));

        Assert.Equal("novo", File.ReadAllText(destino));
        Assert.Equal("novo", File.ReadAllText(Assert.Single(Directory.GetFiles(temp.Path, "*.bak"))));
        Assert.Contains(".bak", erro.Message);
    }

    private static ProjectConfig Config() => new(
        "Minha VN",
        new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default);

    // R2: o processo caiu entre apagar o vnforge.json e pôr o novo no lugar; só o .bak ficou, e o projeto não pode parecer inexistente.
    [Fact]
    public void R2_bak_sobrando_de_uma_queda_volta_a_ser_o_arquivo_ao_abrir_o_projeto()
    {
        using var temp = new TempFolder();
        var raiz = Path.Combine(temp.Path, "projeto");
        var criado = ProjectFolder.Create(raiz, Config());
        var original = File.ReadAllBytes(criado.ConfigPath);
        File.Move(criado.ConfigPath, $"{criado.ConfigPath}.{Guid.NewGuid():N}.bak");

        var aberto = ProjectFolder.Open(raiz);

        Assert.Equal("Minha VN", aberto.LoadConfig().Name);
        Assert.Equal(original, File.ReadAllBytes(aberto.ConfigPath));
        Assert.Empty(Directory.GetFiles(raiz, "*.bak"));
    }

    // R2: criar o projeto num disco cuja gravação sai vazia dava "Já existe um vnforge.json" e deixava um arquivo de 0 byte.
    [Fact]
    public void R2_criar_projeto_com_conferencia_que_falha_da_erro_de_gravacao_e_nao_deixa_arquivo_vazio()
    {
        using var temp = new TempFolder();
        var raiz = Path.Combine(temp.Path, "projeto");
        using var disco = ProjectDisk.Use(new FakeDisk(local: true, MoveAndZero));

        var erro = Assert.Throws<FileWriteVerificationException>(() => ProjectFolder.Create(raiz, Config()));

        Assert.Contains("0 bytes", erro.Message);
        Assert.False(File.Exists(Path.Combine(raiz, ProjectFolder.ConfigFileName)));
    }
}
