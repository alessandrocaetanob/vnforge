using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Engines.YuRis.Tests;

public sealed class YuRisEngineTests : IDisposable
{
    private const string ScriptKeyHex = "0xD36FAC96";
    private const string ScriptsArchiveKey = "scriptsArchive";
    private const string YstbVersionKey = "ystbVersion";
    private const string ScriptKeyDetail = "scriptKey";
    private const string WarningsKey = "warnings";
    private const string YscYbn = "ysc.ybn";
    private const string Yst1Ybn = "yst00001.ybn";
    private const string Yst2Ybn = "yst00002.ybn";
    private const string YsbinYpf = "ysbin.ypf";
    private const string BgmYpf = "bgm.ypf";
    private const string BgmOgg = "bgm01.ogg";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-engine-" + Guid.NewGuid().ToString("N"));
    private readonly YuRisEngine _engine = new();

    public YuRisEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] Script(uint version = 500, uint key = SyntheticYstbBuilder.KeyV290) =>
        new SyntheticYstbBuilder(Yscm).WithVersion(version).WithKey(key).AddWord("こんにちは", 1).AddReturnCode(0, 2).Build().Bytes;

    private static byte[] Archive(uint ypfVersion, params (string Name, byte[] Content)[] files)
    {
        var builder = new SyntheticYpfBuilder(ypfVersion);
        foreach (var (name, content) in files)
        {
            builder.AddFile(name, content);
        }

        return builder.Build().Bytes;
    }

    private static byte[] ScriptArchive(uint ypfVersion = 500, uint ystbVersion = 500, uint key = SyntheticYstbBuilder.KeyV290) =>
        Archive(ypfVersion, (YscYbn, Yscm.Bytes), (Yst1Ybn, Script(ystbVersion, key)), (Yst2Ybn, Script(ystbVersion, key)));

    private void Write(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    [Fact]
    public void Chave_propria_do_jogo_e_derivada_do_script()
    {
        Write(YsbinYpf, ScriptArchive(key: 0x12345678));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("0x12345678", result.Details[ScriptKeyDetail]);
    }

    [Fact]
    public void Pacote_so_com_ysc_ybn_nao_esconde_o_pacote_com_scripts()
    {
        Write("a.ypf", Archive(500, (YscYbn, Yscm.Bytes)));
        Write("b.ypf", Archive(500, (Yst1Ybn, Script())));

        var result = _engine.Detect(_root);

        Assert.Equal("b.ypf", result.Details[ScriptsArchiveKey]);
        Assert.Equal("500", result.Details[YstbVersionKey]);
    }

    [Fact]
    public void SizeStored_acima_do_limite_vira_aviso_sem_ler_a_entrada()
    {
        // O leitor do YPF recusa um size_stored que passa do fim do arquivo, então o limite só se alcança com bytes
        // reais: 32 MiB incompressíveis (Size no limite, não acima) viram um zlib um pouco maior que isso.
        const int limit = 32 * 1024 * 1024;
        var content = new XorShift64(42).Bytes(limit);
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(YscYbn, Yscm.Bytes)
            .AddFile(Yst1Ybn, content, compress: true, forceCompression: true)
            .Build();
        var entry = ypf.Entries.Single(e => e.Name == Yst1Ybn);
        Assert.Equal((uint)limit, entry.SizeUncompressed);
        Assert.True(entry.SizeStored > limit);
        Write(YsbinYpf, ypf.Bytes);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("yst00001.ybn: passa do limite", result.Details[WarningsKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
    }

    [Fact]
    public void Ypf_aberto_com_exclusividade_por_outro_processo_vira_aviso()
    {
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));
        using var lockStream = new FileStream(Path.Combine(_root, BgmYpf), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _engine.Detect(_root);

        Assert.False(result.IsMatch);
        Assert.Contains("bgm.ypf: não foi possível abrir", result.Details[WarningsKey]);
    }

    [Fact]
    public void Mais_de_64_ypf_avisa_que_cortou()
    {
        var empty = Archive(500);
        for (var i = 0; i < 65; i++)
        {
            Write($"p{i:D2}.ypf", empty);
        }

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("65 arquivos .ypf", result.Details[WarningsKey]);
        Assert.Equal(64, result.Details.Keys.Count(k => k.StartsWith("ypf:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Bytes_pseudoaleatorios_nos_ypf_e_nos_ybn_nunca_lancam()
    {
        var rng = new XorShift64(0x9E3779B97F4A7C15);
        var valid = ScriptArchive();

        for (var i = 0; i < 150; i++)
        {
            Write("x.ypf", rng.Next(3) switch
            {
                0 => WithMagic(rng.Bytes(rng.Next(300))),
                1 => rng.Bytes(rng.Next(300)),
                _ => Flip(valid, rng, 1 + rng.Next(4)),
            });
            AssertDoesNotThrow(i, "ypf");
        }

        for (var i = 0; i < 150; i++)
        {
            var ysc = rng.Next(2) == 0 ? Flip(Yscm.Bytes, rng, 1 + rng.Next(3)) : rng.Bytes(rng.Next(200));
            var yst1 = rng.Next(2) == 0 ? Flip(Script(), rng, 1 + rng.Next(3)) : WithYstb(rng.Bytes(rng.Next(200)));
            var yst2 = Flip(Script(), rng, 1 + rng.Next(3));
            Write(YsbinYpf, Archive(500, (YscYbn, ysc), (Yst1Ybn, yst1), (Yst2Ybn, yst2)));
            AssertDoesNotThrow(i, "ybn");
        }
    }

    private void AssertDoesNotThrow(int iteration, string phase)
    {
        var failure = Record.Exception(() => _engine.Detect(_root));
        Assert.True(failure is null, $"Detect lançou na iteração {iteration} ({phase}): {failure}");
    }

    private static byte[] WithMagic(byte[] body) => [.. "YPF\0"u8, .. body];

    private static byte[] WithYstb(byte[] body) => [.. "YSTB"u8, .. body];

    private static byte[] Flip(byte[] source, XorShift64 rng, int count)
    {
        var copy = (byte[])source.Clone();
        for (var i = 0; i < count; i++)
        {
            copy[rng.Next(copy.Length)] ^= (byte)(1 + rng.Next(255));
        }

        return copy;
    }

    /// <summary>Gerador determinístico para o fuzz; <c>System.Random</c> é proibido pelo Sonar (S2245).</summary>
    private sealed class XorShift64(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int exclusiveMax)
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return (int)(_state % (ulong)exclusiveMax);
        }

        public byte[] Bytes(int count)
        {
            var bytes = new byte[count];
            for (var i = 0; i < count; i++)
            {
                bytes[i] = (byte)Next(256);
            }

            return bytes;
        }
    }

    private List<string> Snapshot() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(p => File.Exists(p)
            ? $"{p}|{File.GetLastWriteTimeUtc(p).Ticks}|{Convert.ToHexString(File.ReadAllBytes(p))}"
            : p)
        .ToList();

    [Fact]
    public void Detect_nao_grava_nada_na_pasta()
    {
        Write(YsbinYpf, ScriptArchive());
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));
        var before = Snapshot();

        _engine.Detect(_root);

        Assert.Equal(before, Snapshot());
    }
}
