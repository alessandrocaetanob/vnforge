using VNForge.App.Services;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ProjectLabelsTests
{

    [Theory]
    [InlineData(48211, "48.211")]
    [InlineData(0, "0")]
    public void Numero_em_pt_BR(int n, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Number(n));

    [Theory]
    [InlineData(TranslationStatus.Untranslated, "Em japonês")]
    [InlineData(TranslationStatus.Keep, "Não traduzir")]
    [InlineData(TranslationStatus.Machine, "IA")]
    [InlineData(TranslationStatus.Translated, "Sua")]
    [InlineData(TranslationStatus.Reviewed, "Pronta")]
    [InlineData(TranslationStatus.Approved, "Pronta")]
    public void Status_em_portugues(TranslationStatus status, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Status(status));
}
