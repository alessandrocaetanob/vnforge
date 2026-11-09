using System.IO.Compression;
using System.Text;
using VNForge.Core.Jmdict;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>
/// Um JMdict pequeno inventado no teste, com o DTD, as entidades e a estrutura do arquivo real (<c>&lt;entry&gt;</c>, <c>&lt;k_ele&gt;</c>, <c>&lt;r_ele&gt;</c>, <c>&lt;sense&gt;</c>).
/// Nenhum texto do dicionário de verdade entra no repositório.
/// </summary>
internal static class SyntheticJmdict
{
    private const string Header = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE JMdict [
        <!ELEMENT JMdict (entry*)>
        <!ENTITY n "noun (common) (futsuumeishi)">
        <!ENTITY vs "noun or participle which takes the aux. verb suru">
        <!ENTITY vs-i "suru verb - included">
        <!ENTITY vi "intransitive verb">
        <!ENTITY vt "transitive verb">
        <!ENTITY v1 "Ichidan verb">
        <!ENTITY v5u "Godan verb with 'u' ending">
        <!ENTITY v5k-s "Godan verb - Iku/Yuku special class">
        <!ENTITY vk "Kuru verb - special class">
        <!ENTITY adj-i "adjective (keiyoushi)">
        <!ENTITY cop "copula">
        ]>
        <!-- JMdict created: 2026-11-09 -->
        <JMdict>

        """;

    private static string Entry(int id, string[] kanji, string[] readings, string pri, params string[] senses)
    {
        var text = new StringBuilder($"<entry>\n<ent_seq>{id}</ent_seq>\n");
        foreach (var k in kanji)
        {
            text.Append($"<k_ele>\n<keb>{k}</keb>\n{(pri.Length > 0 ? pri : string.Empty)}</k_ele>\n");
        }

        foreach (var r in readings)
        {
            text.Append($"<r_ele>\n<reb>{r}</reb>\n</r_ele>\n");
        }

        foreach (var sense in senses)
        {
            text.Append($"<sense>\n{sense}</sense>\n");
        }

        return text.Append("</entry>\n").ToString();
    }

    private static string Sense(string pos, params string[] glosses) =>
        (pos.Length == 0 ? string.Empty : string.Concat(pos.Split(' ').Select(p => $"<pos>&{p};</pos>\n"))) + string.Concat(glosses.Select(g => $"<gloss>{g}</gloss>\n"));

    private const string Common = "<ke_pri>news1</ke_pri>\n<ke_pri>nf15</ke_pri>\n";

    /// <summary>O XML completo do dicionário inventado.</summary>
    public static string Xml()
    {
        var text = new StringBuilder(Header);
        text.Append(Entry(1000010, ["遅刻"], ["ちこく"], Common, Sense("n vs", "lateness", "tardiness"), Sense(string.Empty, "being late (for work, school)")));
        text.Append(Entry(1000020, ["行く", "往く"], ["いく", "ゆく"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf10</ke_pri>\n",
            Sense("v5k-s vi", "to go", "to move (in a direction)"),
            Sense(string.Empty, "to proceed", "to take place"),
            Sense(string.Empty, "to head towards"),
            Sense(string.Empty, "to pass through"),
            Sense(string.Empty, "to get along"),
            Sense(string.Empty, "to turn out"),
            Sense(string.Empty, "to reach"),
            Sense(string.Empty, "to be in a state"),
            Sense(string.Empty, "to keep doing"),
            Sense(string.Empty, "to die")));
        text.Append(Entry(1000030, ["行う"], ["おこなう"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf40</ke_pri>\n", Sense("v5u vt", "to perform", "to carry out")));
        text.Append(Entry(1000040, ["食べる"], ["たべる"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf05</ke_pri>\n", Sense("v1 vt", "to eat")));
        text.Append(Entry(1000050, ["美しい"], ["うつくしい"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf20</ke_pri>\n", Sense("adj-i", "beautiful", "lovely")));
        text.Append(Entry(1000060, ["為る"], ["する"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf01</ke_pri>\n", Sense("vs-i", "to do", "to carry out")));
        text.Append(Entry(1000070, ["来る"], ["くる"], "<ke_pri>ichi1</ke_pri>\n<ke_pri>nf02</ke_pri>\n", Sense("vk vi", "to come")));
        text.Append(Entry(1000080, ["二人"], ["ふたり"], Common, Sense("n", "two people", "pair")));
        text.Append(Entry(1000090, ["五分"], ["ごふん", "ごぶ"], string.Empty, Sense("n", "five minutes"), Sense(string.Empty, "fifty percent")));
        text.Append(Entry(1000100, ["揃う"], ["そろう"], "<ke_pri>news2</ke_pri>\n", Sense("v5u vi", "to be complete", "to be all present")));
        text.Append(Entry(1000110, [], ["だ"], string.Empty, Sense("cop", "be", "is")));
        text.Append(Entry(1000120, ["傘"], ["かさ"], "<ke_pri>news1</ke_pri>\n<ke_pri>nf20</ke_pri>\n", Sense("n", "umbrella")));
        text.Append(Entry(1000130, ["学校"], ["がっこう"], "<ke_pri>news1</ke_pri>\n<ke_pri>nf03</ke_pri>\n", Sense("n", "school")));
        return text.Append("</JMdict>\n").ToString();
    }

    public static byte[] XmlBytes() => new UTF8Encoding(false).GetBytes(Xml());

    public static byte[] GzipBytes()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(XmlBytes());
        }

        return output.ToArray();
    }

    /// <summary>O índice do dicionário inventado, montado em memória.</summary>
    public static JmdictIndex Index()
    {
        using var input = new MemoryStream(XmlBytes());
        using var output = new MemoryStream();
        JmdictImporter.Import(input, output);
        return JmdictIndex.FromBytes(output.ToArray(), "sintético");
    }
}
