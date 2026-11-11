using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class GameLaunchPlannerTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    // Arquivos vazios: o lançador dos testes é falso, nenhum deles é executado.
    private string Folder => Path.Combine(_dir.Path, "Jogos", "Clannad");

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_dir.Path, .. parts]);
        SyntheticGames.File(path);
        return path;
    }

    private LibraryGame Game(string? exe = "Clannad.exe", bool japanese = false)
    {
        if (exe is not null)
        {
            Touch("Jogos", "Clannad", exe);
        }

        return new LibraryGame(GameLibrary.IdFor(Folder), Folder, exe, LibraryEngine.Unknown, "CLANNAD", null, DateTimeOffset.UnixEpoch) { RequiresJapaneseLocale = japanese };
    }

    [Fact]
    public void Caminho_do_Locale_Emulator_pode_ser_a_pasta_e_pode_vir_entre_aspas()
    {
        Touch("LE", "LEProc.exe");

        var launch = GameLaunchPlanner.Plan(new GameLibrary(localeEmulatorPath: $"\"{Path.Combine(_dir.Path, "LE")}\""), Game(japanese: true));

        Assert.Equal(Path.Combine(_dir.Path, "LE", "LEProc.exe"), launch.Executable);
    }

    [Fact]
    public void Pedido_na_chamada_vale_mais_que_a_opcao_do_jogo_nos_dois_sentidos()
    {
        var library = new GameLibrary(localeEmulatorPath: Touch("LE", "LEProc.exe"));

        Assert.True(GameLaunchPlanner.Plan(library, Game(), japaneseLocale: true).ViaLocaleEmulator);
        Assert.False(GameLaunchPlanner.Plan(library, Game(japanese: true), japaneseLocale: false).ViaLocaleEmulator);
    }

    [Fact]
    public void Sem_Locale_Emulator_configurado_o_erro_diz_o_comando()
    {
        var error = Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), Game(japanese: true)));

        Assert.Contains("vnf library emulator", error.Message);
    }

    [Fact]
    public void Locale_Emulator_que_nao_existe_mais_e_recusado()
    {
        var library = new GameLibrary(localeEmulatorPath: Path.Combine(_dir.Path, "sumiu"));

        var error = Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(library, Game(japanese: true)));

        Assert.Contains("não foi encontrado", error.Message);
    }

    [Fact]
    public void Jogo_sem_executavel_ou_com_executavel_que_sumiu_ou_fora_da_pasta_e_recusado()
    {
        Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), Game(exe: null)));

        var missing = Game() with { Exe = "Outro.exe" };
        Assert.Contains("não foi encontrado", Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), missing)).Message);

        var outside = Game() with { Exe = @"..\Fora.exe" };
        Assert.Contains("não fica dentro da pasta", Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), outside)).Message);
    }
}
