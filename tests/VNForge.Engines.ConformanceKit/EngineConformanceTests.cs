using System.Diagnostics;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Engines.ConformanceKit;

/// <summary>
/// Os testes que toda <see cref="IEngine"/> precisa passar: identidade, detecção, ida e volta byte a byte (regra 3 do <c>CLAUDE.md</c>),
/// marcadores, falante, escolhas e título da janela. A engine herda desta classe no projeto de testes dela e entrega só os dados sintéticos;
/// o xUnit descobre os testes herdados como se fossem dela. Os testes que são só da engine continuam no projeto dela.
/// </summary>
/// <remarks>
/// O kit nunca lê arquivo de jogo real (regra 1): tudo vem de <see cref="WriteSyntheticGame"/>, <see cref="CreateScripts"/> e
/// <see cref="WriteSyntheticTitle"/>. Uma engine nova herda e implementa os seis membros abstratos; o resto roda sozinho.
/// </remarks>
public abstract class EngineConformanceTests : IDisposable
{
    private const string NewTitle = "New title";

    private readonly Lazy<IEngine> _engine;
    private readonly Lazy<EngineContext> _context;
    private readonly Lazy<ITextEncoder> _encoder;
    private readonly Lazy<IReadOnlyList<ConformanceScript>> _scripts;
    private int _folders;

