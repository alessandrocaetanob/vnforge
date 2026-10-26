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

        // Os leitores respiram 1 ms entre as leituras: em laço fechado, dois leitores mantêm o arquivo aberto quase o tempo todo e o
        // escritor, que no Windows só troca o arquivo quando ninguém o segura, esgota a janela de novas tentativas sob carga.
        var leitor = Task.Run(() =>
        {
            var leituras = 0;
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadText(Key).Count, tamanhosValidos);
                leituras++;
                Thread.Sleep(1);
            }

            return leituras;
        }, TestContext.Current.CancellationToken);

        var leitorDeTudo = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadAllText()[Key].Count, tamanhosValidos);
                Thread.Sleep(1);
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
    public void SaveText_espera_uma_leitura_externa_que_segura_o_arquivo_por_pouco_tempo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));

        var aberto = new FileStream(projeto.TextPath(Key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var gravou = false;
        var liberar = new Thread(() =>
        {
            // Solta o arquivo 100 ms depois que o SaveText gravou o temporário e entrou no Move. Contar a espera a partir daí, numa
            // thread própria (e não numa tarefa do pool), mantém a liberação dentro da janela de novas tentativas mesmo com a máquina carregada.
            while (!Volatile.Read(ref gravou) && !Directory.EnumerateFiles(projeto.TextDirectory, "*.tmp").Any())
            {
                Thread.Sleep(1);
            }

            if (!Volatile.Read(ref gravou))
            {
                Thread.Sleep(100);
            }

            aberto.Dispose();
        })
        {
            IsBackground = true,
        };
        liberar.Start();

        try
        {
            projeto.SaveText(Key, Unidades(3));
        }
        finally
        {
            Volatile.Write(ref gravou, true);
            liberar.Join();
        }

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
    public void SaveText_com_destino_somente_leitura_falha_na_hora_e_deixa_o_original()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "No Linux e no macOS o rename troca o arquivo somente leitura.");

        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));
        var caminho = projeto.TextPath(Key);
        var antes = File.ReadAllBytes(caminho);
        File.SetAttributes(caminho, FileAttributes.ReadOnly);

        try
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            var erro = Record.Exception(() => projeto.SaveText(Key, Unidades(3)));
            relogio.Stop();

            Assert.IsType<UnauthorizedAccessException>(erro);
            Assert.True(relogio.ElapsedMilliseconds < 150, $"A falha permanente esperou {relogio.ElapsedMilliseconds} ms.");
        }
        finally
        {
            File.SetAttributes(caminho, FileAttributes.Normal);
        }

        Assert.Equal(antes, File.ReadAllBytes(caminho));
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
