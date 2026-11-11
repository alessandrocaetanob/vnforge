using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public partial class StylePromptTests
{
    [System.Text.RegularExpressions.GeneratedRegex("\"id\":\"([^\"]+)\"")]
    private static partial System.Text.RegularExpressions.Regex RequestId();

    private sealed class StubClient : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var ids = RequestId().Matches(request.User).Select(m => m.Groups[1].Value).Distinct();
            var items = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"translation\":\"T-{id}\"}}"));
            return Task.FromResult(new ClaudeReply("end_turn", $"{{\"translations\":[{items}]}}"));
        }
    }

    private static TextUnit Unit(string id, string source, string? speaker = null, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation,
            translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static readonly CharacterBook Cast = new([new CharacterEntry("美咲", "Misaki", CharacterGender.Female)]);

    private static TextUnit[] Script() =>
    [
        Unit("l1", "一行目", translation: "Um"),
        Unit("l2", "二行目", translation: "Dois"),
        Unit("l3", "三行目", translation: "Três"),
        Unit("l4", "四行目", "美咲"),
        Unit("l5", "五行目"),
        Unit("l6", "六行目"),
        Unit("l7", "七行目"),
    ];

    private static async Task<ClaudeRequest> Translate(TranslationBatch batch)
    {
        var client = new StubClient();
        await new ClaudeTranslationProvider(client).TranslateAsync(batch, CancellationToken.None);
        return Assert.Single(client.Requests);
    }

    [Fact]
    public async Task O_guia_de_estilo_e_as_escolhas_vao_no_prompt_de_sistema()
    {
        var style = new StyleConfig
        {
            Honorifics = HonorificsPolicy.Keep,
            Address = AddressForm.Tu,
            Narrator = new NarratorStyle { Who = "Kenta, o protagonista", Person = NarratorPerson.Third, Gender = NarratorGender.Male },
            TranslateOnomatopoeia = false,
            WesternNameOrder = false,
        };
        var builder = new TranslationBatchBuilder([], Cast, "Linguagem jovem e natural.", style: style);

        var request = await Translate(builder.Build([Script()[3]]));

        Assert.Contains("Linguagem jovem e natural.", request.System);
        Assert.Contains("Estilo da tradução:", request.System);
        Assert.Contains("mantenha -san, -kun, -chan, -sama e senpai", request.System);
        Assert.Contains("\"tu\"", request.System);
        Assert.Contains("Kenta, o protagonista, em terceira pessoa; gênero masculino", request.System);
        Assert.Contains("mantenha como soam em japonês", request.System);
        Assert.Contains("ordem japonesa", request.System);
    }

    [Theory]
    [InlineData(HonorificsPolicy.Keep, "mantenha -san")]
    [InlineData(HonorificsPolicy.Remove, "remova -san")]
    [InlineData(HonorificsPolicy.Adapt, "adapte. -chan e -kun saem")]
    public async Task Cada_escolha_de_honorifico_tem_a_sua_frase(HonorificsPolicy policy, string expected)
    {
        var builder = new TranslationBatchBuilder([], Cast, style: StyleConfig.Default with { Honorifics = policy });

        var request = await Translate(builder.Build([Script()[3]]));

        Assert.Contains(expected, request.System);
        Assert.Single(StylePhrases(request.System), line => line.StartsWith("- Honoríficos:", StringComparison.Ordinal));
    }

    private static IEnumerable<string> StylePhrases(string system) =>
        system.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("- ", StringComparison.Ordinal));
}
