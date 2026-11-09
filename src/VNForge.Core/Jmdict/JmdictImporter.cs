using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VNForge.Core.Jmdict;

/// <summary>
/// Lê o XML do JMdict (do EDRDG, <c>JMdict_e</c>, só com glosas em inglês) e grava o índice local (<see cref="JmdictIndex"/>). O arquivo de origem tem cerca de 60 MB e o
/// app não o lê a cada consulta: a montagem acontece uma vez, no download. O XML usa entidades do DTD para as classes gramaticais (<c>&amp;v5k-s;</c>); o texto delas
/// ("Godan verb - Iku/Yuku special class") volta para o código ("v5k-s") pela tabela que o próprio cabeçalho do arquivo declara.
/// </summary>
public static partial class JmdictImporter
{
    private const int MaxEntityCharacters = 500_000_000;

    [GeneratedRegex("""<!ENTITY\s+(\S+)\s+"([^"]*)"\s*>""")]
    private static partial Regex EntityDeclaration();

    /// <exception cref="JmdictFileException">O arquivo não é gzip, não é o JMdict ou está cortado.</exception>
    public static void ImportGzipFile(string gzipPath, string indexPath)
    {
        try
        {
            using var file = File.OpenRead(gzipPath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var output = File.Create(indexPath);
            Import(gzip, output);
        }
        catch (InvalidDataException e)
        {
            throw new JmdictFileException("O arquivo baixado não é um JMdict compactado válido. Tente baixar de novo.", e);
        }
    }

    /// <exception cref="JmdictFileException">O XML não é o JMdict ou está cortado.</exception>
    public static void Import(Stream xml, Stream destination)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersFromEntities = MaxEntityCharacters,
            IgnoreWhitespace = true,
            IgnoreComments = false,
        };

