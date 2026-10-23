using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public sealed class PipelineStateTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private ProjectFolder NewProject() =>
        ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));

    private static PipelineState Sample() =>
        new(
            [new OriginalFile("pac/ysbin.ypf", 12, "ab12"), new OriginalFile("a.ypf", 3, "cd34")],
            new Dictionary<string, StageState> { ["extract"] = new(StageStatus.Completed) });

    [Fact]
    public void Sem_state_json_o_estado_e_vazio()
    {
        var project = NewProject();

        var state = project.LoadState();

        Assert.Empty(state.Originals);
        Assert.Empty(state.Stages);
        Assert.Equal(Path.Combine(project.Root, ".cache", "state.json"), project.StatePath);
        Assert.False(File.Exists(project.StatePath));
    }

    [Fact]
    public void Ida_e_volta_preserva_o_estado_e_ordena_os_originais_pelo_caminho()
    {
        var project = NewProject();

        project.SaveState(Sample());
        var loaded = project.LoadState();

        Assert.Equal(["a.ypf", "pac/ysbin.ypf"], loaded.Originals.Select(o => o.Path));
        Assert.Equal(new OriginalFile("pac/ysbin.ypf", 12, "ab12"), loaded.Originals[1]);
        Assert.Equal(StageStatus.Completed, loaded.Stages["extract"].Status);
    }

    [Fact]
    public void Gravar_o_mesmo_estado_duas_vezes_nao_altera_o_arquivo()
    {
        var project = NewProject();
        project.SaveState(Sample());
        var bytes = File.ReadAllBytes(project.StatePath);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(project.StatePath, stamp);

        project.SaveState(Sample());

        Assert.Equal(bytes, File.ReadAllBytes(project.StatePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(project.StatePath));
        Assert.Empty(Directory.GetFiles(project.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public void Estado_e_gravado_em_utf8_sem_bom_e_com_lf()
    {
        var project = NewProject();

        project.SaveState(Sample());

        var bytes = File.ReadAllBytes(project.StatePath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Arquivo_malformado_e_erro_que_cita_o_state_json()
    {
        var project = NewProject();
        File.WriteAllText(project.StatePath, "{ nao e json");

        var ex = Assert.Throws<VNForgeException>(() => project.LoadState());

        Assert.Contains("state.json", ex.Message);
    }

    [Fact]
    public void Versao_desconhecida_e_erro()
    {
        var project = NewProject();
        File.WriteAllText(project.StatePath, """{"version":99,"originals":[],"stages":{}}""");

        var ex = Assert.Throws<VNForgeException>(() => project.LoadState());

        Assert.Contains("state.json", ex.Message);
    }
}
