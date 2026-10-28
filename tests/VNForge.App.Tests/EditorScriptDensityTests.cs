using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

/// <summary>A densidade Roteiro da grade do Editor (#460): começa nas colunas, e a escolha fica em <c>editor-layout.json</c> para o próximo app.</summary>
public sealed class EditorScriptDensityTests
{
    [Fact]
    public void Memoria_sem_pasta_comeca_nas_colunas_e_lembra_so_ate_o_app_fechar()
    {
        var memory = new EditorLayoutMemory();

        Assert.False(memory.ScriptDensity);
        memory.ScriptDensity = true;
        Assert.True(memory.ScriptDensity);
    }

    [Fact]
    public void Memoria_com_pasta_grava_a_densidade_em_editor_layout_json_sem_perder_a_coluna_das_cenas()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        new EditorLayoutMemory(config).ScenesCollapsed = true;

        new EditorLayoutMemory(config).ScriptDensity = true;

        var next = new EditorLayoutMemory(config);
        Assert.True(next.ScriptDensity);
        Assert.True(next.ScenesCollapsed);
        Assert.Contains("lines-script", File.ReadAllText(Path.Combine(config, "editor-layout.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ não é json")]
    [InlineData("[true]")]
    [InlineData("{\"lines-script\": \"sim\"}")]
    public void Arquivo_ilegivel_vale_como_colunas(string content)
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        File.WriteAllText(Path.Combine(config, "editor-layout.json"), content);

        Assert.False(new EditorLayoutMemory(config).ScriptDensity);
    }

    [Fact]
    public void O_Editor_comeca_nas_colunas_e_a_escolha_grava_e_volta_na_proxima_sessao()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        var vm = new EditorViewModel(layoutMemory: new EditorLayoutMemory(config));
        var tips = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.DensityTip))
            {
                tips.Add(vm.DensityTip);
            }
        };

        Assert.False(vm.IsScriptDensity);
        Assert.Contains("roteiro", vm.DensityTip, StringComparison.OrdinalIgnoreCase);

        vm.IsScriptDensity = true;

        Assert.True(new EditorLayoutMemory(config).ScriptDensity);
        Assert.Contains("colunas", Assert.Single(tips), StringComparison.OrdinalIgnoreCase);
        Assert.True(new EditorViewModel(layoutMemory: new EditorLayoutMemory(config)).IsScriptDensity);

        vm.ToggleDensityCommand.Execute(null);

        Assert.False(vm.IsScriptDensity);
        Assert.False(new EditorLayoutMemory(config).ScriptDensity);
        Assert.False(new EditorViewModel(layoutMemory: new EditorLayoutMemory(config)).IsScriptDensity);
    }

    [Fact]
    public void Sem_memoria_o_Editor_aceita_a_troca_de_densidade()
    {
        var vm = new EditorViewModel();

        vm.ToggleDensityCommand.Execute(null);

        Assert.True(vm.IsScriptDensity);
    }
}
