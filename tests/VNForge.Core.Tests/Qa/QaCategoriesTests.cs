using VNForge.Core.Qa;

namespace VNForge.Core.Tests.Qa;

public sealed class QaCategoriesTests
{
    [Fact]
    public void Toda_regra_do_qa_padrao_tem_categoria_propria()
    {
        var uncategorized = QaEngine.Default.Rules.Where(r => QaCategories.Of(r.Id) == QaCategories.Other).Select(r => r.Id);

        Assert.Empty(uncategorized);
    }
}
