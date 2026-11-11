using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A coluna das cenas recolhível do Editor (#393): onde a lista aparece, o estado lembrado entre sessões, o atalho e a faixa com o nome da cena.</summary>
public sealed class ScenesCollapseTests
{
    [Fact]
    public void Um_Editor_novo_volta_recolhido_quando_a_sessao_anterior_terminou_assim()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        new EditorViewModel(layoutMemory: new EditorLayoutMemory(config)).ToggleScenesCommand.Execute(null);

        var next = new EditorViewModel(layoutMemory: new EditorLayoutMemory(config));

        Assert.True(next.IsScenesCollapsed);
    }
}
