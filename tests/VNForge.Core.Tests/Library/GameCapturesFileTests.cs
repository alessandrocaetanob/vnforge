using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R2")]
public sealed class GameCapturesFileTests : IDisposable
{
    // 09/11/2026 23:30 UTC é 20:30 em Brasília (-3): o nome do arquivo usa a hora de quem capturou.
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 23, 30, 15, TimeSpan.Zero);

    private readonly TempFolder _dir = new();
    private readonly FakeTimeProvider _clock = NewClock();

    public void Dispose() => _dir.Dispose();

    private static FakeTimeProvider NewClock()
    {
        var clock = new FakeTimeProvider(Start);
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"));
        return clock;
    }

    private GameCapturesFile NewFile() => new(_dir.Path, _clock);

    private static WindowImage Solid(byte red, byte green, byte blue, int width = 4, int height = 3)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2]) = (blue, green, red);
        }

        return new WindowImage(width, height, bgra);
    }

    // Mil arquivos novos em sequência: o antivírus do Windows às vezes segura um deles e a criação falha por instantes com acesso negado.
    private static void CreateEmpty(string path)
    {
        Exception? last = null;
        var created = SpinWait.SpinUntil(() =>
        {
            try
            {
                File.WriteAllBytes(path, []);
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                last = e;
                return false;
            }
        }, TimeSpan.FromSeconds(10));

        Assert.True(created, $"O arquivo {path} não pôde ser criado em 10 s: {last?.Message}");
    }

    [Fact]
    public void Indice_ilegivel_e_recusado_e_nenhuma_imagem_sobra()
    {
        var file = NewFile();
        Directory.CreateDirectory(_dir.Path);
        File.WriteAllText(file.Path, "{ quebrado");

        var error = Assert.Throws<LibraryException>(() => file.Save("g1", Solid(1, 1, 1)));

        Assert.Contains(file.Path, error.Message);
        Assert.Equal("{ quebrado", File.ReadAllText(file.Path));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "g1")));
    }
}
