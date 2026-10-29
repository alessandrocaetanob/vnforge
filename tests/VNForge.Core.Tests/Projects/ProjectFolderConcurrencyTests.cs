using System.Diagnostics;
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
        const int MinimoDeLeituras = 200;
        const int MinimoDeGravacoes = 300;
        const int MaximoDeGravacoes = 20_000;

        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(50));
        var tamanhosValidos = new HashSet<int> { 50, 80 };
        using var parar = new CancellationTokenSource();
        var leituras = 0;

        // No Windows o escritor só troca o arquivo quando ninguém o segura: leitores em laço fechado o mantêm aberto quase o tempo todo
        // e esgotam a janela de novas tentativas sob carga. Por isso respiram entre as leituras; o escritor segue gravando até o leitor
        // fazer o mínimo de leituras, para a pausa não reduzir a chance de pegar um arquivo parcial. Cada papel tem a própria thread (e não uma
        // tarefa do pool): com o pool ocupado por outros testes, o escritor podia terminar as gravações antes de o leitor ter começado.
        var leitor = EmThreadPropria(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadText(Key).Count, tamanhosValidos);
                Interlocked.Increment(ref leituras);
                Respirar();
            }
        });

        var leitorDeTudo = EmThreadPropria(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadAllText()[Key].Count, tamanhosValidos);
                Respirar();
            }
        });

        var escritor = EmThreadPropria(() =>
        {
            for (var i = 0; i < MaximoDeGravacoes && (i < MinimoDeGravacoes || Volatile.Read(ref leituras) < MinimoDeLeituras); i++)
            {
                projeto.SaveText(Key, Unidades(i % 2 == 0 ? 80 : 50));
            }
        });

        try
        {
            await escritor;
        }
        finally
        {
            await parar.CancelAsync();
        }

        await leitor;
        await leitorDeTudo;
        Assert.True(leituras >= MinimoDeLeituras, $"O leitor fez só {leituras} leituras durante as gravações.");
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public void Move_espera_a_leitura_externa_e_conclui_quando_a_nova_tentativa_solta_o_arquivo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));
        var destino = projeto.TextPath(Key);
        var origem = destino + ".tmp";
        File.WriteAllText(origem, "novo");

        var aberto = new FileStream(destino, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var esperas = new List<int>();

        // A leitura externa termina quando a primeira nova tentativa pede a espera: o arquivo é solto por sinal da própria nova tentativa,
        // sem thread paralela nem relógio, então a máquina carregada não muda o resultado.
        try
        {
            ProjectFileIo.Move(origem, destino, overwrite: true, wait: milissegundos =>
            {
                esperas.Add(milissegundos);
                aberto.Dispose();
            });
        }
        finally
        {
            aberto.Dispose();
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal([5], esperas);
        }

        Assert.Equal("novo", File.ReadAllText(destino));
        Assert.False(File.Exists(origem));
    }

    private static Task EmThreadPropria(Action trabalho) =>
        Task.Factory.StartNew(trabalho, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Uma espera de 1 ms no Windows dura cerca de 15 ms e deixaria os leitores com poucas dezenas de leituras; um giro curto
    // deixa o arquivo livre entre as leituras sem tirar a concorrência com o escritor.
    private static void Respirar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inicio = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(inicio) < TimeSpan.FromMicroseconds(500))
        {
            Thread.SpinWait(20);
        }
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
            var erro = Record.Exception(() => projeto.SaveText(Key, Unidades(3)));

            Assert.IsType<UnauthorizedAccessException>(erro);
        }
        finally
        {
            File.SetAttributes(caminho, FileAttributes.Normal);
        }

        Assert.Equal(antes, File.ReadAllBytes(caminho));
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    // Contar as tentativas separa "falhou na hora" de "esperou a nova tentativa" sem olhar o relógio, que oscila com a máquina carregada.
    [Fact]
    public void Acesso_negado_em_destino_somente_leitura_nao_e_repetido()
    {
        using var temp = new TempFolder();
        var caminho = Path.Combine(temp.Path, "somente-leitura.json");
        File.WriteAllText(caminho, "{}");
        File.SetAttributes(caminho, FileAttributes.ReadOnly);
        var tentativas = 0;

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => ProjectFileIo.Retry<int>(caminho, () =>
            {
                tentativas++;
                throw new UnauthorizedAccessException();
            }));
        }
        finally
        {
            File.SetAttributes(caminho, FileAttributes.Normal);
        }

        Assert.Equal(1, tentativas);
    }

    [Fact]
    public void Falha_de_IO_que_nunca_passa_para_depois_de_8_tentativas()
    {
        var tentativas = 0;

        Assert.Throws<IOException>(() => ProjectFileIo.Retry<int>(null, () =>
        {
            tentativas++;
            throw new IOException("preso");
        }));

        Assert.Equal(8, tentativas);
    }

    [Fact]
    public void Acesso_negado_transitorio_e_repetido_mesmo_quando_a_espera_estoura_o_relogio()
    {
        var tentativas = 0;

        var resultado = ProjectFileIo.Retry(null, () =>
        {
            tentativas++;
            if (tentativas == 1)
            {
                // Uma thread sem CPU volta tarde: o orçamento conta as esperas pedidas, e não o relógio, para ela não perder as tentativas.
                // A espera real é o próprio cenário do teste, não uma sincronização.
#pragma warning disable S2925
                Thread.Sleep(320);
#pragma warning restore S2925
                throw new UnauthorizedAccessException();
            }

            return tentativas;
        });

        Assert.Equal(2, resultado);
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
