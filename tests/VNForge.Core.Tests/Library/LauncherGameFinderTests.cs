using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class LauncherGameFinderTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Jogos", "Clannad");
    private static readonly string Launcher = Path.Combine(Folder, "Launcher.exe");
    private static readonly string Game = Path.Combine(Folder, "bin", "Game.exe");

    private static IReadOnlyList<int> Find(IEnumerable<RunningProgram> running, params int[] before) =>
        LauncherGameFinder.Find(running, Launcher, Folder, before.ToHashSet());

    [Fact]
    public void O_proprio_lancador_nao_e_o_jogo()
    {
        Assert.Empty(Find([new RunningProgram(10, Launcher)]));
    }

    [Fact]
    public void O_jogo_e_o_programa_novo_da_pasta_que_nao_e_o_lancador()
    {
        Assert.Equal([11], Find([new RunningProgram(10, Launcher), new RunningProgram(11, Game)]));
    }

    [Fact]
    public void Compara_os_caminhos_sem_diferenciar_maiusculas_e_com_as_barras_normalizadas()
    {
        var oddly = Folder.ToUpperInvariant() + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + "Launcher.exe";

        Assert.Empty(Find([new RunningProgram(10, oddly)]));
        Assert.Equal([11], Find([new RunningProgram(11, Game.ToUpperInvariant())]));
    }

    [Fact]
    public void Programa_que_ja_rodava_antes_de_abrir_nao_conta()
    {
        Assert.Empty(Find([new RunningProgram(11, Game)], before: 11));
    }

    [Fact]
    public void Programa_de_fora_da_pasta_ou_de_uma_pasta_de_nome_parecido_nao_conta()
    {
        Assert.Empty(Find([new RunningProgram(11, Path.Combine(Path.GetTempPath(), "Jogos", "Clannad2", "Game.exe")), new RunningProgram(12, Path.Combine(Path.GetTempPath(), "Outro", "Game.exe"))]));
    }

    [Fact]
    public void Processo_sem_caminho_legivel_nao_e_confirmado()
    {
        Assert.Empty(Find([new RunningProgram(11, null), new RunningProgram(12, string.Empty)]));
    }
}
