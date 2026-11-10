using VNForge.App.Services;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

public sealed partial class LibraryViewModel
{
    /// <summary>A lista do VNDB (token, leitura, gravação e envio). Sem ela a Ficha não mostra o cartão da lista.</summary>
    public VndbListService? ListService { get; set; }

    /// <summary>
    /// O cartão da lista do VNDB para a Ficha do projeto aberto: o jogo da Biblioteca ligado à pasta do projeto, ou, sem ligação, o que tem o mesmo jogo do VNDB.
    /// Nulo sem o serviço da lista ou quando o jogo não está na Biblioteca nem confirmado no VNDB.
    /// </summary>
    public LibraryVndbListViewModel? ListCardForProject(string? projectPath, string? vndbId)
    {
        var found = games.FirstOrDefault(g => projectPath is not null && string.Equals(g.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            ?? games.FirstOrDefault(g => vndbId is not null && g.VndbId == vndbId);
        if (ListService is null || found is not { IsConfirmed: true })
        {
            return null;
        }

        return new LibraryVndbListViewModel(found, ListService, this, clock);
    }

    /// <summary>A lista mudou no arquivo (edição ou sincronização): relê a Biblioteca para o resto da tela ver o que ficou lá.</summary>
    public Task ReloadListAsync() => LoadCoreAsync();
}
