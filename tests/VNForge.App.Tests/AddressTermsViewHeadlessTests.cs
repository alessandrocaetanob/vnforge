using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba Tratamentos do Glossário na tela (#470): a tabela mostra o par salvo, e abrir a página não suja o projeto.</summary>
public sealed class AddressTermsViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Fact]
    public async Task A_aba_mostra_o_par_salvo_nos_seletores_e_nos_campos_sem_sujar_o_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("健太", "Kenta")]);
        folder.SaveAddressTerms([new AddressTerm("健太", "美咲", "美咲ちゃん", "Misaki-chan")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), SelectedTab = GlossaryTab.Address };

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new Views.GlossaryView { DataContext = vm } };
                window.Show();
                ScreenRenderer.Settle(window);

                T Find<T>(string name) where T : Control =>
                    window.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);

                Assert.Equal("健太", Find<ComboBox>("Quem fala").SelectedItem);
                Assert.Equal("美咲", Find<ComboBox>("Com quem fala").SelectedItem);
                Assert.Equal("美咲ちゃん", Find<TextBox>("Como chama, em japonês").Text);
                Assert.Equal("Misaki-chan", Find<TextBox>("Como chama, na tradução").Text);
                Assert.Equal("Pedir os tratamentos à IA", AutomationProperties.GetName(Find<Button>("Pedir os tratamentos à IA")));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Buscar no glossário");
                Assert.False(vm.IsDirty);
                Assert.Equal("美咲", vm.AddressTerms.Rows[0].To);
                window.Close();
            },
            CancellationToken.None);
    }
}
