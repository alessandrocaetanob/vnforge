using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

/// <summary>Os comandos que gravam e não têm teste próprio recusam entrada inválida com código de saída diferente de 0 e uma mensagem em português, sem gravar nada.</summary>
[Trait("Risco", "R2")]
public sealed class WritingCommandsInvalidInputTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string> Commands =>
    [
        "memory remove projeto-que-nao-existe",
        "memory sync --project {pasta-que-nao-e-projeto}",
        "glossary add  traducao --project {projeto}",
        "glossary personal keep termo-que-nao-esta-no-projeto --project {projeto}",
        "glossary personal remove termo-que-nao-esta-no-glossario",
        "library capture remove jogo-que-nao-existe captura.png",
        "library translate jogo-que-nao-existe",
    ];

    private List<string> Snapshot() =>
        [.. Directory.EnumerateFiles(_cli.Root, "*", SearchOption.AllDirectories).Where(f => !f.StartsWith(_cli.UserLogDirectory, StringComparison.OrdinalIgnoreCase) && !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(f => $"{f}:{new FileInfo(f).Length}")];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Entrada_invalida_sai_com_erro_em_portugues_e_nao_grava_nada(string template)
    {
        var project = Path.Combine(_cli.Root, "projeto");
        ProjectFolder.Create(project, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        var notProject = Directory.CreateDirectory(Path.Combine(_cli.Root, "vazia")).FullName;
        var args = template.Split(' ').Select(a => a.Replace("{projeto}", project).Replace("{pasta-que-nao-e-projeto}", notProject)).ToArray();

        var before = Snapshot();

        var result = await _cli.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.StartsWith("Erro: ", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot());
    }
}
