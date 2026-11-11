using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O fluxo do Início ao resto do app: "Abrir projeto" leva ao Painel e "Continuar em" ao Editor, no script em que o usuário parou.</summary>
public class MainWindowStartFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)));

    private static string TwoScripts(TempFolder temp) =>
        StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines:
        [
            ("yst00001", TranslationStatus.Approved),
            ("yst00002", TranslationStatus.Machine),
            ("yst00003", TranslationStatus.Untranslated),
        ]);
}
