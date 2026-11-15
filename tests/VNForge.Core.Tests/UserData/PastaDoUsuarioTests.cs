using System.Security.Cryptography;
using VNForge.Core.Tests.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.UserData;
using VNForge.Patching;

namespace VNForge.Core.Tests.UserData;

/// <summary>
/// A pasta de dados do usuário (#694): a ordem de busca que o app e o <c>vnf</c> usam, a pergunta da primeira abertura que quem já usa o app não vê, e a troca de pasta
/// que copia e confere sem perder arquivo. Tudo numa pasta temporária e com as variáveis na mão do teste: nada toca o <c>%APPDATA%</c> real.
/// </summary>
public sealed class PastaDoUsuarioTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Roaming => Path.Combine(_temp.Path, "roaming");

    private string Local => Path.Combine(_temp.Path, "local");

    private UserFolders Resolve(Dictionary<string, string>? environment = null) =>
        UserFolders.Resolve(name => environment is not null && environment.TryGetValue(name, out var value) ? value : null, Roaming, Local);

    // R6 (o CLI e o app leem a mesma escolha): variável, depois ponteiro, depois o padrão de hoje; cada pasta decide por si.
    [Fact]
    public void A_pasta_vem_da_variavel_depois_do_ponteiro_e_por_fim_do_padrao_de_hoje()
    {
        var defaults = Resolve();
        Assert.Equal((Path.Combine(Roaming, "VNForge"), UserFolderSource.Default), (defaults.Config.Path, defaults.Config.Source));
        Assert.Equal((Path.Combine(Local, "VNForge"), UserFolderSource.Default), (defaults.Cache.Path, defaults.Cache.Source));
        Assert.Equal(Path.Combine(Local, "VNForge", "logs"), defaults.LogDirectory);
        Assert.Null(defaults.Problem);

        var chosen = Directory.CreateDirectory(Path.Combine(_temp.Path, "escolhida")).FullName;
        UserFolderPointer.Save(defaults.PointerPath, chosen, defaults.DefaultCache);
        var pointed = Resolve();
        Assert.Equal((chosen, UserFolderSource.Pointer), (pointed.Config.Path, pointed.Config.Source));
        Assert.Equal(UserFolderSource.Pointer, pointed.Cache.Source);

        var fromVariable = Directory.CreateDirectory(Path.Combine(_temp.Path, "variavel")).FullName;
        var withVariable = Resolve(new() { [UserFolders.ConfigVariable] = fromVariable });
        Assert.Equal((fromVariable, UserFolderSource.Environment), (withVariable.Config.Path, withVariable.Config.Source));
        Assert.Equal(pointed.Cache.Path, withVariable.Cache.Path);

        var gone = Path.Combine(_temp.Path, "disco-desconectado");
        Assert.Equal(
            $"{UserFolders.ConfigVariable} aponta para {gone}, que não existe. Crie a pasta ou ajuste a variável.",
            Resolve(new() { [UserFolders.ConfigVariable] = gone }).Problem?.Message);
        UserFolderPointer.Save(defaults.PointerPath, gone, defaults.DefaultCache);
        Assert.Equal($"A pasta de dados {gone} não está disponível. Conecte a unidade e tente de novo.", Resolve().Problem?.Message);
    }

    // R6: quem atualiza não vê a pergunta nem tem nada movido ou gravado.
    [Theory]
    [InlineData("library.json", false)]
    [InlineData("recent.json", false)]
    [InlineData("app.json", false)]
    [InlineData("apikey.dat", false)]
    [InlineData("ai.json", true)]
    [InlineData(null, true)]
    public void R6_quem_ja_tem_arquivos_na_pasta_padrao_nao_ve_a_pergunta(string? existing, bool asks)
    {
        var folders = Resolve();
        if (existing is not null)
        {
            Directory.CreateDirectory(folders.Config.Path);
            File.WriteAllText(Path.Combine(folders.Config.Path, existing), "{}");
        }

        Assert.Equal(asks, folders.MustAsk);
        Assert.False(File.Exists(folders.PointerPath));

        if (asks)
        {
            UserFolderMover.Choose(folders, folders.DefaultConfig, folders.DefaultCache);
            Assert.False(Resolve().MustAsk);

            // O ponteiro nunca aponta para pasta que não existe: o próximo início não diz "pasta sumiu" e o vnf não recusa tudo.
            Assert.Null(Resolve().Problem);
            Assert.True(Directory.Exists(folders.DefaultConfig) && Directory.Exists(folders.DefaultCache));
        }
    }

    // R2: a troca copia, confere e grava o ponteiro por último; a falha no meio deixa a pasta antiga, o ponteiro e a pasta nova como estavam.
    [Fact]
    public void R2_trocar_a_pasta_copia_confere_e_a_falha_no_meio_deixa_a_antiga_intacta_e_o_ponteiro_sem_mudar()
    {
        var folders = Resolve();
        var old = folders.Config.Path;
        Seed(old, "library.json", "{\"games\":[]}");
        Seed(old, Path.Combine("dictionary", "jmdict.bin"), new string('a', 5000));
        Seed(old, Path.Combine("sheets", "v1.json"), "ficha");
        Seed(old, "apikey.dat", "cifrado pelo DPAPI");
        Seed(old, "library.json.lock", "trava");
        var before = Hashes(old);
        var destination = Path.Combine(_temp.Path, "destino");

        // Falha no meio: o disco enche ao gravar sheets\v1.json (a pasta dictionary já foi copiada).
        var plan = UserFolderMover.Prepare(UserFolderKind.Config, old, destination);
        Assert.Equal(4, plan.FileCount);
        UserFolderException? failure;
        using (ProjectDisk.Use(new FailingDisk(DiskFailure.DiskFull, local: true, fails: file => file.EndsWith("v1.json", StringComparison.OrdinalIgnoreCase))))
        {
            failure = Assert.Throws<UserFolderException>(() => UserFolderMover.Move(folders, plan, deleteOld: true, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.DoesNotContain("not enough space", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(failure.Percent);
        Assert.Equal(before, Hashes(old));
        Assert.False(File.Exists(folders.PointerPath));
        Assert.False(Directory.Exists(destination), "o que já tinha sido copiado é removido");
        Assert.Null(Resolve().Problem);
        Assert.Equal(old, Resolve().Config.Path);

        // Sem falha: tudo copiado e conferido, o ponteiro aponta para a pasta nova e a antiga fica como estava (a caixa "apagar" desmarcada).
        UserFolderMover.Move(folders, plan, deleteOld: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(before.Where(entry => !entry.Key.EndsWith(".lock", StringComparison.Ordinal)).ToDictionary(), Hashes(destination));
        Assert.Equal(before, Hashes(old));
        var moved = Resolve();
        Assert.Equal((destination, UserFolderSource.Pointer), (moved.Config.Path, moved.Config.Source));
        Assert.Equal(moved.DefaultCache, moved.Cache.Path);
    }

    // R2: a troca não pode apagar o que a outra pasta tem em uso, e mover os dados de uma origem vazia também deixa o ponteiro apontando para pasta que existe.
    [Fact]
    public void R2_a_pasta_nova_nao_se_sobrepoe_a_outra_nem_contem_a_atual_e_a_origem_vazia_deixa_a_pasta_criada()
    {
        var folders = Resolve();
        var cache = folders.Cache.Path;
        var inside = Path.Combine(_temp.Path, "dentro");

        Assert.Contains("não podem ser a mesma", Assert.Throws<UserFolderException>(() => UserFolderMover.Prepare(UserFolderKind.Config, folders.Config.Path, cache, cache)).Message);
        Assert.Contains("uma dentro da outra", Assert.Throws<UserFolderException>(() => UserFolderMover.Prepare(UserFolderKind.Config, folders.Config.Path, Path.Combine(cache, "dados"), cache)).Message);
        Assert.Contains("uma dentro da outra", Assert.Throws<UserFolderException>(() => UserFolderMover.Prepare(UserFolderKind.Config, folders.Config.Path, Path.Combine(_temp.Path, "pai"), Path.Combine(_temp.Path, "pai", "x"))).Message);
        Assert.Contains("conter a pasta atual", Assert.Throws<UserFolderException>(() => UserFolderMover.Prepare(UserFolderKind.Config, Path.Combine(_temp.Path, "a", "b"), Path.Combine(_temp.Path, "a"))).Message);

        var plan = UserFolderMover.Prepare(UserFolderKind.Config, folders.Config.Path, inside, cache);
        Assert.Equal(0, plan.FileCount);
        Assert.False(Directory.Exists(inside), "conferir a pasta nova não deixa pasta para trás");
        UserFolderMover.Move(folders, plan, deleteOld: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(Directory.Exists(inside));
        Assert.Null(Resolve().Problem);
    }

    private static void Seed(string folder, string relative, string text)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static Dictionary<string, string> Hashes(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(file => Path.GetRelativePath(folder, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
            : [];
}
