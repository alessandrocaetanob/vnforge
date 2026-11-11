using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class TextEncoderRegistryTests
{

    [Fact]
    public void Cada_encoder_entra_com_uma_linha()
    {
        var registry = new TextEncoderRegistry().Register("fold", () => new FoldTextEncoder());

        Assert.IsType<FoldTextEncoder>(registry.Create("fold"));
        Assert.Equal(["fold"], registry.AvailableIds);
    }
}
