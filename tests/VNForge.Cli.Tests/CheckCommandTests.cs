using System.Text.Json;
using System.Text.RegularExpressions;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R4")]
public sealed partial class CheckCommandTests : IDisposable
{
    private const string ScriptKey = "yst00001";
    private const string Forbidden = "proibida";
    private const string ForbiddenRule = "palavra-proibida";
    private const string WarningRule = "aviso-a";
    private const string ErrorRule = "erro-b";
    private const string SeverityFlag = "--severity";
    private const string RulesFlag = "--rules";
    private const string FixFlag = "--fix";
    private const string DoubleSpaced = "Olá  mundo";

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;

    public CheckCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("jogo", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, string translation) =>
        new($"{ScriptKey}:{n:D6}", TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated);

    private void Save(params string[] translations) =>
        _project.SaveText(ScriptKey, [.. translations.Select((t, i) => Unit(i + 1, t))]);

    private void UseRules(params IQaRule[] rules) => _cli.Cli.Qa = new QaEngine(rules);

    private Task<RunResult> Check(params string[] args) =>
        _cli.RunAsync(["check", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Arquivo_de_palavras_ignoradas_ilegivel_nao_derruba_a_conferencia_e_o_texto_avisa()
    {
        Save("Olá mundo.");
        UseRules(new SpellingRule());
        File.WriteAllText(_project.IgnoredWordsPath, "<<<<<<< HEAD");

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ignored-words.json", result.Output);
        Assert.Contains("ignorado", result.Output);
    }

    private const int EllipsisLines = 1234;
    private const string GroupedEllipsis = "Reticências (Caractere …): 1.234 linhas com erro. A estratégia cp1252 não grava '…'. vnf check --fix troca para Três pontos.";

    private ProjectFolder EllipsisProject()
    {
        var project = ProjectFolder.Create(Path.Combine(_cli.Root, "cp1252"), _project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        project.SaveFormatting(FormattingProfile.Default with { Typography = new TypographyRule(Ellipsis: EllipsisStyle.Character) });
        UseRules(new FormattingConflictRule(), new UnencodableCharacterRule());
        project.SaveText(ScriptKey, [.. Enumerable.Range(1, EllipsisLines).Select(n => Unit(n, "Espera...")), Unit(EllipsisLines + 1, "Sem reticências")]);
        return project;
    }

    private Task<RunResult> CheckEllipsis(ProjectFolder project, params string[] args) =>
        _cli.RunAsync(["check", "--project", project.Root, .. args], TestContext.Current.CancellationToken);

    [GeneratedRegex("Mesmo original")]
    private static partial Regex SameOriginHeading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string[] Lines(RunResult result) =>
        [.. result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())];

    private void SaveUntranslated(string key, int count) =>
        _project.SaveText(key, [.. Enumerable.Range(1, count).Select(n =>
            new TextUnit($"{key}:{n:D6}", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated))]);

    private sealed class TranslationIs(string id, QaSeverity severity, string text) : UnitQaRule
    {
        public override string Id => id;

        public override string Description => "Teste.";

        public override QaSeverity Severity => severity;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == text)
            {
                yield return $"tradução '{text}' recusada";
            }
        }
    }
}
