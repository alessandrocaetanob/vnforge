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
