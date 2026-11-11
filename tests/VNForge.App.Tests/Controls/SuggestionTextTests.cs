using Avalonia.Controls;
using Avalonia.Controls.Documents;
using VNForge.App.Controls;

namespace VNForge.App.Tests.Controls;

/// <summary>O texto da sugestão do editor (#532): no inspetor o começo igual fica sem marca e só o resto aparece riscado e depois proposto, como no quadro.</summary>
public sealed class SuggestionTextTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task<IReadOnlyList<(string Text, string? Style)>> Runs(string current, string proposed, bool showRemoved) => fixture.Session.Dispatch<IReadOnlyList<(string, string?)>>(
        () =>
        {
            var block = new TextBlock();
            SuggestionText.SetShowRemoved(block, showRemoved);
            SuggestionText.SetCurrent(block, current);
            SuggestionText.SetProposed(block, proposed);
            return block.Inlines!.OfType<Run>().Select(r => (r.Text ?? string.Empty, r.Classes.FirstOrDefault())).ToList();
        },
        TestContext.Current.CancellationToken);

    [Fact]
    public async Task O_comeco_igual_para_na_palavra_inteira()
    {
        var runs = await Runs("Os dois chegaram.", "Os doisinho chegaram.", showRemoved: true);

        Assert.Equal([("Os ", null), ("dois chegaram.", "sug-del"), (" ", null), ("doisinho chegaram.", "ins")], runs);
    }
}
