using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// O diálogo da pasta diferente: de onde para onde a pasta do jogo muda no projeto e em que estado o jogo está na pasta nova. <see cref="CanConfirm"/> falso é a recusa
/// (a pasta não é o jogo do projeto): só tem Fechar e nada muda.
/// </summary>
public sealed record LibraryLinkDialog(
    string Title,
    string Message,
    string FromFolder,
    string ToFolder,
    bool CanConfirm,
    string ProjectName = "",
    string? StateTitle = null,
    string StateTone = "pend",
    string? StateText = null,
    string? Note = null,
    string? File = null)
{
    public bool HasFile => File is not null;

    public bool HasNoFile => File is null;

    public bool IsRefusal => !CanConfirm;

    public bool HasRoute => FromFolder.Length > 0;

    public bool HasNote => !string.IsNullOrEmpty(Note);
}

/// <summary>"Ligar a um projeto que já existe": pede a pasta do projeto e liga ao jogo, como o <c>vnf library link</c>. Só lê a pasta do jogo.</summary>
public sealed partial class LibraryViewModel
{
    private LibraryGame? linkGame;
    private string? linkFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkDialog))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLinkCommand))]
    public partial LibraryLinkDialog? LinkDialog { get; private set; }

    /// <summary>A conferência dos arquivos do jogo (e a gravação) está rodando: o botão do diálogo fica ocupado.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLinkCommand), nameof(LinkCardProjectCommand))]
    public partial bool IsLinking { get; private set; }

    /// <summary>O diálogo da pasta diferente está aberto; fechá-lo (Cancelar, Escape) esquece a pasta escolhida.</summary>
    public bool HasLinkDialog
    {
        get => LinkDialog is not null;
        set
        {
            if (!value)
            {
                CloseLinkDialog();
            }
        }
    }

    /// <summary>A ligação que está rodando ou rodou por último.</summary>
    public Task Linking { get; private set; } = Task.CompletedTask;

    /// <summary>Um projeto foi ligado ao jogo; o shell avisa com um toast.</summary>
    public event EventHandler<LibraryGame>? ProjectLinked;

    /// <summary>Uma ligação nova começou ou a pessoa fechou o diálogo sem ligar: o que o shell guardou para abrir depois da ligação anterior deixa de valer.</summary>
    public event EventHandler? LinkSuperseded;

    /// <summary>O diálogo aberto é o de confirmar a ligação de <paramref name="game"/>; um diálogo de outro jogo ou uma recusa não conta.</summary>
    public bool IsLinkDialogFor(LibraryGame game) => linkGame?.Id == game.Id && LinkDialog is { CanConfirm: true };

    [RelayCommand]
    private void CloseLinkDialog()
    {
        ClearLinkDialog();
        LinkSuperseded?.Invoke(this, EventArgs.Empty);
    }

    private void ClearLinkDialog()
    {
        LinkDialog = null;
        linkGame = null;
        linkFolder = null;
    }

    [RelayCommand(CanExecute = nameof(CanLink))]
    private Task LinkCardProjectAsync(LibraryCardViewModel? card) => (card ?? Selected) is { } target ? LinkProjectAsync(target.Game) : Task.CompletedTask;

    private bool CanLink() => !IsLinking && editor is not null;

    /// <summary>
    /// Liga o projeto ao jogo; sem <paramref name="projectFolder"/>, pede a pasta. Pasta do jogo igual no projeto liga direto; diferente abre o diálogo, que liga ao confirmar.
    /// Pasta que não é projeto vira o aviso da Biblioteca, sem ligar nada.
    /// </summary>
    public Task LinkProjectAsync(LibraryGame game, string? projectFolder = null) => Linking = LinkCoreAsync(game, projectFolder);

    private async Task LinkCoreAsync(LibraryGame game, string? projectFolder)
    {
        if (editor is null || IsLinking)
        {
            return;
        }

        LinkSuperseded?.Invoke(this, EventArgs.Empty);

        // Marcado antes do seletor: um segundo clique com o seletor aberto não abre outro nem corre uma segunda ligação.
        IsLinking = true;
        LibraryGame? linked;
        try
        {
            var folder = projectFolder ?? await picker.PickFolderAsync("Pasta do projeto de tradução");
            if (folder is null)
            {
                return;
            }

            ErrorMessage = null;
            GameRelocationPreview? preview;
            try
            {
                preview = await Task.Run(() => LibraryProjectLinker.PreviewFollowGame(game, folder, UserBackupLocations.Read(file)), CancellationToken.None);
            }
            catch (LibraryException e)
            {
                ErrorMessage = e.Message;
                LinkSuperseded?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                LinkDialog = LinkRefusal(e.Message);
                LinkSuperseded?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (preview is not null)
            {
                linkGame = game;
                linkFolder = folder;
                LinkDialog = DescribeLink(game, folder, preview);
                return;
            }

            linked = await TryLinkAsync(game, folder);
        }
        finally
        {
            IsLinking = false;
        }

        if (linked is not null)
        {
            await FinishLinkAsync(linked);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmLink))]
    private async Task ConfirmLinkAsync()
    {
        if (linkGame is not { } game || linkFolder is not { } folder)
        {
            return;
        }

        IsLinking = true;
        LibraryGame? linked;
        try
        {
            linked = await TryLinkAsync(game, folder);
        }
        finally
        {
            IsLinking = false;
        }

        if (linked is not null)
        {
            await FinishLinkAsync(linked);
        }
    }

    private bool CanConfirmLink() => !IsLinking && LinkDialog is { CanConfirm: true };

    /// <summary>Grava a ligação. Falha vira o aviso da Biblioteca (jogo ou projeto que sumiu) ou a recusa do diálogo (a pasta não é o jogo do projeto) e devolve nulo.</summary>
    private async Task<LibraryGame?> TryLinkAsync(LibraryGame game, string folder)
    {
        try
        {
            return await Task.Run(() => editor!.LinkProject(game.Id, folder), CancellationToken.None);
        }
        catch (LibraryException e)
        {
            ClearLinkDialog();
            ErrorMessage = e.Message;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            LinkDialog = LinkRefusal(e.Message);
        }

        LinkSuperseded?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Depois da ligação gravada: refaz a Biblioteca e avisa. Uma falha ao reler não desfaz a ligação, então o aviso sobe de qualquer jeito.</summary>
    private async Task FinishLinkAsync(LibraryGame linked)
    {
        ClearLinkDialog();
        try
        {
            await LoadCoreAsync();
            Select(Cards.FirstOrDefault(c => c.Id == linked.Id));
        }
        finally
        {
            ProjectLinked?.Invoke(this, linked);
        }
    }

    private LibraryLinkDialog DescribeLink(LibraryGame game, string folder, GameRelocationPreview preview)
    {
        if (preview.Mixed)
        {
            return LinkRefusal(ProjectGameRelocator.MixedMessage) with
            {
                Title = "Os arquivos do jogo estão misturados",
                FromFolder = preview.CurrentFolder,
                ToFolder = preview.TargetFolder,
                Note = "Nada mudou no projeto.",
            };
        }

        if (!preview.Accepted)
        {
            var first = preview.Mismatches[0];
            var rest = preview.Mismatches.Count - 1;
            var problem = first.IsMissing ? " não está na pasta do jogo." : " não bate com o original nem com o que o VNForge gravou.";
            if (rest > 0)
            {
                problem += $" Mais {rest} {(rest == 1 ? "arquivo" : "arquivos")} também não conferem.";
            }

            return LinkRefusal(problem)
                with { FromFolder = preview.CurrentFolder, ToFolder = preview.TargetFolder, File = first.Path };
        }

        var applied = preview.State == GameFolderState.Applied;
        return new LibraryLinkDialog(
            "Mudar a pasta do jogo neste projeto?",
            "ainda aponta para uma pasta antiga. A Biblioteca tem o jogo em outro lugar.",
            preview.CurrentFolder,
            preview.TargetFolder,
            CanConfirm: true,
            ProjectName: ProjectNameOf(folder, game),
            StateTitle: applied ? "Patch do VNForge no jogo" : "Sem patch no jogo",
            StateTone: applied ? "ok" : "pend",
            StateText: applied
                ? $"{AppliedSince(preview.AppliedAt)}Os arquivos da pasta nova batem com o que o VNForge gravou."
                : "Os arquivos da pasta nova batem com a cópia de segurança do projeto: o jogo está como veio.",
            Note: "O VNForge só lê a pasta do jogo. Os textos, o glossário e as cópias de segurança do projeto continuam como estão.");
    }

    private static LibraryLinkDialog LinkRefusal(string message) => new(
        "Esta pasta não tem o jogo deste projeto",
        message,
        string.Empty,
        string.Empty,
        CanConfirm: false,
        Note: "Pode ser outro jogo, outra versão ou um arquivo mudado fora do VNForge. Nada mudou no projeto.");

    private string AppliedSince(DateTimeOffset? at)
    {
        if (at is not { } moment)
        {
            return string.Empty;
        }

        var local = moment.ToLocalTime();
        var now = clock.GetLocalNow();
        var day = local.ToString(local.Year == now.Year ? "dd/MM" : "dd/MM/yyyy", PtBr);
        return $"No jogo desde {day}. ";
    }

    private static string ProjectNameOf(string folder, LibraryGame game)
    {
        try
        {
            var name = ProjectFolder.Open(folder).LoadConfig().Name;
            return string.IsNullOrWhiteSpace(name) ? game.Title : name;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return game.Title;
        }
    }
}