    /// <summary>Cria o kit e a pasta temporária que os testes usam.</summary>
    protected EngineConformanceTests()
    {
        Root = Path.Combine(Path.GetTempPath(), "vnforge-conformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _engine = new(CreateEngine);
        _context = new(CreateContext);
        _encoder = new(CreateEncoder);
        _scripts = new(CreateScripts);
    }

    /// <summary>Pasta temporária própria desta instância, apagada no fim do teste.</summary>
    protected string Root { get; }

    /// <summary>A engine sob teste, criada uma vez por instância.</summary>
    protected IEngine Engine => _engine.Value;

    /// <summary>Texto que o codificador de <see cref="CreateEncoder"/> não grava; o padrão (um emoji) não existe em nenhuma página de código antiga.</summary>
    protected virtual string UnencodableText => "\U0001F600";

    /// <summary>A engine sob teste.</summary>
    protected abstract IEngine CreateEngine();

    /// <summary>Codificador que a engine usa para gravar texto no jogo (no YU-RIS, Shift-JIS).</summary>
    protected abstract ITextEncoder CreateEncoder();

    /// <summary>Contexto da engine para os scripts de <see cref="CreateScripts"/>, o mesmo que <c>IGameUnpacker.Open</c> montaria para o jogo sintético.</summary>
    protected abstract EngineContext CreateContext();

    /// <summary>
    /// Grava em <paramref name="directory"/> (já criada, vazia) um jogo sintético que <see cref="IEngine.Detect"/> reconhece.
    /// </summary>
    protected abstract void WriteSyntheticGame(string directory);

    /// <summary>
    /// Scripts sintéticos com o que a extração deve devolver. Juntos precisam ter um diálogo com falante, uma escolha e uma unidade com
    /// marcador, todos com <see cref="ExpectedUnit.Translation"/>; o kit falha se faltar algum, para a engine não passar sem ser testada.
    /// </summary>
    protected abstract IReadOnlyList<ConformanceScript> CreateScripts();

    /// <summary>
    /// Divide os bytes de um script nas faixas do formato da engine (<see cref="ScriptRegion"/>), para o kit conferir que a reescrita só mexe no que
    /// a tradução alcança: o resto do script, inclusive argumentos que a tradução não toca, sai byte a byte igual (regra 3 do <c>CLAUDE.md</c>).
    /// As faixas cobrem o script inteiro, em ordem, e têm os mesmos nomes no original e no reconstruído. Declare mutável só o que a engine de fato muda:
    /// as faixas que apontam para o texto de uma unidade (<see cref="ScriptRegion.Unit"/>), um campo de tamanho compartilhado
    /// (<see cref="ScriptRegion.Shared"/>) e uma área que cresce (<see cref="ScriptRegion.Appends"/>).
    /// </summary>
    /// <param name="script">O script original ou o reconstruído, que têm o mesmo número de unidades e o mesmo layout de instruções.</param>
    /// <param name="units">As unidades extraídas do script <em>original</em>, na ordem de <see cref="ConformanceScript.Units"/>; o
    /// <see cref="TextUnit.Locator"/> de cada uma diz onde ela está.</param>
    protected abstract IReadOnlyList<ScriptRegion> DescribeScript(ScriptFile script, IReadOnlyList<TextUnit> units);

    /// <summary>
    /// Só para engine que também é <see cref="IGameTitleSource"/>: grava em <paramref name="unpackedDirectory"/> (já criada, vazia) o arquivo do
    /// título como o <c>IGameUnpacker.Unpack</c> o deixa, e devolve o título que <see cref="IGameTitleSource.ReadTitle"/> deve ler. O título
    /// novo dos testes é ASCII, então o codificador do kit o grava sem objeção.
    /// </summary>
    protected virtual string WriteSyntheticTitle(string unpackedDirectory) =>
        throw new NotSupportedException($"A engine '{Engine.Id}' é uma IGameTitleSource: sobrescreva {nameof(WriteSyntheticTitle)}.");

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Apaga a pasta temporária.</summary>
    /// <param name="disposing">Verdadeiro quando chamado por <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private string NewFolder()
    {
        var path = Path.Combine(Root, "p" + _folders++);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ForeignContext() : EngineContext("outra-engine");

    private sealed record Subject(ConformanceScript Script, IScriptFormat Format, ScriptFile File)
    {
        public string Describe(string text) => $"{Script.Name}: {text}";
    }

    private List<Subject> Subjects() =>
    [
        .. _scripts.Value.Select(script =>
        {
            var extension = Path.GetExtension(script.Name).ToLowerInvariant();
            var format = Engine.Scripts.SingleOrDefault(f => f.FileExtensions.Contains(extension));
            Assert.True(format is not null, $"Nenhum IScriptFormat da engine '{Engine.Id}' aceita a extensão '{extension}' do script '{script.Name}'.");
            return new Subject(script, format, new ScriptFile(script.Name, script.Content));
        }),
    ];

    private static List<TextUnit> Extract(Subject subject, EngineContext context) => [.. subject.Format.Extract(subject.File, context).Units];

    private static void AssertSameBytes(string label, ReadOnlyMemory<byte> expected, byte[] actual) =>
        Assert.True(expected.Span.SequenceEqual(actual), $"{label}: o arquivo reconstruído difere do original ({expected.Length} bytes contra {actual.Length}).");

    [Fact]
    public void Engine_declara_id_e_nome_validos()
    {
        Assert.False(string.IsNullOrWhiteSpace(Engine.Id));
        Assert.Equal(Engine.Id.ToLowerInvariant(), Engine.Id);
        Assert.DoesNotContain(Engine.Id, char.IsWhiteSpace);
        Assert.False(string.IsNullOrWhiteSpace(Engine.DisplayName));
    }

    [Fact]
    public void Engine_expoe_ao_menos_um_formato_de_script_bem_declarado()
    {
        Assert.NotEmpty(Engine.Scripts);
        Assert.NotNull(Engine.Archives);
        Assert.Distinct(Engine.Archives.Select(f => f.Id));
        Assert.NotNull(Engine.Patches);
        Assert.Distinct(Engine.Scripts.Select(f => f.Id));
        foreach (var format in Engine.Scripts)
        {
            Assert.False(string.IsNullOrWhiteSpace(format.Id));
            Assert.Equal(format.Id.ToLowerInvariant(), format.Id);
            Assert.NotEmpty(format.FileExtensions);
            Assert.All(format.FileExtensions, e => Assert.True(e.Length > 1 && e[0] == '.' && e == e.ToLowerInvariant(), $"Extensão '{e}' do formato '{format.Id}' deve ter ponto e minúsculas."));
        }
    }

    [Fact]
    public void Pasta_vazia_nao_e_reconhecida()
    {
        Assert.False(Engine.Detect(NewFolder()).IsMatch);
    }

    [Fact]
    public void Pasta_inexistente_nao_e_reconhecida_e_nao_lanca()
    {
        Assert.False(Engine.Detect(Path.Combine(Root, "nao-existe")).IsMatch);
    }

    [Fact]
    public void Pasta_de_arquivos_quaisquer_nao_e_reconhecida()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "jogo.exe"), [0x4D, 0x5A, 0x00]);
        File.WriteAllText(Path.Combine(folder, "leia-me.txt"), "nada de jogo aqui");

