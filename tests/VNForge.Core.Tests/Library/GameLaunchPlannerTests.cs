using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R6")]
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
    public void Jogo_sem_executavel_ou_com_executavel_que_sumiu_ou_fora_da_pasta_e_recusado()
    {
        Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), Game(exe: null)));

        var missing = Game() with { Exe = "Outro.exe" };
        Assert.Contains("não foi encontrado", Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), missing)).Message);

        var outside = Game() with { Exe = @"..\Fora.exe" };
        Assert.Contains("não fica dentro da pasta", Assert.Throws<LibraryException>(() => GameLaunchPlanner.Plan(new GameLibrary(), outside)).Message);
    }
}
