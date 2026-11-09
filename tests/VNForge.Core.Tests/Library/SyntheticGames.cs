using VNForge.Core.Engines;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

/// <summary>
/// Monta em código uma pasta de coleção com jogos de várias engines, sem nenhum arquivo de jogo de verdade: só nomes de arquivo, que é o que o reconhecimento lê.
/// </summary>
internal static class SyntheticGames
{
    public const string MarkerFile = "marcador.engine";

    public static void File(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
    }

    /// <summary>A coleção: 6 jogos reconhecíveis em profundidades diferentes e pastas que não são jogo.</summary>
    public static string Collection(string root)
    {
        // KiriKiri: pacote .xp3 na raiz do jogo, com marcas de coleção no nome da pasta.
        File(Path.Combine(root, "[Key] CLANNAD v1.02", "data.xp3"));
        File(Path.Combine(root, "[Key] CLANNAD v1.02", "CLANNAD.exe"));
        File(Path.Combine(root, "[Key] CLANNAD v1.02", "unins000.exe"));

        // Siglus: Scene.pck.
        File(Path.Combine(root, "Steins;Gate", "Scene.pck"));
        File(Path.Combine(root, "Steins;Gate", "SG.exe"));

        // Ren'Py: pasta renpy, e o jogo dentro de uma pasta de agrupamento (profundidade 2).
        File(Path.Combine(root, "Ocidentais", "Eden Fall", "renpy", "common.rpa"));
        File(Path.Combine(root, "Ocidentais", "Eden Fall", "game", "script.rpa"));
        File(Path.Combine(root, "Ocidentais", "Eden Fall", "Eden Fall.exe"));

        // RPG Maker MV: título no System.json.
        File(Path.Combine(root, "RPG", "Tale", "www", "js", "rpg_core.js"));
        File(Path.Combine(root, "RPG", "Tale", "www", "data", "System.json"), "{\"gameTitle\":\"Moonlit Tale\"}");
        File(Path.Combine(root, "RPG", "Tale", "Game.exe"));

        // Uma engine registrada (a de mentira dos testes), reconhecida pelo Detect e não por assinatura.
        File(Path.Combine(root, "Yuris", "Sakura no Uta", MarkerFile));
        File(Path.Combine(root, "Yuris", "Sakura no Uta", "SakuraNoUta.exe"));

        // O que não é jogo ou não deve ser percorrido.
        File(Path.Combine(root, "Windows", "Sistema", "data.xp3"));
        File(Path.Combine(root, "node_modules", "pacote", "Scene.pck"));
        File(Path.Combine(root, "$RECYCLE.BIN", "lixo", "data.xp3"));
        File(Path.Combine(root, "Anotações", "notas.txt"));
        File(Path.Combine(root, "Programas", "Ferramenta", "Ferramenta.exe"));
        return root;
    }

    public static EngineRecognizer Recognizer() => new([new MarkerEngine()]);

    public static LibraryScanner Scanner(IGameWindowTitleReader? titles = null) => new(Recognizer(), titles);

    public static VndbVisualNovel Vn(
        string id,
        string title,
        string? alt = null,
        string[]? aliases = null,
        VndbImage? image = null,
        IReadOnlyList<VndbTag>? tags = null,
        string? description = null) =>
        new(id, title, alt, [], aliases ?? [], "ja", 0, "2008-04-24", ["ja"], ["win"], image, 4, 3000, 10, description, tags ?? []);

    /// <summary>Engine de mentira registrada: reconhece a pasta que tem o <see cref="MarkerFile"/>.</summary>
    internal sealed class MarkerEngine : IEngine
    {
        public string Id => "marker";

        public string DisplayName => "Marcador";

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public EngineDetection Detect(string gameDir) =>
            System.IO.File.Exists(Path.Combine(gameDir, MarkerFile))
                ? EngineDetection.Matched("marcador reconhecido")
                : EngineDetection.NotDetected;
    }

    internal sealed class FixedTitles(string? title) : IGameWindowTitleReader
    {
        public string? Read(string gameFolder, string? exe) => title;
    }
}
