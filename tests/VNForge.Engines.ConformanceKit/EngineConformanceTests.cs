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
/// <see cref="WriteSyntheticTitle"/>. Uma engine nova herda e implementa os cinco membros abstratos; o resto roda sozinho.
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

    private static List<TextUnit> WithTranslations(List<TextUnit> extracted, ConformanceScript script, Func<ExpectedUnit, bool> pick) =>
        [.. extracted.Select((unit, i) => pick(script.Units[i]) ? unit with { Translation = script.Units[i].Translation, Status = TranslationStatus.Translated } : unit)];

    private static void AssertSameBytes(string label, ReadOnlyMemory<byte> expected, byte[] actual) =>
        Assert.True(expected.Span.SequenceEqual(actual), $"{label}: o script reconstruído difere do original ({expected.Length} bytes contra {actual.Length}).");

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

    [Fact]
    public void Os_scripts_sinteticos_cobrem_falante_escolha_e_marcador()
    {
        var units = _scripts.Value.SelectMany(s => s.Units).ToList();

        Assert.NotEmpty(units);
        Assert.Contains(units, u => u.Speaker is not null && u.Translation is not null);
        Assert.Contains(units, u => u.Kind == TextUnitKind.Choice && u.Translation is not null);
        Assert.Contains(units, u => u.Markers.Count > 0 && u.Translation is not null);
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
    public void Escolhas_sobrevivem_a_traducao()
    {
        AssertTranslationsSurvive(u => u.Kind == TextUnitKind.Choice && u.Translation is not null);
    }

    [Fact]
    public void Marcadores_sobrevivem_a_traducao()
    {
        AssertTranslationsSurvive(u => u.Markers.Count > 0 && u.Translation is not null);
    }

    private void AssertTranslationsSurvive(Func<ExpectedUnit, bool> pick)
    {
        var encoder = _encoder.Value;
        var checkedAny = false;
        foreach (var subject in Subjects().Where(s => s.Script.Units.Any(pick)))
        {
            checkedAny = true;
            var original = Extract(subject, _context.Value);
            var translated = WithTranslations(original, subject.Script, pick);
            var contentBefore = subject.File.Content.ToArray();
            var unitsBefore = translated.ToList();

            var rebuilt = subject.Format.Rebuild(subject.File, translated, encoder, _context.Value);

            AssertSameBytes(subject.Describe("Rebuild não deve alterar o arquivo original"), contentBefore, subject.File.Content.ToArray());
            Assert.Equal(unitsBefore, translated);
            AssertSameBytes(subject.Describe("Rebuild repetido"), rebuilt, subject.Format.Rebuild(subject.File, translated, encoder, _context.Value));
            Assert.False(subject.File.Content.Span.SequenceEqual(rebuilt), subject.Describe("a tradução não mudou nenhum byte"));

            var again = Extract(new Subject(subject.Script, subject.Format, new ScriptFile(subject.File.Name, rebuilt)), _context.Value);
            Assert.Equal(original.Select(u => u.Id), again.Select(u => u.Id));
            for (var i = 0; i < original.Count; i++)
            {
                var expected = subject.Script.Units[i];
                var wanted = pick(expected) ? expected.Translation! : original[i].Source;
                Assert.Equal(subject.Describe($"{original[i].Id}|{original[i].Kind}|{original[i].Speaker}|{wanted}"), subject.Describe($"{again[i].Id}|{again[i].Kind}|{again[i].Speaker}|{again[i].Source}"));
                Assert.All(expected.Markers, marker => Assert.Contains(marker, again[i].Source, StringComparison.Ordinal));
            }

            var back = again.Select((u, i) => pick(subject.Script.Units[i]) ? u with { Translation = original[i].Source, Status = TranslationStatus.Translated } : u).ToList();
            // Os bytes não voltam iguais: a reescrita mínima da engine pode acrescentar o texto novo e deixar o antigo órfão (o YSTB cresce a
            // cada reescrita). O que precisa voltar é o conteúdo que o tradutor vê.
            var backScript = new ScriptFile(subject.File.Name, subject.Format.Rebuild(new ScriptFile(subject.File.Name, rebuilt), back, encoder, _context.Value));
            Assert.Equal(
                original.Select(u => subject.Describe($"{u.Id}|{u.Kind}|{u.Speaker}|{u.Source}")),
                Extract(new Subject(subject.Script, subject.Format, backScript), _context.Value).Select(u => subject.Describe($"{u.Id}|{u.Kind}|{u.Speaker}|{u.Source}")));
        }

        Assert.True(checkedAny, "Nenhum script sintético tem a unidade que este teste confere.");
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
