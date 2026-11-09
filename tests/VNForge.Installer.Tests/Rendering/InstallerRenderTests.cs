using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.Installer.ViewModels;
using VNForge.Installer.Views;
using VNForge.Patching;

namespace VNForge.Installer.Tests.Rendering;

/// <summary>
/// As cinco janelas do quadro #540 a 720×480 nos dois temas. Cada uma confere, na ordem em que o jogador lê, todos os textos e botões do quadro;
/// os PNG seguem <c>VNFORGE_RENDER_DIR</c> (senão uma pasta temporária) e se conferem lado a lado com a captura do quadro.
/// </summary>
public sealed class InstallerRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public const string RenderVariable = "VNFORGE_RENDER_DIR";

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static string RenderDirectory =>
        Environment.GetEnvironmentVariable(RenderVariable) is { Length: > 0 } configured ? configured : Path.Combine(Path.GetTempPath(), "vnforge-installer-tests", "render");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Sep => Path.DirectorySeparatorChar.ToString();

    private static async Task<InstallerViewModel> FolderAsync(BoardEngine engine, FakeTimeProvider time)
    {
        var vm = new InstallerViewModel(engine, new NoShell(), new InstallerStart(null, Path.GetTempPath(), null), time);
        await vm.StartAsync();
        // Só o texto da pasta depende do disco da máquina de teste; o quadro mostra a pasta do jogo dele.
        vm.FolderPath = BoardEngine.Folder;
        return vm;
    }

    private static async Task<InstallerViewModel> StartedAtAsync(BoardEngine engine, FakeTimeProvider time)
    {
        var vm = new InstallerViewModel(engine, new NoShell(), new InstallerStart(BoardEngine.Folder, Path.GetTempPath(), null), time);
        await vm.StartAsync();
        return vm;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_1_pasta_do_jogo(string theme)
    {
        var vm = await FolderAsync(new BoardEngine(), new FakeTimeProvider());

        await RenderAsync(vm, "540-inst-0", theme, (window, texts) =>
        {
            Assert.Equal(
                [
                    "Tradução PT-BR · Amaoto no Hakoniwa",
                    "Amaoto no Hakoniwa",
                    "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                    "Instalar a tradução",
                    "Escolha a pasta onde o jogo está instalado. O instalador procurou e achou esta.",
                    BoardEngine.Folder,
                    "Procurar",
                    "Versão do jogo conferida",
                    "Game.exe confere com a versão esperada (SHA-256 3f9a…c21d).",
                    "Antes de mexer no jogo, o instalador guarda uma cópia dos arquivos originais.",
                    "Cancelar",
                    "Instalar",
                ],
                texts);
            Assert.Equal(["Procurar", "Cancelar", "Instalar"], EnabledButtons(window));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_2_instalando_com_63_por_cento(string theme)
    {
        var time = new FakeTimeProvider();
        var engine = new BoardEngine(time) { PauseAt = p => p is { Phase: PatchPhase.Apply, Done: 2, Path: not null } };
        var vm = await FolderAsync(engine, time);
        var install = vm.InstallCommand.ExecuteAsync(null);
        await Task.Run(() => engine.Reached.Wait(TimeSpan.FromMinutes(3)), Ct);
        try
        {
            await RenderAsync(vm, "540-inst-1", theme, (window, texts) =>
            {
                Assert.Equal(
                    [
                        "Tradução PT-BR · Amaoto no Hakoniwa",
                        "Amaoto no Hakoniwa",
                        "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                        "Instalando",
                        "Não feche o instalador. Leva cerca de 20 segundos.",
                        "Conferir a versão do jogo",
                        "0,2 s",
                        "Guardar a cópia dos originais",
                        "4 arquivos · 218 MB",
                        "Aplicar a tradução",
                        "63%",
                        "Conferir o resultado",
                        "Cancelar",
                    ],
                    texts);
                Assert.Empty(EnabledButtons(window));
                var bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.InRange(bar.Value, 62.5, 63.5);
                window.Close();
                Assert.True(window.IsVisible, "a janela não fecha com o instalador trabalhando");
            });
        }
        finally
        {
            engine.Release.Set();
            await install;
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_3_concluido(string theme)
    {
        var vm = await FolderAsync(new BoardEngine(), new FakeTimeProvider());
        await vm.InstallCommand.ExecuteAsync(null);

        await RenderAsync(vm, "540-inst-2", theme, (window, texts) =>
        {
            Assert.Equal(
                [
                    "Tradução PT-BR · Amaoto no Hakoniwa",
                    "Amaoto no Hakoniwa",
                    "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                    "Tradução instalada",
                    "Pronto. Abra o jogo normalmente.",
                    $"4 arquivos alterados. Os originais estão guardados em Amaoto no Hakoniwa{Sep}_originais.",
                    "Para voltar ao jogo original, execute este instalador de novo e escolha Voltar ao jogo original.",
                    "Ler o leia-me",
                    "Fechar",
                    "Abrir o jogo",
                ],
                texts);
            Assert.Equal(["Ler o leia-me", "Fechar", "Abrir o jogo"], EnabledButtons(window));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_4_versao_errada(string theme)
    {
        var vm = await StartedAtAsync(new BoardEngine { State = BoardEngine.Situation.WrongVersion }, new FakeTimeProvider());

        await RenderAsync(vm, "540-inst-3", theme, (window, texts) =>
        {
            Assert.Equal(
                [
                    "Tradução PT-BR · Amaoto no Hakoniwa",
                    "Amaoto no Hakoniwa",
                    "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                    "Esta não é a versão esperada",
                    "O Game.exe desta pasta é diferente",
                    "Encontrado: SHA-256 77b0…e04a. Esperado: 3f9a…c21d. Nada foi alterado.",
                    "A tradução 1.2.0 foi feita para outra versão do jogo. Procure uma pasta com a versão certa, ou veja no leia-me qual é e onde conseguir.",
                    "Sair",
                    "Escolher outra pasta",
                ],
                texts);
            Assert.Equal(["Sair", "Escolher outra pasta"], EnabledButtons(window));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_5_ja_instalada(string theme)
    {
        var time = new FakeTimeProvider();
        var vm = await StartedAtAsync(new BoardEngine(time) { State = BoardEngine.Situation.Installed }, time);

        await RenderAsync(vm, "540-inst-4", theme, (window, texts) =>
        {
            Assert.Equal(
                [
                    "Tradução PT-BR · Amaoto no Hakoniwa",
                    "Amaoto no Hakoniwa",
                    "Tradução para português do Brasil Versão 1.2.0 · Equipe Chuva Mansa",
                    "A tradução 1.2.0 já está instalada",
                    "Os arquivos originais, guardados em 08/11 às 20:03, estão inteiros. Voltar ao original apaga a tradução do jogo e não mexe nos seus saves.",
                    "4 arquivos para devolver",
                    "script.ypf, Game.exe, font.ypf e system.ypf voltam byte a byte.",
                    "Fechar",
                    "Voltar ao jogo original",
                ],
                texts);
            Assert.Equal(["Fechar", "Voltar ao jogo original"], EnabledButtons(window));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_as_janelas_que_o_quadro_deixou_como_estados_que_faltam(string theme)
    {
        var errorVm = new InstallerViewModel(new FailingEngine(new BoardEngine()), new NoShell(), new InstallerStart(BoardEngine.Folder, Path.GetTempPath(), null));
        await errorVm.StartAsync();
        await errorVm.InstallCommand.ExecuteAsync(null);
        var empty = new InstallerViewModel(null, new NoShell(), new InstallerStart(null, Path.GetTempPath(), null));
        var reverted = await StartedAtAsync(new BoardEngine { State = BoardEngine.Situation.Installed }, new FakeTimeProvider());
        await reverted.RevertCommand.ExecuteAsync(null);

        await RenderAsync(errorVm, "540-inst-erro", theme, (_, texts) =>
        {
            Assert.Contains("A instalação não terminou", texts);
            Assert.Contains("Não foi possível instalar a tradução", texts);
            Assert.Contains("Tentar de novo", texts);
        });
        await RenderAsync(empty, "540-inst-vazio", theme, (_, texts) => Assert.Contains("Este instalador está vazio", texts));
        await RenderAsync(reverted, "540-inst-original", theme, (_, texts) =>
        {
            Assert.Contains("Jogo original de volta", texts);
            Assert.Contains("4 arquivos voltaram ao original, byte a byte.", texts);
        });
    }

    private static string[] EnabledButtons(Window window) =>
    [
        .. VisibleButtons(window).Where(b => b.IsEffectivelyEnabled).Select(ButtonText),
    ];

    private static IEnumerable<Button> VisibleButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Height > 0)
            .OrderBy(b => b.TranslatePoint(default, window)!.Value.Y).ThenBy(b => b.TranslatePoint(default, window)!.Value.X);

    private static string ButtonText(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().First().Text ?? string.Empty;

    /// <summary>
    /// Os textos visíveis, na ordem de leitura: a barra de título, a coluna da esquerda de cima para baixo e a coluna principal por linhas (da esquerda para a direita
    /// dentro da linha). O texto da capa é arte, não texto da janela, e fica de fora.
    /// </summary>
    private static List<string> ReadingOrder(Window window)
    {
        var items = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.Name != "CoverTitle")
            .Select(t => (Block: t, Point: t.TranslatePoint(default, window)!.Value))
            .ToList();
        var result = new List<string>();
        result.AddRange(items.Where(i => i.Point.Y < 32).OrderBy(i => i.Point.X).Select(i => Normalize(i.Block.Text!)));
        result.AddRange(items.Where(i => i.Point.Y >= 32 && i.Point.X < 200).OrderBy(i => i.Point.Y).Select(i => Normalize(i.Block.Text!)));
        var main = items.Where(i => i.Point.Y >= 32 && i.Point.X >= 200).OrderBy(i => i.Point.Y).ToList();
        var rows = new List<List<(TextBlock Block, Point Point)>>();
        foreach (var item in main)
        {
            if (rows.Count > 0 && item.Point.Y - rows[^1][0].Point.Y < 12)
            {
                rows[^1].Add(item);
            }
            else
            {
                rows.Add([item]);
            }
        }

        result.AddRange(rows.SelectMany(r => r.OrderBy(i => i.Point.X)).Select(i => Normalize(i.Block.Text!)));
        return result;
    }

    private static string Normalize(string text) => text.Replace("\n", " ", StringComparison.Ordinal);

    private async Task RenderAsync(InstallerViewModel vm, string name, string themeName, Action<Window, List<string>> inspect)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var path = Path.Combine(RenderDirectory, $"{name}-{(theme == ThemeVariant.Light ? "claro" : "escuro")}.png");
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new InstallerWindow { DataContext = vm, RequestedThemeVariant = theme };
                try
                {
                    window.Show();
                    Settle(window);
                    Assert.Equal(new Size(720, 480), window.ClientSize);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("O Avalonia.Headless não devolveu o quadro; confira o UseSkia na sessão."))
                    {
                        frame.Save(path, PngBitmapEncoderOptions.Default);
                    }

                    inspect(window, ReadingOrder(window));
                }
                finally
                {
                    // Com o instalador trabalhando em segundo plano, a janela ligada ao modelo seguiria mudando depois do render; sem o modelo ela fecha.
                    window.DataContext = null;
                    window.Close();
                }

                return 0;
            },
            Ct);
        Assert.True(new FileInfo(path).Length > 10_000);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class NoShell : IInstallerShell
    {
        // A pasta do quadro não existe nesta máquina: o executável é o de onde o jogador joga.
        public string? FindGame(string folder, PatchManifest manifest) => Path.Combine(folder, "Game.exe");

        public void OpenGame(string executablePath)
        {
        }

        public void OpenReadMe(string text)
        {
        }
    }

    /// <summary>O quadro do estado "A instalação não terminou": o engine do quadro que falha ao instalar, como um disco cheio.</summary>
    private sealed class FailingEngine(BoardEngine inner) : IInstallerEngine
    {
        public BoardEngine Inner { get; } = inner;

        public PatchManifest Manifest => Inner.Manifest;

        public PatchCheck Check(string folder) => Inner.Check(folder);

        public PatchUninstallPlan PlanUninstall(string folder) => Inner.PlanUninstall(folder);

        public PatchInstallResult Install(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) =>
            throw new PatchException("Não foi possível aplicar o patch em Game.exe: não há espaço no disco. Os arquivos do jogo voltaram ao estado de antes desta instalação.");

        public PatchUninstallResult Uninstall(string folder, IProgress<PatchProgress> progress, CancellationToken cancellationToken) => Inner.Uninstall(folder, progress, cancellationToken);
    }
}
