using Microsoft.Extensions.Logging;
using VNForge.Core.Logging;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Logging;

public sealed class LogDirectoryTests : IDisposable
{
    private const string ProjectName = "projeto";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _user;

    public LogDirectoryTests()
    {
        _user = Path.Combine(_root, "usuario-logs");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string NewDir(string name, bool withProjectFile = false)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        if (withProjectFile)
        {
            File.WriteAllText(Path.Combine(path, ProjectFolder.ConfigFileName), "{}");
        }

        return path;
    }

    [Fact]
    public void Project_com_vnforge_json_leva_o_log_para_dentro_dele()
    {
        var project = NewDir(ProjectName, withProjectFile: true);
        var cwd = NewDir("qualquer");

        var dir = VNForgeLogging.ResolveLogDirectory(project, cwd, _user);

        Assert.Equal(VNForgeLogging.GetLogDirectory(project), dir);
    }

    [Fact]
    public void Project_relativo_e_resolvido_a_partir_do_diretorio_atual()
    {
        var cwd = NewDir("base");
        NewDir(Path.Combine("base", ProjectName), withProjectFile: true);

        var dir = VNForgeLogging.ResolveLogDirectory(ProjectName, cwd, _user);

        Assert.Equal(VNForgeLogging.GetLogDirectory(Path.Combine(cwd, ProjectName)), dir);
    }

    [Fact]
    public void Sem_project_e_com_vnforge_json_no_diretorio_atual_usa_o_diretorio_atual()
    {
        var cwd = NewDir(ProjectName);
        File.WriteAllText(Path.Combine(cwd, "vnforge.json"), "{}");

        var dir = VNForgeLogging.ResolveLogDirectory(null, cwd, _user);

        Assert.Equal(VNForgeLogging.GetLogDirectory(cwd), dir);
    }

    [Fact]
    public void Sem_project_e_sem_vnforge_json_usa_a_pasta_do_usuario_e_nao_suja_o_diretorio_atual()
    {
        var cwd = NewDir("pasta-do-jogo");

        var dir = VNForgeLogging.ResolveLogDirectory(null, cwd, _user);

        Assert.Equal(_user, dir);
        Assert.False(Directory.Exists(Path.Combine(cwd, ".cache")));
    }

    [Fact]
    public void Project_existente_sem_vnforge_json_usa_a_pasta_do_usuario_e_nao_suja_a_pasta()
    {
        var gameFolder = NewDir("pasta-do-jogo");
        var cwd = NewDir("base", withProjectFile: true);

        var dir = VNForgeLogging.ResolveLogDirectory(gameFolder, cwd, _user);

        Assert.Equal(_user, dir);
        Assert.False(Directory.Exists(Path.Combine(gameFolder, ".cache")));
    }

    [Fact]
    public void Project_inexistente_cai_na_pasta_do_usuario_e_nao_cria_a_pasta()
    {
        var cwd = NewDir("base");
        var missing = Path.Combine(_root, "nao-existe");

        var dir = VNForgeLogging.ResolveLogDirectory(missing, cwd, _user);

        Assert.Equal(_user, dir);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Pasta_do_usuario_padrao_fica_em_LocalApplicationData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VNForge", "logs");

        Assert.Equal(expected, VNForgeLogging.GetUserLogDirectory());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relativo/appdata")]
    public void Pasta_do_usuario_nunca_e_relativa_ao_cwd(string localApplicationData)
    {
        var expected = Path.Combine(Path.GetTempPath(), "VNForge", "logs");

        var dir = VNForgeLogging.GetUserLogDirectory(localApplicationData);

        Assert.Equal(expected, dir);
        Assert.True(Path.IsPathRooted(dir));
    }

    [Fact]
    public void Pasta_do_usuario_usa_LocalApplicationData_quando_absoluto()
    {
        var dir = VNForgeLogging.GetUserLogDirectory(_root);

        Assert.Equal(Path.Combine(_root, "VNForge", "logs"), dir);
    }

    [Fact]
    public void Fabrica_por_diretorio_grava_o_log_no_diretorio_dado()
    {
        var dir = Path.Combine(_root, "logs-diretos");

        using (var factory = VNForgeLogging.CreateFactoryForDirectory(dir))
        {
            factory.CreateLogger("Teste").LogInformation("olá");
        }

        Assert.Contains("olá", File.ReadAllText(Assert.Single(Directory.GetFiles(dir, "*.log"))));
    }
}
