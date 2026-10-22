using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    private const int FuzzCases = 500;

    private static byte[] FuzzContent(Random random, int maxLength)
    {
        var content = new byte[random.Next(1, maxLength)];
        if (random.Next(2) == 0)
        {
            Array.Fill(content, (byte)random.Next(256));
        }
        else
        {
            random.NextBytes(content);
        }

        return content;
    }

    private static SyntheticYpf FuzzArchive(Random random, uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var builder = new SyntheticYpfBuilder(version)
            .WithOffsetSize(offsetSize)
            .WithDataOrder(random.Next(2) == 0 ? SyntheticYpfDataOrder.Index : SyntheticYpfDataOrder.Insertion)
            .WithSharedDuplicateData(random.Next(2) == 0);
        var pool = new List<byte[]>();
        var count = random.Next(1, 8);
        for (var i = 0; i < count; i++)
        {
            byte[] content;
            if (pool.Count > 0 && random.Next(3) == 0)
            {
                content = pool[random.Next(pool.Count)];
            }
            else
            {
                content = random.Next(4) == 0 ? [] : FuzzContent(random, 300);
                pool.Add(content);
            }

            builder.AddFile(
                $"f{i}.ybn",
                content,
                SyntheticYpfFileType.Ybn,
                compress: random.Next(2) == 0,
                forceCompression: random.Next(5) == 0);
        }

        return builder.Build();
    }

    private static Dictionary<string, byte[]> FuzzReplacements(Random random, SyntheticYpf ypf)
    {
        var replacements = new Dictionary<string, byte[]>();
        foreach (var entry in ypf.Entries.Where(_ => random.Next(2) == 0))
        {
            replacements[Key(entry.Name)] = random.Next(4) switch
            {
                0 => entry.Content,
                1 => [],
                _ => FuzzContent(random, 400),
            };
        }

        return replacements;
    }

    private static byte[] UnreferencedBytes(byte[] file, YpfParameters parameters) =>
        parameters.Gaps
            .SelectMany(g => file[(int)g.Offset..(int)(g.Offset + g.Length)])
            .Concat(file[(int)parameters.Trailer.Offset..])
            .ToArray();

    private static void AssertFuzzOutput(SyntheticYpf ypf, byte[] input, YpfArchive archive, Dictionary<string, byte[]> replacements, byte[] output)
    {
        using var rewritten = Open(output);
        Assert.Equal(archive.Entries.Count, rewritten.Entries.Count);
        Assert.Equal(input[..YpfHeader.Size], output[..YpfHeader.Size]);
        Assert.Equal(archive.Parameters.IndexSize, rewritten.Parameters.IndexSize);
        Assert.Equal(UnreferencedBytes(input, archive.Parameters), UnreferencedBytes(output, rewritten.Parameters));

        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var before = archive.Entries[i];
            var after = rewritten.Entries[i];
            var original = ypf.Entries.Single(e => Key(e.Name) == before.Path).Content;
            var expected = replacements.TryGetValue(before.Path, out var replaced) ? replaced : original;

            using var stream = rewritten.OpenEntry(after);
            Assert.Equal(expected, ReadAll(stream));
            Assert.Equal(before.IsCompressed, after.IsCompressed);
            Assert.True(after.Offset <= (ulong)output.Length, $"offset fora do arquivo em {before.Path}");

            var storedAfter = output[(int)after.Offset..(int)(after.Offset + after.SizeStored)];
            if (!replacements.TryGetValue(before.Path, out var replacement) || replacement.SequenceEqual(original))
            {
                Assert.Equal(before.SizeStored, after.SizeStored);
                Assert.Equal(before.DataHash, after.DataHash);
                Assert.Equal(input[(int)before.Offset..(int)(before.Offset + before.SizeStored)], storedAfter);
            }
            else
            {
                Assert.Equal(YpfHash.Compute(archive.Parameters.DataHash, storedAfter), after.DataHash);
            }
        }
    }

    [Fact]
    public void WriteTo_FuzzDeterministicoMantemEntradasIndiceECompressao()
    {
        var random = new Random(12345);
        uint[] versions = [200, 300, 477, 479, 500];
        for (var iteration = 0; iteration < FuzzCases; iteration++)
        {
            var version = versions[random.Next(versions.Length)];
            var offsetSize = random.Next(2) == 0 ? SyntheticYpfOffsetSize.Bits32 : SyntheticYpfOffsetSize.Bits64;
            var ypf = FuzzArchive(random, version, offsetSize);

            var input = ypf.Bytes;
            if (random.Next(2) == 0)
            {
                var candidates = ypf.Entries.Where(e => e.SizeStored > 0).Select(e => (long)e.Offset).Append(ypf.DataStart).ToArray();
                input = WithGap(ypf, candidates[random.Next(candidates.Length)], random.Next(1, 5));
            }

            if (random.Next(2) == 0)
            {
                input = [.. input, 7, 8, 9];
            }

            var replacements = FuzzReplacements(random, ypf);
            using var archive = Open(input);
            byte[] output;
            try
            {
                output = Write(archive, replacements);
            }
            catch (NotSupportedException) when (!archive.Parameters.DataHashVerified)
            {
                continue;
            }

            try
            {
                AssertFuzzOutput(ypf, input, archive, replacements, output);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Caso {iteration} (versão {version}, offset {offsetSize}, {ypf.Entries.Count} entradas, trocas: {string.Join(", ", replacements.Select(kv => $"{kv.Key}:{kv.Value.Length}"))}) falhou.",
                    ex);
            }
        }
    }
}
