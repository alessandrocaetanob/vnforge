using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R3")]
public class FormattingProfileSerializerTests
{
    [Fact]
    public void Serializa_e_le_de_volta_o_mesmo_perfil()
    {
        var profile = new FormattingProfile([TextUnitKind.Narration], new WrapRule(true, 30, 4, CharWidthMode.Uniform));

        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Kinds, back.Kinds);
        Assert.Equal(profile.Wrap, back.Wrap);
    }

    [Fact]
    public void Perfil_de_exemplo_em_samples_le_sem_erro()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "formatting.json");

        var profile = FormattingProfileSerializer.Parse(File.ReadAllText(path));

        Assert.True(profile.Wrap.Enabled);
        Assert.Equal(FormattingProfileSerializer.Serialize(profile), FormattingProfileSerializer.Serialize(FormattingProfileSerializer.Parse(File.ReadAllText(path))));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VNForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
