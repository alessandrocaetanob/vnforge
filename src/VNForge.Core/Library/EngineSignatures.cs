namespace VNForge.Core.Library;

/// <summary>
/// Assinaturas de arquivo das engines que o app ainda não traduz: <c>.xp3</c> do KiriKiri, <c>Scene.pck</c> do Siglus, <c>.rpa</c> e a pasta <c>renpy</c> do Ren'Py
/// e outras óbvias. Só olha os nomes de arquivo da pasta e de algumas subpastas de pacote; nunca abre o jogo.
/// </summary>
public static class EngineSignatures
{
    /// <summary>Subpastas em que os pacotes costumam ficar, além da raiz.</summary>
    private static readonly string[] PackageFolders = ["data", "game", "resource", "resources"];

    private static readonly Signature[] All =
    [
        new("siglus", "Siglus", p => p.HasFile("Scene.pck") || p.HasFile("Scene.pck", "data")),
        new("reallive", "RealLive", p => p.HasFile("Seen.txt") || p.HasFile("Seen.txt", "data")),
        new("kirikiri", "KiriKiri", p => p.HasExtension(".xp3") || p.HasFile("krkr.eXe") || (p.HasDirectory("plugin") && p.HasExtension(".tjs"))),
        new("renpy", "Ren'Py", p => p.HasDirectory("renpy") || p.HasExtension(".rpa") || p.HasExtension(".rpyc") || p.HasExtension(".rpy")),
        new("rpgmaker", "RPG Maker", p =>
            p.HasExtension(".rgss3a") || p.HasExtension(".rgss2a") || p.HasExtension(".rgssad")
            || p.HasPath("www/js/rpg_core.js") || p.HasPath("js/rpg_core.js") || p.HasPath("js/rmmz_core.js") || p.HasPath("www/js/rmmz_core.js")
            || p.HasPath("Data/System.rvdata2") || p.HasPath("Data/System.rvdata") || p.HasPath("Data/System.rxdata")),
        new("wolf", "Wolf RPG", p => p.HasFile("Data.wolf") || p.HasPath("Data/BasicData/Game.dat")),
        new("nscripter", "NScripter", p => p.HasFile("nscript.dat") || p.HasFile("nscr_sec.dat") || p.HasFile("arc.nsa") || p.HasFile("onscript.nt2")),
        new("tyrano", "TyranoScript", p => p.HasPath("resources/app/tyrano/tyrano.js") || p.HasPath("tyrano/tyrano.js") || (p.HasPath("data/system/Config.tjs") && p.HasDirectory("tyrano"))),
        new("artemis", "Artemis", p => p.HasExtension(".pfs") || p.HasExtension(".pfs.000")),
        new("unity", "Unity", p => p.HasFile("UnityPlayer.dll") || p.HasDirectoryEndingWith("_Data", "globalgamemanagers")),
    ];

    /// <summary>Os ids das assinaturas, na ordem em que são tentadas.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. All.Select(s => s.Id)];

    /// <summary>A primeira engine cuja assinatura aparece na pasta, ou nulo. Pasta ilegível vale como sem assinatura.</summary>
    public static LibraryEngine? Match(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var probe = new FolderProbe(folder);
        return All.Where(signature => probe.Safely(signature.Matches))
            .Select(signature => new LibraryEngine(signature.Id, signature.Name, LibraryEngineSource.Signature))
            .FirstOrDefault();
    }

    private sealed record Signature(string Id, string Name, Func<FolderProbe, bool> Matches);

    /// <summary>Os nomes de arquivo e subpasta da raiz do jogo, lidos uma vez, mais as pastas de pacote que a assinatura pedir.</summary>
    private sealed class FolderProbe(string folder)
    {
        private readonly Dictionary<string, string[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lazy<string[]> _directories = new(() => [.. Directory.EnumerateDirectories(folder).Select(d => Path.GetFileName(d))]);

        public bool Safely(Func<FolderProbe, bool> test)
        {
            try
            {
                return test(this);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool HasFile(string name, string? subfolder = null) =>
            FilesIn(subfolder).Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

        public bool HasExtension(string extension) =>
            FilesIn(null).Concat(PackageFolders.SelectMany(FilesIn)).Any(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        public bool HasDirectory(string name) => _directories.Value.Contains(name, StringComparer.OrdinalIgnoreCase);

        public bool HasPath(string relative)
        {
            var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) || Directory.Exists(path);
        }

        public bool HasDirectoryEndingWith(string suffix, string file) =>
            _directories.Value.Any(d => d.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(folder, d, file)));

        private string[] FilesIn(string? subfolder)
        {
            var key = subfolder ?? string.Empty;
            if (_files.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var directory = subfolder is null ? folder : Path.Combine(folder, subfolder);
            string[] names;
            try
            {
                names = Directory.Exists(directory)
                    ? [.. Directory.EnumerateFiles(directory).Select(f => Path.GetFileName(f))]
                    : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                names = [];
            }

            _files[key] = names;
            return names;
        }
    }
}