        try
        {
            using var reader = XmlReader.Create(xml, settings);
            var build = new Build();
            ReadAll(reader, build);
            if (!build.SawRoot || build.Entries.Count == 0)
            {
                throw new JmdictFileException("O arquivo não parece ser o JMdict (falta a raiz <JMdict> ou não há entradas). Tente baixar de novo.");
            }

            build.WriteTo(destination);
        }
        catch (XmlException e)
        {
            throw new JmdictFileException("O arquivo do JMdict está incompleto ou não é um XML válido. Tente baixar de novo.", e);
        }
        catch (InvalidDataException e)
        {
            throw new JmdictFileException("O arquivo do JMdict está corrompido. Tente baixar de novo.", e);
        }
    }

    private static void ReadAll(XmlReader reader, Build build)
    {
        while (!reader.EOF)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.DocumentType:
                    build.Codes = DescriptionToCode(reader.Value);
                    reader.Read();
                    break;
                case XmlNodeType.Comment:
                    build.NoteComment(reader.Value);
                    reader.Read();
                    break;
                case XmlNodeType.Element when reader.Name == "entry":
                    build.Add(ParseEntry((XElement)XNode.ReadFrom(reader), build.Codes));
                    break;
                case XmlNodeType.Element when reader.Name == "JMdict":
                    build.SawRoot = true;
                    reader.Read();
                    break;
                default:
                    reader.Read();
                    break;
            }
        }
    }

    private static Dictionary<string, string> DescriptionToCode(string internalSubset)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = EntityDeclaration().Matches(internalSubset).Select(m => (Description: WebUtility.HtmlDecode(m.Groups[2].Value), Code: m.Groups[1].Value));
        foreach (var (description, code) in declarations)
        {
            map.TryAdd(description, code);
        }

        return map;
    }

    private static JmdictEntry ParseEntry(XElement entry, Dictionary<string, string> codes)
    {
        var tags = new List<string>();
        var kanji = Forms(entry, "k_ele", "keb", "ke_pri", tags);
        var readings = Forms(entry, "r_ele", "reb", "re_pri", tags);
        var senses = new List<JmdictSense>();
        IReadOnlyList<string> inherited = [];
        foreach (var sense in entry.Elements("sense"))
        {
            var parts = sense.Elements("pos").Select(p => codes.GetValueOrDefault(p.Value, p.Value)).ToList();
            var glosses = sense.Elements("gloss").Where(IsEnglish).Select(g => g.Value).ToList();
            if (parts.Count > 0)
            {
                inherited = parts;
            }

            if (glosses.Count > 0)
            {
                senses.Add(new JmdictSense(inherited, glosses));
            }
        }

        _ = int.TryParse(entry.Element("ent_seq")?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id);
        return new JmdictEntry(id, kanji, readings, senses, tags.Select(RankOf).DefaultIfEmpty(JmdictEntry.NoRank).Min());
    }

    private static bool IsEnglish(XElement gloss)
    {
        var language = gloss.Attribute(XNamespace.Xml + "lang")?.Value;
        return language is null or "eng";
    }

    private static List<string> Forms(XElement entry, string group, string text, string priority, List<string> tags)
    {
        var forms = new List<string>();
        foreach (var element in entry.Elements(group))
        {
            forms.AddRange(element.Elements(text).Select(e => e.Value).Where(v => v.Length > 0));
            tags.AddRange(element.Elements(priority).Select(e => e.Value));
        }

        return forms;
    }

    /// <summary>Frequência de uma marca do JMdict: <c>nf01</c> a <c>nf48</c> valem o próprio número (as 500 palavras mais usadas por faixa) e as marcas <c>news1</c>, <c>ichi1</c>, <c>spec1</c> e <c>gai1</c> valem 12.</summary>
    internal static int RankOf(string tag)
    {
        if (tag.StartsWith("nf", StringComparison.Ordinal) && int.TryParse(tag.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var band))
        {
            return Math.Clamp(band, 1, JmdictEntry.NoRank - 1);
        }

        return tag switch
        {
            "news1" or "ichi1" or "spec1" or "gai1" => 12,
            "news2" or "ichi2" or "spec2" or "gai2" => 30,
            _ => JmdictEntry.NoRank,
        };
    }

    private sealed class Build
    {
        public Dictionary<string, string> Codes { get; set; } = new(StringComparer.Ordinal);

        public bool SawRoot { get; set; }

        public string Created { get; private set; } = string.Empty;

        public List<JmdictEntry> Entries { get; } = [];

        public void NoteComment(string comment)
        {
            const string marker = "JMdict created:";
            var at = comment.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                Created = comment[(at + marker.Length)..].Trim();
            }
        }

        public void Add(JmdictEntry entry) => Entries.Add(entry);

        public void WriteTo(Stream destination)
        {
            var keys = new SortedDictionary<byte[], List<int>>(ByteOrder.Instance);
            var maxKey = 0;
            for (var i = 0; i < Entries.Count; i++)
            {
                foreach (var form in Entries[i].Kanji.Concat(Entries[i].Readings))
                {
                    maxKey = Math.Max(maxKey, form.Length);
                    var bytes = Encoding.UTF8.GetBytes(form);
                    if (!keys.TryGetValue(bytes, out var list))
                    {
                        keys[bytes] = list = [];
                    }

                    if (list.Count == 0 || list[^1] != i)
                    {
                        list.Add(i);
                    }
                }
            }

            var keyRecords = new MemoryStream();
            var keyOffsets = new List<int>(keys.Count);
            using (var writer = new BinaryWriter(keyRecords, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var (key, list) in keys)
                {
                    keyOffsets.Add((int)keyRecords.Position);
                    writer.Write7BitEncodedInt(key.Length);
                    writer.Write(key);
                    writer.Write7BitEncodedInt(list.Count);
                    list.ForEach(writer.Write7BitEncodedInt);
                }
            }

            var entryRecords = new MemoryStream();
            var entryOffsets = new List<int>(Entries.Count);
            using (var writer = new BinaryWriter(entryRecords, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var entry in Entries)
                {
                    entryOffsets.Add((int)entryRecords.Position);
                    WriteEntry(writer, entry);
                }
            }

            var createdBytes = new MemoryStream();
            using (var writer = new BinaryWriter(createdBytes, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Created);
            }

            var keyTable = JmdictIndex.FixedHeaderLength + (int)createdBytes.Length;
            var entryTable = keyTable + (4 * keys.Count) + (int)keyRecords.Length;
            using var output = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            output.Write(JmdictIndex.Magic);
            output.Write(Entries.Count);
            output.Write(keys.Count);
            output.Write(maxKey);
            output.Write(keyTable);
            output.Write(entryTable);
            output.Write(createdBytes.ToArray());
            var keyBase = keyTable + (4 * keys.Count);
            keyOffsets.ForEach(o => output.Write(keyBase + o));
            output.Write(keyRecords.ToArray());
            var entryBase = entryTable + (4 * Entries.Count);
            entryOffsets.ForEach(o => output.Write(entryBase + o));
            output.Write(entryRecords.ToArray());
        }

        private static void WriteEntry(BinaryWriter writer, JmdictEntry entry)
        {
            writer.Write7BitEncodedInt(entry.Id);
            WriteStrings(writer, entry.Kanji);
            WriteStrings(writer, entry.Readings);
            writer.Write7BitEncodedInt(entry.Senses.Count);
            foreach (var sense in entry.Senses)
            {
                WriteStrings(writer, sense.PartsOfSpeech);
                WriteStrings(writer, sense.Glosses);
            }

            writer.Write7BitEncodedInt(entry.Rank);
        }

        private static void WriteStrings(BinaryWriter writer, IReadOnlyList<string> items)
        {
            writer.Write7BitEncodedInt(items.Count);
            foreach (var item in items)
            {
                writer.Write(item);
            }
        }
    }

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
