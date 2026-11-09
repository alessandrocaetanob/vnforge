using System.Buffers.Binary;
using System.Text;

namespace VNForge.Core.Jmdict;

/// <summary>
/// O índice local do JMdict (<c>jmdict.vnfdict</c>): um arquivo só, lido inteiro para a memória, com as formas escritas (kanji e leituras) em ordem para busca binária.
/// A consulta não acessa a rede nem decodifica o XML. Formato (inteiros little-endian; textos em UTF-8 com tamanho de 7 bits):
/// <c>"VNFDICT1"</c>, entradas, chaves, maior chave em caracteres, posição da tabela de chaves, posição da tabela de entradas, data de geração do JMdict;
/// depois, para cada tabela, uma lista de posições e os registros. Registro de chave: texto, quantidade e índices das entradas.
/// Registro de entrada: id, kanji, leituras, sentidos (classes e traduções) e frequência.
/// </summary>
public sealed class JmdictIndex
{
    internal static ReadOnlySpan<byte> Magic => "VNFDICT1"u8;

    internal const int FixedHeaderLength = 28;

    private readonly byte[] data;
    private readonly string path;
    private readonly int keyTable;
    private readonly int entryTable;
    private readonly int keyCount;

    private JmdictIndex(byte[] data, string path, int entryCount, int keyCount, int maxKeyLength, int keyTable, int entryTable, string created)
    {
        this.data = data;
        this.path = path;
        EntryCount = entryCount;
        this.keyCount = keyCount;
        MaxKeyLength = maxKeyLength;
        this.keyTable = keyTable;
        this.entryTable = entryTable;
        Created = created;
    }

    public int EntryCount { get; }

    /// <summary>O maior número de caracteres de uma forma escrita do índice; limita o trecho que vale a pena procurar.</summary>
    public int MaxKeyLength { get; }

    /// <summary>A data de geração que o EDRDG grava no cabeçalho do JMdict (<c>2026-11-09</c>); vazia se o arquivo não a traz.</summary>
    public string Created { get; }

    /// <exception cref="JmdictFileException">O arquivo não existe, não é um índice do VNForge ou está truncado.</exception>
    public static JmdictIndex Open(string path)
    {
        try
        {
            return FromBytes(File.ReadAllBytes(path), path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new JmdictFileException($"O dicionário '{path}' não pôde ser lido: {e.Message}", e);
        }
    }

    /// <exception cref="JmdictFileException">Os bytes não são um índice do VNForge ou estão truncados.</exception>
    public static JmdictIndex FromBytes(byte[] data, string path)
    {
        if (data.Length < FixedHeaderLength + 1 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new JmdictFileException($"O arquivo '{path}' não é um dicionário do VNForge. Baixe o dicionário de novo.");
        }

        var entries = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        var keys = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        var maxKey = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        var keyTable = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
        var entryTable = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
        var tablesFit = entries >= 0 && keys >= 0 && keyTable >= FixedHeaderLength && entryTable >= keyTable
            && (long)keyTable + (4L * keys) <= data.Length && (long)entryTable + (4L * entries) <= data.Length;
        if (!tablesFit)
        {
            throw new JmdictFileException($"O dicionário '{path}' está incompleto. Baixe o dicionário de novo.");
        }

        var index = new JmdictIndex(data, path, entries, keys, maxKey, keyTable, entryTable, ReadCreated(data));
        index.CheckLastRecords();
        return index;
    }

    private static string ReadCreated(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data, FixedHeaderLength, data.Length - FixedHeaderLength, writable: false), Encoding.UTF8);
        try
        {
            return reader.ReadString();
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>Um arquivo cortado no fim perde os últimos registros: ler o último de cada tabela acusa isso na abertura e não na primeira consulta.</summary>
    private void CheckLastRecords()
    {
        try
        {
            if (EntryCount > 0)
            {
                _ = ReadEntry(EntryCount - 1);
            }

            if (keyCount > 0)
            {
                _ = KeyAt(keyCount - 1);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            throw new JmdictFileException($"O dicionário '{path}' está incompleto. Baixe o dicionário de novo.", e);
        }
    }

    /// <summary>As entradas que têm exatamente esta forma escrita (kanji ou leitura), da mais comum para a menos comum. Vazio se não há nenhuma.</summary>
    public IReadOnlyList<JmdictEntry> Find(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return [];
        }

        var wanted = Encoding.UTF8.GetBytes(key);
        int low = 0, high = keyCount - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var (found, entries) = KeyAt(middle, wanted);
            if (found == 0)
            {
                return [.. entries!.Select(ReadEntry).OrderBy(e => e.Rank).ThenBy(e => e.Id)];
            }

            if (found < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return [];
    }

    private int Offset(int table, int position) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(table + (4 * position)));

    private (int Order, int[]? Entries) KeyAt(int position, byte[]? wanted = null)
    {
        var reader = new SpanReader(data, Offset(keyTable, position));
        var length = reader.Read7BitInt();
        var stored = data.AsSpan(reader.Position, length);
        reader.Skip(length);
        if (wanted is not null)
        {
            var order = stored.SequenceCompareTo(wanted);
            if (order != 0)
            {
                return (order < 0 ? -1 : 1, null);
            }
        }

        var count = reader.Read7BitInt();
        var entries = new int[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = reader.Read7BitInt();
        }

        return (0, entries);
    }

    private JmdictEntry ReadEntry(int position)
    {
        var reader = new SpanReader(data, Offset(entryTable, position));
        var id = reader.Read7BitInt();
        var kanji = reader.ReadStrings();
        var readings = reader.ReadStrings();
        var senses = new JmdictSense[reader.Read7BitInt()];
        for (var i = 0; i < senses.Length; i++)
        {
            senses[i] = new JmdictSense(reader.ReadStrings(), reader.ReadStrings());
        }

        return new JmdictEntry(id, kanji, readings, senses, reader.Read7BitInt());
    }

    /// <summary>Leitor de posição própria sobre os bytes do índice; lança <see cref="InvalidDataException"/> quando um registro passa do fim do arquivo.</summary>
    private struct SpanReader(byte[] data, int position)
    {
        public int Position { get; private set; } = position;

        public void Skip(int count)
        {
            if (count < 0 || Position + count > data.Length)
            {
                throw new InvalidDataException("registro além do fim do arquivo");
            }

            Position += count;
        }

        public int Read7BitInt()
        {
            var value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                if (Position >= data.Length)
                {
                    throw new InvalidDataException("registro além do fim do arquivo");
                }

                var next = data[Position++];
                value |= (next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("número inválido no registro");
        }

        public string ReadString()
        {
            var length = Read7BitInt();
            var start = Position;
            Skip(length);
            return Encoding.UTF8.GetString(data, start, length);
        }

        public string[] ReadStrings()
        {
            var items = new string[Read7BitInt()];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = ReadString();
            }

            return items;
        }
    }
}
