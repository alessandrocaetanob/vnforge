using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectGameRelocatorTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string Original = "original";
    private const string Traduzido = "traduzido";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;

    public ProjectGameRelocatorTests()
    {
        _game = Path.Combine(_temp.Path, "antigo", "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, YsbinYpf), Original);
        File.WriteAllText(Path.Combine(_game, "jogo.exe"), "MZ");
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace('\\', '/'), "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
        var bytes = Encoding.UTF8.GetBytes(Original);
        _project.SaveState(new PipelineState([new OriginalFile(YsbinYpf, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)))], new Dictionary<string, StageState>(), _game));
    }

    public void Dispose() => _temp.Dispose();

    private string Inject()
    {
        Directory.CreateDirectory(_project.BuildDirectory);
        File.WriteAllText(Path.Combine(_project.BuildDirectory, YsbinYpf), Traduzido);
        new GameInjector().Inject(GameInjector.Plan(_project));
        return Path.Combine(_game, YsbinYpf);
    }

    // O jogo é copiado e a pasta antiga fica onde estava: é o caso em que o restore, sem a troca, escreveria no lugar errado.
    private string CopyGameTo(string name)
    {
        var target = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(_game))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        return target;
    }

    private string[] ProjectFiles() =>
        [File.ReadAllText(_project.ConfigPath), File.ReadAllText(_project.StatePath), .. Directory.EnumerateFiles(_project.BackupDirectory, BackupManifest.FileName, SearchOption.AllDirectories).Order().Select(File.ReadAllText)];

    [Fact]
    public void Jogo_injetado_movido_troca_a_pasta_e_o_restore_devolve_o_original_so_na_pasta_nova()
    {
        var oldInjected = Inject();
        var novo = CopyGameTo("W-VNs");

        var result = ProjectGameRelocator.Move(_project, novo);

        Assert.True(result.Changed);
        Assert.Equal(1, result.ManifestsUpdated);
        Assert.Equal(novo.Replace('\\', '/'), _project.LoadConfig().Game.Path);
        Assert.Equal(novo, _project.LoadState().GameDirectory);
        Assert.Equal(novo, GameInjector.ListBackups(_project).Single().Manifest!.GameDirectory);
        Assert.Equal(Path.Combine(novo, "jogo.exe"), GameLauncher.ResolveExecutable(_project));
        Assert.Equal(Traduzido, File.ReadAllText(Path.Combine(novo, YsbinYpf)));

        new GameInjector().Restore(GameInjector.PlanRestore(_project));

        Assert.Equal(Original, File.ReadAllText(Path.Combine(novo, YsbinYpf)));
        Assert.Equal(Traduzido, File.ReadAllText(oldInjected));
    }

    [Fact]
    public void Jogo_nao_injetado_tambem_troca_e_repetir_a_troca_nao_grava_nada()
    {
        var novo = CopyGameTo("W-VNs");

        var first = ProjectGameRelocator.Move(_project, novo);
        var before = ProjectFiles();
        var second = ProjectGameRelocator.Move(_project, novo);

        Assert.True(first.Changed);
        Assert.Equal(0, first.ManifestsUpdated);
        Assert.Equal(1, first.CheckedFiles);
        Assert.False(second.Changed);
        Assert.Equal(before, ProjectFiles());
        Assert.Equal(Path.Combine(novo, "jogo.exe"), GameLauncher.ResolveExecutable(_project));
        Assert.Equal(Original, File.ReadAllText(Path.Combine(novo, YsbinYpf)));
    }

    [Theory]
    [InlineData("diferente", "outro jogo")]
    [InlineData("ausente", null)]
    public void Pasta_com_outro_jogo_e_recusada_dizendo_o_arquivo_e_nada_muda_no_projeto(string motivo, string? conteudo)
    {
        Inject();
        var novo = CopyGameTo("W-VNs");
        var arquivo = Path.Combine(novo, YsbinYpf);
        if (conteudo is null)
        {
            File.Delete(arquivo);
        }
        else
        {
            File.WriteAllText(arquivo, conteudo);
        }

        var before = ProjectFiles();

        var erro = Assert.Throws<VNForgeException>(() => ProjectGameRelocator.Move(_project, novo));

        Assert.Contains($"{YsbinYpf} ({motivo})", erro.Message);
        Assert.Contains("nada foi gravado", erro.Message);
        Assert.Equal(before, ProjectFiles());
    }
}
