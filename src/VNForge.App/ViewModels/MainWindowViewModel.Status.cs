using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>O que a barra de status e o cabeçalho do jogo mostram além do andamento: a Biblioteca, o VNDB e o título original do jogo.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>O que o app sabe do VNDB: ainda nenhuma consulta, uma resposta que chegou ou a falha da última tentativa.</summary>
    public enum VndbConnection
    {
        Unknown,
        Connected,
        Unavailable,
    }

    /// <summary>Nas telas de trabalho (Editor, Conferir, Testar e Glossário) a barra de status mostra os dados do texto; nas outras, a Biblioteca e o VNDB.</summary>
    public bool ShowWorkStatus => CurrentPage is EditorViewModel or ConferenceViewModel or BuildViewModel or GlossaryViewModel;

    /// <summary>O visto da chave só aparece na Biblioteca; a chave que falta é um aviso e aparece em toda tela.</summary>
    public bool ShowKeyOk => ApiKeyConfigured && IsStartPage;

    /// <summary>"12 jogos na Biblioteca": os jogos que a Biblioteca lista, os mesmos do Início.</summary>
    public string LibraryCountText => Start.Recents.Count switch
    {
        0 => "Biblioteca vazia",
        1 => "1 jogo na Biblioteca",
        var n => $"{n} jogos na Biblioteca",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VndbStatusText))]
    public partial VndbConnection Vndb { get; private set; }

    /// <summary>O app só sabe do VNDB o que a última consulta do assistente de novo projeto disse: "VNDB conectado" depois de uma resposta, "VNDB indisponível" depois de uma falha.</summary>
    public string VndbStatusText => Vndb switch
    {
        VndbConnection.Connected => "VNDB conectado",
        VndbConnection.Unavailable => "VNDB indisponível",
        _ => "VNDB: sem consulta ainda",
    };

    /// <summary>O título do jogo em japonês, do <c>vndb.json</c> do projeto; vazio quando o projeto não tem o vínculo com o VNDB.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameOriginalTitle))]
    public partial string GameOriginalTitle { get; private set; } = string.Empty;

    public bool HasGameOriginalTitle => GameOriginalTitle.Length > 0;

    private static string ReadOriginalTitle(string root)
    {
        try
        {
            return ProjectFolder.Open(root).LoadVndbLink()?.OriginalTitle ?? string.Empty;
        }
        catch (Exception ex) when (ex is VndbLinkException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private void OnWizardVndbChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NewProjectWizardViewModel.Stage) || sender is not NewProjectWizardViewModel wizard)
        {
            return;
        }

        Vndb = wizard.Stage switch
        {
            VndbStage.Choosing or VndbStage.Preview or VndbStage.Empty => VndbConnection.Connected,
            VndbStage.Unavailable or VndbStage.Failed => VndbConnection.Unavailable,
            _ => Vndb,
        };
    }
}