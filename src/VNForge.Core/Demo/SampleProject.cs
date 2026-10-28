using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Demo;

/// <summary>
/// A cena de exemplo da primeira vez: um projeto sintético de 30 falas em três cenas curtas, para quem abre o app sem jogo e sem chave de API
/// ver cada tela funcionando. As falas são originais, escritas para o exemplo; nenhum texto ou arquivo de jogo. A pasta leva o marcador
/// <see cref="MarkerFileName"/>, que diz ao app que não há jogo para testar nem injetar.
/// </summary>
public static class SampleProject
{
    public const string Name = "Cena de exemplo";
    public const string FolderName = "cena-de-exemplo";
    public const string MarkerFileName = "exemplo.txt";
    public const int LineCount = 30;

    private const string MarkerText = "Cena de exemplo do VNForge: o texto é sintético, não há jogo nesta pasta e o build e a injeção não se aplicam.";

    private const string Misaki = "美咲";
    private const string Kenta = "健太";
    private const string Teacher = "先生";

    private static readonly (string Script, string Name, string Folder)[] Scenes =
    [
        ("yst00001", "Cena 1 · A biblioteca", "userscript/01_biblioteca"),
        ("yst00002", "Cena 2 · A sala dos professores", "userscript/02_professores"),
        ("yst00003", "Cena 3 · A volta para casa", "userscript/03_volta"),
    ];

    /// <summary>Script, quem fala (nulo é narração), o original em japonês e o português que a IA de demonstração devolve.</summary>
    private static readonly (int Scene, string? Speaker, string Source, string Translation)[] Lines =
    [
        (0, null, "放課後の図書室には、私たちのほかに誰もいなかった。", "Depois da aula, a biblioteca estava vazia, só nós dois."),
        (0, Misaki, "「ねえ、この本、もう読み終わった？」", "Ei, você já terminou este livro?"),
        (0, Kenta, "「まだ半分くらいかな。続きが気になるんだけど」", "Só na metade. Estou curioso para saber o fim."),
        (0, Misaki, "「じゃあ、明日までに読んで感想を聞かせてね」", "Então leia até amanhã e me conte o que achou."),
        (0, Kenta, "「明日までは無理だよ。テストもあるし」", "Até amanhã é impossível. Ainda tem a prova."),
        (0, null, "窓の外では、夕焼けが少しずつ色を変えていた。", "Lá fora, o pôr do sol mudava de cor aos poucos."),
        (0, Misaki, "「それなら、週末に一緒に読もう」", "Então vamos ler juntos no fim de semana."),
        (0, Kenta, "「一緒に？ 図書室は休みだぞ」", "Juntos? A biblioteca fecha no fim de semana."),
        (0, Misaki, "「大丈夫。先生に鍵を借りるから」", "Sem problema. Eu peço a chave emprestada ao professor."),
        (0, null, "彼女はそう言って、楽しそうに笑った。", "Ela disse isso e riu, animada."),
        (1, null, "翌日、二人は職員室の前に立っていた。", "No dia seguinte, os dois estavam em frente à sala dos professores."),
        (1, Kenta, "「本当に頼むのか？ 怒られないか？」", "Vai mesmo pedir? E se ele ficar bravo?"),
        (1, Misaki, "「まずは聞いてみなきゃ。失礼します！」", "Primeiro temos que perguntar. Com licença!"),
        (1, Teacher, "「おや、二人そろってどうした？」", "Oh, os dois juntos? O que houve?"),
        (1, Misaki, "「週末に図書室を使わせてください」", "Por favor, deixe-nos usar a biblioteca no fim de semana."),
        (1, Teacher, "「理由は？」", "Qual é o motivo?"),
        (1, Kenta, "「ええと……読書の、勉強会です」", "Bem... é um grupo de estudos de leitura."),
        (1, Teacher, "「ほう。勉強会なら、いいだろう」", "Hum. Se é um grupo de estudos, tudo bem."),
        (1, null, "先生は引き出しから古い鍵を取り出した。", "O professor tirou uma chave velha da gaveta."),
        (1, Teacher, "「ただし、片付けはきちんとするんだぞ」", "Mas arrumem tudo direitinho depois."),
        (2, null, "鍵を手にした美咲は、足取りが軽かった。", "Com a chave na mão, Misaki andava leve."),
        (2, Kenta, "「勉強会なんて、よく思いついたな」", "Grupo de estudos... como você pensou nisso?"),
        (2, Misaki, "「あれは健太くんが言ったんでしょ」", "Foi você que disse isso, Kenta."),
        (2, Kenta, "「……そうだった」", "...É verdade."),
        (2, null, "二人の笑い声が、夕暮れの道に響いた。", "A risada dos dois ecoou pela rua ao entardecer."),
        (2, Misaki, "「土曜日、楽しみだね」", "Estou ansiosa pelo sábado."),
        (2, Kenta, "「ああ。お菓子も持っていこう」", "É. Vamos levar uns doces também."),
        (2, Misaki, "「それは勉強会じゃなくて、ピクニックだよ」", "Isso já não é grupo de estudos, é piquenique."),
        (2, Kenta, "「いいじゃないか、少しくらい」", "Qual é, só um pouquinho."),
        (2, null, "こうして、小さな約束がひとつ増えた。", "E assim ganhamos mais uma pequena promessa."),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<TextUnit>>> Generated = new(Generate);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> BySource = new(
        () => Lines.ToDictionary(l => l.Source, l => l.Translation, StringComparer.Ordinal));

    /// <summary>Os textos por chave de script. A terceira fala da primeira cena vem como tradução da IA com um espaço duplo, para a Conferência ter o que mostrar.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts => Generated.Value;

    /// <summary>O português que a IA de demonstração devolve para o original <paramref name="source"/>; nulo quando a fala não é do exemplo.</summary>
    public static string? TranslationOf(string source) => BySource.Value.GetValueOrDefault(source);

    /// <summary>A pasta <paramref name="root"/> é uma cena de exemplo (tem o marcador gravado por <see cref="Create"/>).</summary>
    public static bool IsSample(string root)
    {
        try
        {
            var marker = Path.Combine(root, MarkerFileName);
            return File.Exists(marker) && File.ReadLines(marker).FirstOrDefault() == MarkerText;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Grava o projeto em <c>parent/cena-de-exemplo</c> e devolve a pasta. O projeto é montado numa pasta temporária ao lado e movido de uma vez
    /// no fim, então uma criação interrompida não deixa nada pela metade. Se a pasta já é uma cena de exemplo, devolve ela como está: o que a
    /// pessoa traduziu ali não se perde. Uma pasta com o mesmo nome em outro estado (só <c>vnforge.json</c>, só o marcador ou outro conteúdo)
    /// não é tocada: o método avisa em vez de sobrescrever.
    /// </summary>
    /// <exception cref="ProjectConfigException">A pasta existe e não é uma cena de exemplo completa.</exception>
    public static string Create(string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var root = Path.GetFullPath(Path.Combine(parent, FolderName));
        if (TryReuse(root))
        {
            return root;
        }

        var staging = Path.Combine(Path.GetFullPath(parent), $".{FolderName}-{Guid.NewGuid():N}");
        try
        {
            WriteTo(staging, root);
            try
            {
                Directory.Move(staging, root);
            }
            catch (IOException) when (TryReuse(root))
            {
                return root;
            }
        }
        finally
        {
            TryDelete(staging);
        }

        return root;
    }

    /// <summary>Verdadeiro quando <paramref name="root"/> já é uma cena de exemplo completa; falha com uma explicação quando existe em outro estado.</summary>
    private static bool TryReuse(string root)
    {
        var hasConfig = File.Exists(Path.Combine(root, ProjectFolder.ConfigFileName));
        var isSample = IsSample(root);
        if (hasConfig && isSample)
        {
            return true;
        }

        if (hasConfig)
        {
            throw new ProjectConfigException(
                $"Já existe um projeto na pasta '{FolderName}' que não é a cena de exemplo. Nada foi alterado; renomeie ou mova essa pasta e tente de novo.");
        }

        if (isSample)
        {
            throw new ProjectConfigException(
                $"A pasta '{FolderName}' tem o marcador da cena de exemplo, mas falta o {ProjectFolder.ConfigFileName}. Nada foi apagado, para não perder traduções; renomeie ou mova essa pasta e tente de novo.");
        }

        if (Directory.Exists(root))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new ProjectConfigException(
                    $"Já existe uma pasta '{FolderName}' com outro conteúdo. Nada foi alterado; renomeie ou mova essa pasta e tente de novo.");
            }

            Directory.Delete(root);
        }

        return false;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Limpeza de melhor esforço: uma pasta temporária que não saiu fica para trás sem atrapalhar, e a próxima criação usa outro nome.
        }
    }

