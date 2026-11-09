using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.Core.Eval;

namespace VNForge.AI.Tests.Eval;

public sealed class EvalChoiceTests : IDisposable
{
    private readonly EvalProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void A_configuracao_escolhida_vira_o_ai_do_projeto()
    {
        EvalChoice.Apply(_project.Folder, new EvalConfig("C", ClaudeModels.Sonnet, "high", 12, false));

        var ai = _project.Folder.LoadConfig().Ai;
        Assert.Equal(ClaudeModels.Sonnet, ai.Model);
        Assert.Equal("high", ai.Effort);
        Assert.Equal(12, ai.ContextBefore);
        Assert.Equal(12, ai.ContextAfter);
        Assert.Equal("anthropic", ai.Provider);
        Assert.Equal(30, ai.BatchSize);
    }
}