        Assert.False(Engine.Detect(folder).IsMatch);
    }

    [Fact]
    public void Jogo_sintetico_e_reconhecido_com_resumo()
    {
        var folder = NewFolder();
        WriteSyntheticGame(folder);

        var detection = Engine.Detect(folder);

        Assert.True(detection.IsMatch);
        Assert.False(string.IsNullOrWhiteSpace(detection.Summary));
    }

    [Fact]
    public void Detect_nao_grava_nada_na_pasta()
    {
        var folder = NewFolder();
        WriteSyntheticGame(folder);
        var before = Snapshot(folder);

        Engine.Detect(folder);

        Assert.Equal(before, Snapshot(folder));
    }

    private static List<string> Snapshot(string folder) => [.. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(p => File.Exists(p) ? $"{p}|{File.GetLastWriteTimeUtc(p).Ticks}|{Convert.ToHexString(File.ReadAllBytes(p))}" : p)];

    private List<(string Path, byte[] Bytes, IArchiveFormat Format)> SyntheticArchives()
    {
        var folder = NewFolder();
        WriteSyntheticGame(folder);
        var found = new List<(string, byte[], IArchiveFormat)>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            foreach (var format in Engine.Archives.Where(f => f.FileExtensions.Contains(extension)))
            {
                using var stream = new MemoryStream(bytes);
                if (format.CanOpen(stream))
                {
                    found.Add((Path.GetRelativePath(folder, path), bytes, format));
                }
            }
        }

        return found;
    }

    [Fact]
    public void Pacote_sem_trocas_e_regravado_byte_a_byte_igual()
    {
        var archives = SyntheticArchives();
        if (archives.Count == 0)
        {
            Assert.True(Engine.Archives.Count == 0, $"A engine '{Engine.Id}' declara formatos de pacote, mas WriteSyntheticGame não gravou nenhum arquivo que eles abram.");
            Assert.Skip($"A engine '{Engine.Id}' não declara formato de pacote.");
        }

        foreach (var (path, bytes, format) in archives)
        {
            using var archive = format.Open(new MemoryStream(bytes));
            Assert.NotEmpty(archive.Entries);

            var untouched = new MemoryStream();
            archive.WriteTo(untouched, new Dictionary<string, byte[]>());
            AssertSameBytes($"{path}: WriteTo sem trocas", bytes, untouched.ToArray());

            var sameContent = new Dictionary<string, byte[]>();
            foreach (var entry in archive.Entries)
            {
                using var content = archive.OpenEntry(entry);
                using var copy = new MemoryStream();
                content.CopyTo(copy);
                sameContent[entry.Path] = copy.ToArray();
            }

            var repacked = new MemoryStream();
            archive.WriteTo(repacked, sameContent);
            AssertSameBytes($"{path}: WriteTo com o conteúdo de cada entrada, sem mudar nada", bytes, repacked.ToArray());
        }
    }

    [Fact]
    public void Os_scripts_sinteticos_cobrem_falante_escolha_e_marcador()
    {
        var units = _scripts.Value.SelectMany(s => s.Units).ToList();

        Assert.NotEmpty(units);
        Assert.Contains(units, u => u.Speaker is not null && u.Translation is not null);
        Assert.Contains(units, u => u.Kind == TextUnitKind.Choice && u.Translation is not null);
        Assert.Contains(units, u => u.Markers.Count > 0 && u.Translation is not null);
        Assert.Contains(units, u => u.Speaker is not null && u.Translation is not null && u.SpeakerTranslation is not null);
        Assert.All(units.Where(u => u.SpeakerTranslation is not null), u =>
        {
            Assert.NotNull(u.Speaker);
            Assert.NotNull(u.Translation);
            Assert.NotEqual(u.Speaker, u.SpeakerTranslation);
            Assert.Empty(_encoder.Value.FindUnencodable(u.SpeakerTranslation!));
        });
        Assert.All(units.Where(u => u.Translation is not null), u =>
        {
            Assert.NotEqual(u.Source, u.Translation);
            Assert.Empty(_encoder.Value.FindUnencodable(u.Translation!));
            Assert.All(u.Markers, marker =>
            {
                Assert.Contains(marker, u.Source, StringComparison.Ordinal);
                Assert.Contains(marker, u.Translation!, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    public void Extract_devolve_as_unidades_esperadas_com_ids_estaveis()
    {
        foreach (var subject in Subjects())
        {
            var units = Extract(subject, _context.Value);

            Assert.Equal(
                subject.Script.Units.Select(u => subject.Describe($"{u.Kind}|{u.Speaker}|{u.Source}")),
                units.Select(u => subject.Describe($"{u.Kind}|{u.Speaker}|{u.Source}")));
            Assert.Distinct(units.Select(u => u.Id));
            Assert.All(units, u =>
            {
                Assert.StartsWith(subject.File.Key + ":", u.Id, StringComparison.Ordinal);
                Assert.Null(u.Translation);
                Assert.Equal(TranslationStatus.Untranslated, u.Status);
                Assert.NotNull(u.Locator);
            });
            Assert.Equal(units.Select(u => u.Id), Extract(subject, _context.Value).Select(u => u.Id));
        }
    }

    [Fact]
    public void Extract_nao_altera_o_arquivo()
    {
        foreach (var subject in Subjects())
        {
            var before = subject.File.Content.ToArray();

            Extract(subject, _context.Value);

            AssertSameBytes(subject.Describe("Extract"), before, subject.File.Content.ToArray());
        }
    }

    [Fact]
    public void Extract_de_conteudo_que_nao_e_do_formato_lanca_InvalidDataException()
    {
        foreach (var subject in Subjects())
        {
            var garbage = new ScriptFile(subject.File.Name, Enumerable.Range(1, 12).Select(i => (byte)i).ToArray());

            Assert.Throws<InvalidDataException>(() => subject.Format.Extract(garbage, _context.Value));
        }
    }

    [Fact]
    public void Contexto_de_outra_engine_lanca_ArgumentException()
    {
        foreach (var subject in Subjects())
        {
            var units = Extract(subject, _context.Value);

            Assert.Throws<ArgumentException>(() => subject.Format.Extract(subject.File, new ForeignContext()));
            Assert.Throws<ArgumentException>(() => subject.Format.Rebuild(subject.File, units, _encoder.Value, new ForeignContext()));
        }
    }

    [Fact]
    public void Rebuild_que_nao_reescreve_nada_devolve_o_script_byte_a_byte_igual()
    {
        var encoder = _encoder.Value;
        var subjects = Subjects().Where(s => !s.Format.RequiresRebuild(s.File, encoder, _context.Value)).ToList();
        Assert.NotEmpty(subjects);
        foreach (var subject in subjects)
        {
            var units = Extract(subject, _context.Value);
            var cases = new Dictionary<string, List<TextUnit>>
            {
                ["sem unidades"] = [],
                ["todas sem tradução"] = units,
                ["só a última"] = [.. units.TakeLast(1)],
                ["ordem invertida"] = [.. Enumerable.Reverse(units)],
                ["tradução igual à origem"] = [.. units.Select(u => u with { Translation = u.Source, Status = TranslationStatus.Translated })],
                ["tradução com status não traduzido"] = [.. units.Select(u => u with { Translation = u.Source + "!", Status = TranslationStatus.Untranslated })],
                ["tradução vazia"] = [.. units.Select(u => u with { Translation = string.Empty, Status = TranslationStatus.Translated })],
            };

            foreach (var (label, list) in cases)
            {
                AssertSameBytes(subject.Describe(label), subject.File.Content, subject.Format.Rebuild(subject.File, list, encoder, _context.Value));
            }
        }
    }

    [Fact]
    public void Rebuild_com_traducao_reextrai_o_texto_novo_e_deixa_o_resto()
    {
        AssertTranslationsSurvive(u => u.Translation is not null);
    }

    [Fact]
    public void Falante_sobrevive_a_traducao()
    {
        AssertTranslationsSurvive(u => u.Speaker is not null && u.Translation is not null);
    }

    [Fact]
    public void Falante_traduzido_e_gravado_e_relido()
    {
        AssertTranslationsSurvive(u => u.SpeakerTranslation is not null && u.Translation is not null, SpeakerChange.Translate);
    }

    [Fact]
    public void Falante_escolhido_e_gravado_e_relido()
    {
        AssertTranslationsSurvive(u => u.SpeakerTranslation is not null && u.Translation is not null, SpeakerChange.Choose);
    }

    [Fact]
    public void Escolhas_sobrevivem_a_traducao()
    {
        AssertTranslationsSurvive(u => u.Kind == TextUnitKind.Choice && u.Translation is not null);
    }

    [Fact]
    public void Marcadores_sobrevivem_a_traducao()
    {
        AssertTranslationsSurvive(u => u.Markers.Count > 0 && u.Translation is not null);
    }

    private enum SpeakerChange
    {
        Keep,
        Translate,
        Choose,
    }

    private static TextUnit Translated(TextUnit unit, ExpectedUnit expected, SpeakerChange speaker) => speaker switch
    {
        SpeakerChange.Translate => unit with { Speaker = expected.SpeakerTranslation, Translation = expected.Translation, Status = TranslationStatus.Translated },
        SpeakerChange.Choose => unit with { ChosenSpeaker = expected.SpeakerTranslation, Translation = expected.Translation, Status = TranslationStatus.Translated },
        _ => unit with { Translation = expected.Translation, Status = TranslationStatus.Translated },
    };

    private void AssertTranslationsSurvive(Func<ExpectedUnit, bool> pick, SpeakerChange speaker = SpeakerChange.Keep)
    {
        var encoder = _encoder.Value;
        var checkedAny = false;
        foreach (var subject in Subjects().Where(s => s.Script.Units.Any(pick)))
        {
            checkedAny = true;
            var original = Extract(subject, _context.Value);
            var rewritten = subject.Script.Units.Select((u, i) => (u, i)).Where(p => pick(p.u)).Select(p => p.i).ToHashSet();
            var translated = original.Select((unit, i) => rewritten.Contains(i) ? Translated(unit, subject.Script.Units[i], speaker) : unit).ToList();
            var contentBefore = subject.File.Content.ToArray();
            var unitsBefore = translated.ToList();

            var rebuilt = subject.Format.Rebuild(subject.File, translated, encoder, _context.Value);

            AssertSameBytes(subject.Describe("Rebuild não deve alterar o arquivo original"), contentBefore, subject.File.Content.ToArray());
            Assert.Equal(unitsBefore, translated);
            AssertSameBytes(subject.Describe("Rebuild repetido"), rebuilt, subject.Format.Rebuild(subject.File, translated, encoder, _context.Value));
            Assert.False(subject.File.Content.Span.SequenceEqual(rebuilt), subject.Describe("a tradução não mudou nenhum byte"));
            AssertOnlyRewrittenChanged(subject, subject.File.Content, rebuilt, original, rewritten);

            var again = Extract(new Subject(subject.Script, subject.Format, new ScriptFile(subject.File.Name, rebuilt)), _context.Value);
            Assert.Equal(original.Select(u => u.Id), again.Select(u => u.Id));
            for (var i = 0; i < original.Count; i++)
            {
                var expected = subject.Script.Units[i];
                var picked = rewritten.Contains(i);
                var wanted = picked ? expected.Translation! : original[i].Source;
                var wantedSpeaker = picked && speaker != SpeakerChange.Keep ? expected.SpeakerTranslation : original[i].Speaker;
                Assert.Equal(subject.Describe($"{original[i].Id}|{original[i].Kind}|{wantedSpeaker}|{wanted}"), subject.Describe($"{again[i].Id}|{again[i].Kind}|{again[i].Speaker}|{again[i].Source}"));
                Assert.All(expected.Markers, marker => Assert.Contains(marker, again[i].Source, StringComparison.Ordinal));
            }

            var back = again.Select((u, i) => rewritten.Contains(i) ? u with { Translation = original[i].Source, Speaker = original[i].Speaker, Status = TranslationStatus.Translated } : u).ToList();
            // Os bytes não voltam iguais: a reescrita mínima da engine pode acrescentar o texto novo e deixar o antigo órfão (o YSTB cresce a
            // cada reescrita). O que precisa voltar é o conteúdo que o tradutor vê.
            var backScript = new ScriptFile(subject.File.Name, subject.Format.Rebuild(new ScriptFile(subject.File.Name, rebuilt), back, encoder, _context.Value));
            AssertOnlyRewrittenChanged(subject, rebuilt, backScript.Content, original, rewritten);
            Assert.Equal(
                original.Select(u => subject.Describe($"{u.Id}|{u.Kind}|{u.Speaker}|{u.Source}")),
                Extract(new Subject(subject.Script, subject.Format, backScript), _context.Value).Select(u => subject.Describe($"{u.Id}|{u.Kind}|{u.Speaker}|{u.Source}")));
        }

        Assert.True(checkedAny, "Nenhum script sintético tem a unidade que este teste confere.");
    }

    /// <summary>
    /// A regra 3 do <c>CLAUDE.md</c> vista de dentro do script: pelas faixas de <see cref="DescribeScript"/>, só as das unidades reescritas, os campos
    /// compartilhados e as áreas que crescem podem diferir. O resto, inclusive o argumento de uma unidade que a tradução não tocou, sai igual.
    /// </summary>
    private void AssertOnlyRewrittenChanged(Subject subject, ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after, IReadOnlyList<TextUnit> units, ISet<int> rewritten)
    {
        var old = DescribeScript(new ScriptFile(subject.File.Name, before), units);
        var current = DescribeScript(new ScriptFile(subject.File.Name, after), units);

        AssertTiles(subject, "original", before, old);
        AssertTiles(subject, "reconstruído", after, current);
        Assert.Equal(old.Select(r => r.Name), current.Select(r => r.Name));
        Assert.All(rewritten, i => Assert.True(old.Any(r => r.Unit == i), subject.Describe($"nenhuma faixa de DescribeScript é da unidade {i}, que foi reescrita.")));
        Assert.All(old.Where(r => r.Unit is not null), r => Assert.InRange(r.Unit!.Value, 0, units.Count - 1));

        for (var i = 0; i < old.Count; i++)
        {
            var region = old[i];
            var changed = current[i];
            var mayChange = region.Unit is { } owner ? rewritten.Contains(owner) : (region.Shared || region.Appends) && rewritten.Count > 0;
            if (!mayChange)
            {
                Assert.True(region.Bytes.Span.SequenceEqual(changed.Bytes.Span), subject.Describe($"a faixa '{region.Name}' não é de nenhuma unidade reescrita e mudou ({region.Bytes.Length} bytes contra {changed.Bytes.Length})."));
            }
            else if (region.Appends)
            {
                Assert.True(changed.Bytes.Span.StartsWith(region.Bytes.Span), subject.Describe($"a faixa '{region.Name}' só pode crescer no fim, e o começo dela mudou."));
            }
            else
            {
                Assert.True(region.Bytes.Length == changed.Bytes.Length, subject.Describe($"a faixa '{region.Name}' mudou de tamanho ({region.Bytes.Length} bytes contra {changed.Bytes.Length})."));
            }
        }
    }

    private static void AssertTiles(Subject subject, string which, ReadOnlyMemory<byte> content, IReadOnlyList<ScriptRegion> regions)
    {
        var joined = regions.SelectMany(r => r.Bytes.ToArray()).ToArray();
        Assert.True(content.Span.SequenceEqual(joined), subject.Describe($"as faixas de DescribeScript não cobrem o script {which} do começo ao fim ({joined.Length} bytes contra {content.Length})."));
    }

    [Fact]
    public void Rebuild_recusa_ids_de_outro_script_e_ids_repetidos()
    {
        var encoder = _encoder.Value;
        foreach (var subject in Subjects())
        {
            var first = Extract(subject, _context.Value)[0];

            Assert.Throws<ArgumentException>(() => subject.Format.Rebuild(subject.File, [first with { Id = "outro-script:000000" }], encoder, _context.Value));
            Assert.Throws<ArgumentException>(() => subject.Format.Rebuild(subject.File, [first, first], encoder, _context.Value));
        }
    }

    [Fact]
    public void Rebuild_recusa_unidade_que_nao_bate_com_o_script_citando_o_id()
    {
        var encoder = _encoder.Value;
        foreach (var subject in Subjects().Where(s => s.Script.Units.Any(u => u.Translation is not null)))
        {
            var index = subject.Script.Units.ToList().FindIndex(u => u.Translation is not null);
            var unit = Extract(subject, _context.Value)[index] with { Translation = subject.Script.Units[index].Translation, Status = TranslationStatus.Translated };

            var wrongSource = Assert.Throws<InvalidDataException>(() => subject.Format.Rebuild(subject.File, [unit with { Source = unit.Source + " (de outra versão)" }], encoder, _context.Value));
            var noLocator = Assert.Throws<InvalidDataException>(() => subject.Format.Rebuild(subject.File, [unit with { Locator = null }], encoder, _context.Value));

            Assert.Contains(unit.Id, wrongSource.Message, StringComparison.Ordinal);
            Assert.Contains(unit.Id, noLocator.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rebuild_recusa_traducao_que_o_codificador_nao_grava_citando_o_id()
    {
        var encoder = _encoder.Value;
        Assert.NotEmpty(encoder.FindUnencodable(UnencodableText));
        foreach (var subject in Subjects().Where(s => s.Script.Units.Any(u => u.Translation is not null)))
        {
            var index = subject.Script.Units.ToList().FindIndex(u => u.Translation is not null);
            var unit = Extract(subject, _context.Value)[index] with { Translation = UnencodableText, Status = TranslationStatus.Translated };

            var ex = Assert.Throws<EncoderFallbackException>(() => subject.Format.Rebuild(subject.File, [unit], encoder, _context.Value));

            Assert.Contains(unit.Id, ex.Message, StringComparison.Ordinal);
        }
    }

    private IGameTitleSource TitleSource()
    {
        if (Engine is IGameTitleSource source)
        {
            return source;
        }

        Assert.Skip($"A engine '{Engine.Id}' não implementa IGameTitleSource.");
        throw new UnreachableException();
    }

    [Fact]
    public void Titulo_e_lido_da_pasta_descompactada()
    {
        var source = TitleSource();
        var folder = NewFolder();
        var title = WriteSyntheticTitle(folder);

        var file = source.ReadTitle(folder);

        Assert.NotNull(file);
        Assert.Equal(title, file.Title);
        Assert.StartsWith(folder, file.Path, StringComparison.OrdinalIgnoreCase);
        AssertSameBytes("arquivo do título", File.ReadAllBytes(file.Path), file.Content);
    }

    [Fact]
    public void Titulo_de_pasta_sem_o_arquivo_ou_inexistente_devolve_nulo()
    {
        var source = TitleSource();

        Assert.Null(source.ReadTitle(NewFolder()));
        Assert.Null(source.ReadTitle(Path.Combine(Root, "nao-existe")));
    }

    [Fact]
    public void ReadTitle_nao_grava_nada_na_pasta()
    {
        var source = TitleSource();
        var folder = NewFolder();
        WriteSyntheticTitle(folder);
        var before = Snapshot(folder);

        source.ReadTitle(folder);

        Assert.Equal(before, Snapshot(folder));
    }

    [Fact]
    public void WithTitle_com_o_titulo_original_devolve_o_arquivo_byte_a_byte_igual()
    {
        var source = TitleSource();
        var folder = NewFolder();
        var title = WriteSyntheticTitle(folder);
        var file = source.ReadTitle(folder)!;

        AssertSameBytes("título original", file.Content, source.WithTitle(file, title, _encoder.Value));
    }

    [Fact]
    public void WithTitle_troca_o_titulo_e_volta_ao_original_byte_a_byte()
    {
        var source = TitleSource();
        var folder = NewFolder();
        var title = WriteSyntheticTitle(folder);
        var file = source.ReadTitle(folder)!;
        var contentBefore = file.Content.ToArray();

        var changed = source.WithTitle(file, NewTitle, _encoder.Value);

        AssertSameBytes("WithTitle não deve alterar o arquivo lido", contentBefore, file.Content);
        var otherFolder = NewFolder();
        WriteSyntheticTitle(otherFolder);
        var otherFile = source.ReadTitle(otherFolder)!;
        File.WriteAllBytes(otherFile.Path, changed);
        var reread = source.ReadTitle(otherFolder)!;
        Assert.Equal(NewTitle, reread.Title);
        AssertSameBytes("ida e volta do título", contentBefore, source.WithTitle(reread, title, _encoder.Value));
    }

    [Fact]
    public void WithTitle_recusa_titulo_que_o_codificador_nao_grava()
    {
        var source = TitleSource();
        var folder = NewFolder();
        WriteSyntheticTitle(folder);
        var file = source.ReadTitle(folder)!;

        Assert.Throws<EncoderFallbackException>(() => source.WithTitle(file, UnencodableText, _encoder.Value));
    }
}
