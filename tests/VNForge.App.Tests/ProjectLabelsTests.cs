using VNForge.App.Services;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ProjectLabelsTests
{
    [Theory]
    [InlineData("yuris", 500, "YU-RIS · YPF v500")]
    [InlineData("yuris", null, "YU-RIS")]
    [InlineData("kirikiri", null, "kirikiri")]
    public void Engine(string id, int? version, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Engine(id, version));

    [Theory]
    [InlineData("claude-opus-5-5", "Claude Opus 5.5")]
    [InlineData("claude-fable-5-1", "Claude Fable 5.1")]
    [InlineData("claude-haiku-5-5", "Claude Haiku 5.5")]
    [InlineData("outro", "outro")]
    public void Modelo(string id, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Model(id));

    [Theory]
    [InlineData("fold", "Remover acentos (fold)")]
    [InlineData("cp1252", "CP1252 (patch opcional da fonte)")]
    [InlineData("sjis", "Shift-JIS puro")]
    [InlineData("remap", "Remapear glifos (remap)")]
    [InlineData("zzz", "zzz")]
    public void Acentuacao(string id, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Encoding(id));

    [Theory]
    [InlineData(48211, "48.211")]
    [InlineData(0, "0")]
    public void Numero_em_pt_BR(int n, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Number(n));

    [Theory]
    [InlineData(TranslationStatus.Untranslated, "Em japonês")]
    [InlineData(TranslationStatus.Machine, "IA")]
    [InlineData(TranslationStatus.Translated, "Sua")]
    [InlineData(TranslationStatus.Reviewed, "Pronta")]
    [InlineData(TranslationStatus.Approved, "Pronta")]
    public void Status_em_portugues(TranslationStatus status, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Status(status));

    [Theory]
    [InlineData(CharacterGender.Unknown, "Não definido")]
    [InlineData(CharacterGender.Female, "Feminino")]
    [InlineData(CharacterGender.Male, "Masculino")]
    [InlineData(CharacterGender.Other, "Outro")]
    public void Genero(CharacterGender gender, string esperado) =>
        Assert.Equal(esperado, ProjectLabels.Gender(gender));
}
