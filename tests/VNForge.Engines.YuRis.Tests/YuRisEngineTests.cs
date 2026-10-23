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
    public void Identifica_a_engine_e_expoe_o_formato_ypf()
    {
        Assert.Equal("yuris", _engine.Id);
        Assert.Contains(_engine.Archives, a => a.Id == "ypf");
        Assert.IsAssignableFrom<IEngine>(_engine);
    }

    [Fact]
    public void Pasta_sem_yuris_nao_e_reconhecida()
    {
        Write("jogo.exe", [0x4D, 0x5A, 0x00]);
        Write("data/script.rpy", "label start:"u8.ToArray());

        var result = _engine.Detect(_root);

        Assert.False(result.IsMatch);
        Assert.Equal(string.Empty, result.Summary);
        Assert.Empty(result.Details);
    }

    [Fact]
    public void Pasta_vazia_nao_e_reconhecida()
    {
        Assert.False(_engine.Detect(_root).IsMatch);
    }

    [Fact]
    public void Pasta_inexistente_nao_e_reconhecida_e_nao_lanca()
    {
        var result = _engine.Detect(Path.Combine(_root, "nao-existe"));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Arquivo_ypf_sem_assinatura_nao_e_reconhecido()
    {
        Write(YsbinYpf, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.False(_engine.Detect(_root).IsMatch);
    }

    [Fact]
    public void Jogo_sintetico_informa_versoes_arquivos_e_chave()
    {
        Write(YsbinYpf, ScriptArchive());
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));
        Write("jogo.exe", [0x4D, 0x5A]);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("YU-RIS", result.Summary);
        Assert.Contains("ysbin.ypf v500", result.Summary);
        Assert.Contains(ScriptKeyHex, result.Summary);
        Assert.Equal(YsbinYpf, result.Details[ScriptsArchiveKey]);
        Assert.Equal("500", result.Details["ypfVersion"]);
        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.Equal(ScriptKeyHex, result.Details[ScriptKeyDetail]);
        Assert.Equal("2", result.Details["scripts"]);
        Assert.Equal("v500, 3 arquivos", result.Details["ypf:ysbin.ypf"]);
        Assert.Equal("v500, 1 arquivo", result.Details["ypf:bgm.ypf"]);
        Assert.False(result.Details.ContainsKey(WarningsKey));
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
    public void Acha_os_ypf_na_pasta_pac_e_ignora_maiusculas()
    {
        Write("PAC/YSBIN.YPF", ScriptArchive());

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("PAC/YSBIN.YPF", result.Details[ScriptsArchiveKey]);
        Assert.Equal(ScriptKeyHex, result.Details[ScriptKeyDetail]);
    }

    [Fact]
    public void Ypf_sem_scripts_e_reconhecido_sem_chave()
    {
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
        Assert.False(result.Details.ContainsKey(ScriptsArchiveKey));
        Assert.Contains(BgmYpf, result.Summary);
    }

    [Fact]
    public void Sem_ysc_ybn_informa_a_versao_do_ystb_mas_nao_a_chave()
    {
        Write(YsbinYpf, Archive(500, (Yst1Ybn, Script())));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
        Assert.Contains(YscYbn, result.Details[WarningsKey]);
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
    public void Ystb_abaixo_da_300_informa_a_versao_e_avisa()
    {
        Write(YsbinYpf, ScriptArchive(ypfVersion: 290, ystbVersion: 290));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("290", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
        Assert.True(result.Details.ContainsKey(WarningsKey));
    }

    [Fact]
    public void Ypf_com_assinatura_e_indice_quebrado_e_reconhecido_com_aviso()
    {
        var bytes = ScriptArchive();
        Write(YsbinYpf, bytes[..40]);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains(YsbinYpf, result.Details[WarningsKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
    }

    [Fact]
    public void Size_acima_do_limite_vira_aviso_sem_ler_a_entrada()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile(YscYbn, Yscm.Bytes).AddFile(Yst1Ybn, Script()).Build();
        Write(YsbinYpf, ypf.WithUncompressedSize(YscYbn, 0x7FFFFFFF));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Contains("ysc.ybn: passa do limite", result.Details[WarningsKey]);
        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
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
    public void Ysc_ybn_corrompido_mantem_a_versao_do_ystb_e_avisa()
    {
        Write(YsbinYpf, Archive(500, (YscYbn, [1, 2, 3, 4, 5, 6, 7, 8, 9]), (Yst1Ybn, Script())));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
        Assert.StartsWith("ysc.ybn:", result.Details[WarningsKey]);
    }

    [Fact]
    public void Primeiro_script_sem_assinatura_nao_impede_ler_o_segundo()
    {
        Write(YsbinYpf, Archive(500, (YscYbn, Yscm.Bytes), (Yst1Ybn, [1, 2, 3]), (Yst2Ybn, Script())));

        var result = _engine.Detect(_root);

        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.Equal(ScriptKeyHex, result.Details[ScriptKeyDetail]);
        Assert.False(result.Details.ContainsKey(WarningsKey));
    }

    [Fact]
    public void Sem_ysc_ybn_a_versao_vem_do_primeiro_script_com_assinatura()
    {
        Write(YsbinYpf, Archive(500, (Yst1Ybn, [1, 2, 3]), (Yst2Ybn, Script())));

        var result = _engine.Detect(_root);

        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
    }

    [Fact]
    public void Chave_que_nao_decifra_nenhum_script_vira_aviso_do_primeiro_erro()
    {
        var broken1 = Script();
        broken1[32] ^= 0xFF;
        var broken2 = Script();
        broken2[32] ^= 0xFF;
        Write(YsbinYpf, Archive(500, (YscYbn, Yscm.Bytes), (Yst1Ybn, broken1), (Yst2Ybn, broken2)));

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal("500", result.Details[YstbVersionKey]);
        Assert.False(result.Details.ContainsKey(ScriptKeyDetail));
        Assert.StartsWith("yst00001.ybn:", result.Details[WarningsKey]);
        Assert.DoesNotContain(Yst2Ybn, result.Details[WarningsKey]);
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
    public void Ypf_bloqueado_nao_impede_reconhecer_pelo_outro()
    {
        Write(YsbinYpf, ScriptArchive());
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));
        using var lockStream = new FileStream(Path.Combine(_root, BgmYpf), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _engine.Detect(_root);

        Assert.True(result.IsMatch);
        Assert.Equal(ScriptKeyHex, result.Details[ScriptKeyDetail]);
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

    [Fact]
    public void Detect_nao_grava_nada_na_pasta()
    {
        Write(YsbinYpf, ScriptArchive());
        Write(BgmYpf, Archive(500, (BgmOgg, [1, 2, 3])));
        var before = Snapshot();

        _engine.Detect(_root);

        Assert.Equal(before, Snapshot());
    }

    private List<string> Snapshot() => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(p => File.Exists(p)
            ? $"{p}|{File.GetLastWriteTimeUtc(p).Ticks}|{Convert.ToHexString(File.ReadAllBytes(p))}"
            : p)
        .ToList();
}
