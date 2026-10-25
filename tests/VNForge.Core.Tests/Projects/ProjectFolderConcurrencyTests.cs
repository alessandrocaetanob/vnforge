using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectFolderConcurrencyTests
{
    private const string Key = "yst00001";
    private static readonly string GamePath = Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN");

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig(GamePath, "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static List<TextUnit> Unidades(int quantidade) =>
        Enumerable.Range(1, quantidade)
            .Select(i => new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"今日は{i}\\p", null, TranslationStatus.Untranslated))
            .ToList();

    [Fact]
    public async Task LoadText_e_SaveText_simultaneos_nao_falham_nem_devolvem_arquivo_parcial()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(50));
        var tamanhosValidos = new HashSet<int> { 50, 80 };
        using var parar = new CancellationTokenSource();

        var leitor = Task.Run(() =>
        {
            var leituras = 0;
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadText(Key).Count, tamanhosValidos);
                leituras++;
            }

            return leituras;
        }, TestContext.Current.CancellationToken);

        var leitorDeTudo = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadAllText()[Key].Count, tamanhosValidos);
            }
        }, TestContext.Current.CancellationToken);

        var escritor = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                projeto.SaveText(Key, Unidades(i % 2 == 0 ? 80 : 50));
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            await escritor;
        }
        finally
        {
            await parar.CancelAsync();
        }

        Assert.True(await leitor > 0);
        await leitorDeTudo;
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public async Task SaveText_espera_uma_leitura_externa_que_segura_o_arquivo_por_pouco_tempo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));

        var aberto = new FileStream(projeto.TextPath(Key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var liberar = Task.Run(async () =>
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await aberto.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        projeto.SaveText(Key, Unidades(3));

        await liberar;
        Assert.Equal(3, projeto.LoadText(Key).Count);
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public void SaveText_com_o_arquivo_preso_por_muito_tempo_falha_e_deixa_o_original_sem_temporario()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "No Linux e no macOS o rename troca o arquivo mesmo aberto.");

        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));
        var antes = File.ReadAllBytes(projeto.TextPath(Key));

        using (new FileStream(projeto.TextPath(Key), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var erro = Record.Exception(() => projeto.SaveText(Key, Unidades(3)));
            Assert.True(erro is UnauthorizedAccessException or IOException, $"Esperava falha de acesso ao arquivo, veio {erro?.GetType().Name ?? "nenhuma"}.");
        }

        Assert.Equal(antes, File.ReadAllBytes(projeto.TextPath(Key)));
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public void LoadText_le_o_arquivo_enquanto_outro_leitor_o_mantem_aberto()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));

        using var aberto = new FileStream(projeto.TextPath(Key), FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Equal(2, projeto.LoadText(Key).Count);
    }
}
