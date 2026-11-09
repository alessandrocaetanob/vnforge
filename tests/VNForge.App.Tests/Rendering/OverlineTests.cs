using System.Text.RegularExpressions;
using Avalonia.Controls;
using VNForge.App.Controls;

namespace VNForge.App.Tests.Rendering;

/// <summary>O overline é caixa alta em toda a tela (#580, G4): o texto fica como foi escrito e o <see cref="OverlineText"/> o desenha em maiúsculas.</summary>
public partial class OverlineTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [GeneratedRegex(@"<TextBlock\b(?:[^>""]|""[^""]*"")*Classes=""(?:[^""]*\s)?overline(?:\s[^""]*)?""")]
    private static partial Regex PlainOverlineTag();

    [Fact]
    public void Nenhum_XAML_usa_TextBlock_com_a_classe_overline()
    {
        var src = Path.Combine(RepoRoot(), "src", "VNForge.App");
        var offenders = Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .Where(f => PlainOverlineTag().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        Assert.True(offenders.Count == 0, "TextBlock com overline sai em caixa baixa; use c:OverlineText em: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task TextBlock_com_overline_e_letra_minuscula_e_apontado_e_OverlineText_nao()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var comum = new TextBlock { Classes = { "overline" }, Text = "Original" };
                var rotulo = new OverlineText { Classes = { "overline" }, Text = "Original" };
                var janela = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { comum, rotulo } } };
                try
                {
                    janela.Show();
                    ScreenRenderer.Settle(janela);

                    Assert.Equal(["Original"], OverlineProbe.LowercaseLabels(janela));
                    Assert.Throws<Xunit.Sdk.TrueException>(() => OverlineProbe.AssertUpperCase(janela));

                    janela.Content = new StackPanel { Children = { new OverlineText { Classes = { "overline" }, Text = "Original" } } };
                    ScreenRenderer.Settle(janela);
                    OverlineProbe.AssertUpperCase(janela);
                }
                finally
                {
                    janela.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OverlineText_mantem_o_texto_e_desenha_do_tamanho_das_maiusculas()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var rotulo = new OverlineText { Classes = { "overline" }, Text = "Só quando todas as condições baterem" };
                var maiusculas = new TextBlock { Classes = { "overline" }, Text = "SÓ QUANDO TODAS AS CONDIÇÕES BATEREM" };
                var janela = new Window { Width = 800, Height = 200, Content = new StackPanel { Children = { rotulo, maiusculas } } };
                try
                {
                    janela.Show();
                    ScreenRenderer.Settle(janela);

                    Assert.Equal("Só quando todas as condições baterem", rotulo.Text);
                    Assert.Equal("SÓ QUANDO TODAS AS CONDIÇÕES BATEREM", rotulo.DisplayText);
                    Assert.Equal(maiusculas.DesiredSize.Width, rotulo.DesiredSize.Width, 0.5);
                    Assert.True(rotulo.LetterSpacing > 0, "o estilo overline dá o espaçamento de 0,77 px");
                }
                finally
                {
                    janela.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VNForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("VNForge.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}
