using CommunityToolkit.Mvvm.ComponentModel;

namespace VNForge.Installer.ViewModels;

public enum InstallStepState
{
    Pending,
    Running,
    Done,
}

/// <summary>Uma linha da janela "Instalando": o ícone pelo estado e, à direita, o tempo, o tamanho ou o percentual.</summary>
public sealed partial class InstallStepViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsDone))]
    public partial InstallStepState State { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    public bool IsPending => State == InstallStepState.Pending;

    public bool IsRunning => State == InstallStepState.Running;

    public bool IsDone => State == InstallStepState.Done;
}
