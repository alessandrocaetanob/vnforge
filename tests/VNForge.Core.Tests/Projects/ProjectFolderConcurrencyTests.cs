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
        // fazer o mínimo de leituras, para a pausa não reduzir a chance de pegar um arquivo parcial.
        var leitor = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadText(Key).Count, tamanhosValidos);
                Interlocked.Increment(ref leituras);
                Respirar();
            }
        }, TestContext.Current.CancellationToken);

        var leitorDeTudo = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                Assert.Contains(projeto.LoadAllText()[Key].Count, tamanhosValidos);
                Respirar();
            }
        }, TestContext.Current.CancellationToken);

        var escritor = Task.Run(() =>
        {
            for (var i = 0; i < MaximoDeGravacoes && (i < MinimoDeGravacoes || Volatile.Read(ref leituras) < MinimoDeLeituras); i++)
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

        await leitor;
        await leitorDeTudo;
        Assert.True(leituras >= MinimoDeLeituras, $"O leitor fez só {leituras} leituras durante as gravações.");
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public async Task SaveText_espera_uma_leitura_externa_que_segura_o_arquivo_por_pouco_tempo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(Key, Unidades(2));

        using var aberto = new FileStream(projeto.TextPath(Key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var terminou = new ManualResetEventSlim();
        var pendenteAoSoltar = false;

        // O temporário nasce vazio quando o File.WriteAllText cria o arquivo, antes do Move; com conteúdo, o SaveText está a um passo de
        // trocar o arquivo, e é dali que contam os 100 ms de leitura externa. Abrir o temporário para saber se foi fechado atrasou o Move
        // sob carga (29 falhas em 60 rodadas), por isso a checagem olha só o tamanho. A thread é própria (e não uma tarefa do pool)
        // para a liberação não atrasar além da janela de novas tentativas com a máquina carregada.
        var liberar = Task.Factory.StartNew(() =>
        {
            while (!TemporarioGravado(projeto.TextDirectory))
            {
                if (terminou.Wait(1))
                {
                    break;
                }
            }

            terminou.Wait(100);
            pendenteAoSoltar = Directory.EnumerateFiles(projeto.TextDirectory, "*.tmp").Any();
            aberto.Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            projeto.SaveText(Key, Unidades(3));
        }
        finally
        {
            terminou.Set();
            await liberar;
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.True(pendenteAoSoltar, "O arquivo foi solto antes de o SaveText tentar trocá-lo: a nova tentativa não foi exercida.");
        }

        Assert.Equal(3, projeto.LoadText(Key).Count);
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

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

    private static bool TemporarioGravado(string diretorio) =>
        Directory.EnumerateFiles(diretorio, "*.tmp").Any(temporario => new FileInfo(temporario).Length > 0);

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
            var relogio = Stopwatch.StartNew();
            var erro = Record.Exception(() => projeto.SaveText(Key, Unidades(3)));
            relogio.Stop();

            Assert.IsType<UnauthorizedAccessException>(erro);
            // 250 ms fica abaixo dos 300 ms de novas tentativas: separa falhar na hora de esperar a nova tentativa e sobra folga para a máquina carregada.
            Assert.True(relogio.ElapsedMilliseconds < 250, $"A falha permanente esperou {relogio.ElapsedMilliseconds} ms.");
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
