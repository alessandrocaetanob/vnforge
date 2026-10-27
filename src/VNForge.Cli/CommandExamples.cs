namespace VNForge.Cli;

/// <summary>
/// Os exemplos que a ajuda mostra em "Exemplos:", por caminho do comando (<c>"ypf unpack"</c>; a raiz é <c>""</c>).
/// Cada exemplo começa com <c>vnf</c> e o caminho, não usa aspas e passa pelo parser sem erro (um teste confere).
/// </summary>
public static class CommandExamples
{
    private static readonly Dictionary<string, string[]> Examples = new(StringComparer.Ordinal)
    {
        [string.Empty] =
        [
            @"vnf init minha-vn --game D:\Jogos\MinhaVN",
            "vnf status --project minha-vn",
        ],
        ["init"] = [@"vnf init minha-vn --game D:\Jogos\MinhaVN"],
        ["extract"] = ["vnf extract"],
        ["translate"] = ["vnf translate --dry-run", "vnf translate --files yst001* --model claude-sonnet-5-5"],
        ["review"] = ["vnf review --status translated"],
        ["check"] = ["vnf check --severity error"],
        ["build"] = ["vnf build", "vnf build --report"],
        ["inject"] = ["vnf inject"],
        ["run"] = ["vnf run"],
        ["restore"] = ["vnf restore"],
        ["test"] = ["vnf test", "vnf test --accent-test", "vnf test --only-script yst00012"],
        ["status"] = ["vnf status"],
        ["preview"] = ["vnf preview yst00012:000034"],
        ["export"] = ["vnf export --output revisao.xlsx"],
        ["import"] = ["vnf import revisao.xlsx"],
        ["glossary add"] = ["vnf glossary add 先輩 veterano"],
        ["glossary list"] = ["vnf glossary list"],
        ["chars set"] = ["vnf chars set 美咲 Misaki --gender female"],
        ["chars list"] = ["vnf chars list"],
        ["title show"] = ["vnf title show"],
        ["title set"] = ["vnf title set Minha_VN"],
        ["title clear"] = ["vnf title clear"],
        ["scripts list"] = ["vnf scripts list"],
        ["scripts group"] = ["vnf scripts group yst00010..yst00020 Prologo"],
        ["scripts ungroup"] = ["vnf scripts ungroup yst00010..yst00020"],
        ["scripts rename-group"] = ["vnf scripts rename-group 2 Prologo"],
        ["lines keep"] = ["vnf lines keep yst00012:000034", "vnf lines keep yst00012:000034,yst00012:000035"],
        ["lines unkeep"] = ["vnf lines unkeep yst00012:000034"],
        ["config ai"] = ["vnf config ai --model claude-opus-5-5"],
        ["detect"] = [@"vnf detect --game D:\Jogos\MinhaVN"],
        ["ypf info"] = ["vnf ypf info ysbin.ypf"],
        ["ypf unpack"] = ["vnf ypf unpack ysbin.ypf ysbin"],
        ["ypf pack"] = ["vnf ypf pack ysbin --original ysbin.ypf --output novo.ypf"],
        ["ybn dump"] = ["vnf ybn dump ysbin/yst00001.ybn"],
        ["patch-exe"] = ["vnf patch-exe"],
    };

    /// <summary>Caminhos que têm exemplo.</summary>
    public static IEnumerable<string> Paths => Examples.Keys;

    public static IReadOnlyList<string> For(string path) => Examples.TryGetValue(path, out var found) ? found : [];
}