    private static void WriteTo(string staging, string finalRoot)
    {
        var folder = ProjectFolder.Create(staging, new ProjectConfig(
            Name,
            new GameConfig(Path.Combine(finalRoot, "sem-jogo"), "exemplo.exe"),
            new EngineConfig("yuris", 500),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        foreach (var (key, units) in Texts)
        {
            folder.SaveText(key, units);
        }

        folder.SaveState(new PipelineState([], new Dictionary<string, StageState> { [ProjectExtractor.ExtractStage] = new(StageStatus.Completed) }));
        folder.SaveScriptNames(ScriptNames.Empty.WithDetected(Scenes.ToDictionary(s => s.Script, s => new DetectedScript(s.Name, s.Folder), StringComparer.Ordinal)));
        folder.SaveCharacters([new CharacterEntry(Misaki, "Misaki"), new CharacterEntry(Kenta, "Kenta"), new CharacterEntry(Teacher, "Professor")]);
        folder.SaveGlossary(
        [
            new GlossaryTerm("図書室", "biblioteca"),
            new GlossaryTerm("職員室", "sala dos professores"),
            new GlossaryTerm("勉強会", "grupo de estudos"),
        ]);
        File.WriteAllText(Path.Combine(folder.Root, MarkerFileName), MarkerText + "\n");
    }

    private static Dictionary<string, IReadOnlyList<TextUnit>> Generate()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        foreach (var (scene, (key, _, _)) in Scenes.Index())
        {
            var units = new List<TextUnit>();
            foreach (var line in Lines.Where(l => l.Scene == scene))
            {
                var id = $"{key}:{units.Count + 1:D4}";
                var kind = line.Speaker is null ? TextUnitKind.Narration : TextUnitKind.Dialogue;
                var machine = scene == 0 && units.Count == 2;
                units.Add(machine
                    ? new TextUnit(id, kind, line.Speaker, line.Source, line.Translation.Replace(". ", ".  ", StringComparison.Ordinal), TranslationStatus.Machine)
                    : new TextUnit(id, kind, line.Speaker, line.Source, null, TranslationStatus.Untranslated));
            }

            texts[key] = units;
        }

        return texts;
    }
}
