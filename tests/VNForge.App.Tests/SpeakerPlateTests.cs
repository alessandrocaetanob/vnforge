using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// A placa da caixa do jogo mostra o nome que o build grava na caixa de nome (#464): a tradução do Glossário, ou o falante escolhido na linha (#437), e não o nome do jogo.
/// A igualdade com o que a engine recebe do build está em <c>ProjectBuilderTests.Placa_da_caixa_do_jogo_mostra_o_nome_que_o_build_entrega_a_engine</c>; aqui vale que cada tela usa a mesma regra.
/// </summary>
public sealed class SpeakerPlateTests : IDisposable
{
    private const string Perfil = """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""";

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        pasta.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TextUnit Linha(string? falante, string? escolhido = null, string traducao = "Bom dia!", int n = 0) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", traducao, TranslationStatus.Translated) { ChosenSpeaker = escolhido };

    private static SpeakerNaming Nomes(string encoder = "sjis") =>
        new(new CharacterBook([new CharacterEntry("美咲", "Misaki")]), TextEncoderRegistry.Default.Create(encoder));

    private ProjectFolder Projeto(string nome, params TextUnit[] linhas)
    {
        var folder = ProjectFolder.Open(SampleProjects.Create(pasta.SubFolder(nome), "Teste", TranslationStatus.Translated));
        folder.SaveFormatting(FormattingProfileSerializer.Parse(Perfil));
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki")]);
        folder.SaveText("yst00001", linhas);
        return folder;
    }

    [Theory]
    [InlineData("sjis")]
    [InlineData("cp1252")]
    public void Placa_mostra_a_traducao_do_cadastro_no_lugar_do_nome_do_jogo(string encoder)
    {
        var vm = new TextboxPreviewViewModel(Linha("美咲"), FormattingProfileSerializer.Parse(Perfil), "Bom dia!", naming: Nomes(encoder));

        Assert.Equal("Misaki", vm.Layout!.Speaker);
    }
}
