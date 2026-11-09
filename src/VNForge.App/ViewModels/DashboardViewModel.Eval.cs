using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>As abas do Painel: "Visão geral" (o mapa do jogo) e "Provas" (o banco de provas, #529).</summary>
public sealed partial class DashboardViewModel
{
    private bool evalStale = true;

    /// <summary>O banco de provas, a aba "Provas".</summary>
    public EvalViewModel Eval { get; } = new();

    /// <summary>0 é "Visão geral" e 1 é "Provas".</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    public bool ShowOverview => SelectedTab == 0;

    public bool ShowEvalTab => SelectedTab == 1;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowOverview));
        OnPropertyChanged(nameof(ShowEvalTab));
        if (value == 1 && evalStale)
        {
            LoadEval();
        }
    }

    /// <summary>O projeto mudou ou foi relido: a aba "Provas" lê de novo na próxima vez que aparecer (ou já, se estiver aparecendo).</summary>
    private void ResetEval()
    {
        evalStale = true;
        if (SelectedTab == 1)
        {
            LoadEval();
        }
    }

    private void LoadEval()
    {
        evalStale = false;
        ProjectFolder? folder = null;
        if (Project is { } project)
        {
            try
            {
                folder = ProjectFolder.Open(project.Root);
            }
            catch (VNForgeException)
            {
                // O Painel já mostra por que o projeto não abriu; a aba fica sem projeto.
            }
        }

        _ = Eval.LoadAsync(folder);
    }
}
