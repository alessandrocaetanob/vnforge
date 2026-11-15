using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.UserData;

namespace VNForge.App.ViewModels;

/// <summary>
/// As pastas do usuário (#694): a aba Pastas dos Ajustes e os diálogos de escolha e de troca (primeira abertura, troca, unidade de rede, falha, pasta que sumiu).
/// A regra de achar a pasta e a cópia conferida são do Core (<see cref="UserFolders"/>, <see cref="UserFolderMover"/>); aqui ficam a escolha da pessoa e o texto em português.
/// Toda troca termina reiniciando o app, porque ele monta tudo a partir da pasta ao abrir.
/// </summary>
public sealed partial class FoldersViewModel : ObservableObject
{
    private readonly UserFolders folders;
    private readonly IFolderPicker picker;
    private readonly Action<string> openFolder;
    private readonly Action restart;
    private readonly Action<Action> post;
    private readonly Func<UserFolders> resolve;
    private readonly Func<string, (int Files, long Bytes)> measure;
    private readonly Func<UserFolderKind, string, string, UserFolderMovePlan> prepare;
    private readonly Action<UserFolders, UserFolderMovePlan, bool, Action<UserFolderMoveProgress>?, CancellationToken> move;

    /// <param name="restart">Fecha o app e o abre de novo; os testes passam um contador.</param>
    /// <param name="services">As costuras que a captura de tela e os testes trocam; sem elas valem o disco e a thread da tela.</param>
    public FoldersViewModel(UserFolders folders, IFolderPicker picker, Action<string> openFolder, Action restart, FoldersServices? services = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        services ??= new FoldersServices();
        this.folders = folders;
        this.picker = picker;
        this.openFolder = openFolder;
        this.restart = restart;
        post = services.Post ?? (action => action());
        resolve = services.Resolve ?? UserFolders.Resolve;
        measure = services.Measure ?? MeasureOnDisk;
        prepare = services.Prepare ?? UserFolderMover.Prepare;
        move = services.Move ?? UserFolderMover.Move;
        Config = new FolderCardViewModel(this, UserFolderKind.Config, folders);
        Cache = new FolderCardViewModel(this, UserFolderKind.Cache, folders);
        Cards = [Config, Cache];
        DraftConfig = folders.Config.Path;
        DraftCache = folders.Cache.Path;
        Loading = Task.Run(Measure, CancellationToken.None);
    }

    public FolderCardViewModel Config { get; }

    public FolderCardViewModel Cache { get; }

    /// <summary>Os dois cartões da aba, na ordem do quadro.</summary>
    public IReadOnlyList<FolderCardViewModel> Cards { get; }

    /// <summary>Termina quando o tamanho de cada pasta foi contado.</summary>
    public Task Loading { get; private set; }

    /// <summary>O comando de terminal que mostra de onde veio cada pasta, e a variável que vale mais que a escolha do app.</summary>
    public string CommandText => "vnf config pasta";

    public string VariableText => $"set {UserFolders.ConfigVariable}=D:\\Dados\\VNForge";

    /// <summary>A primeira pergunta foi respondida sem trocar nada: a Biblioteca pode seguir. Não dispara quando a resposta reinicia o app.</summary>
    public event EventHandler? Continued;

    /// <summary>Conta de novo o que há em cada pasta (a aba abriu, ou a pessoa gravou algo).</summary>
    public Task RefreshAsync() => Loading = Task.Run(Measure, CancellationToken.None);

    private void Measure()
    {
        foreach (var card in Cards)
        {
            var (files, bytes) = measure(card.Path);
            post(() => card.SetSize(files, bytes));
        }
    }

    private static (int Files, long Bytes) MeasureOnDisk(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return (0, 0);
        }

        var files = 0;
        var bytes = 0L;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                files++;
                bytes += file.Length;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Uma subpasta que não abre fica de fora da conta; o tamanho é só informação.
        }

        return (files, bytes);
    }

    internal void Open(UserFolderKind kind)
    {
        var path = (kind == UserFolderKind.Config ? Config : Cache).Path;
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Pasta que não pode ser criada (unidade fora do ar): o gerenciador de arquivos mostra o erro dele.
        }

        openFolder(path);
    }
}

/// <summary>As costuras de <see cref="FoldersViewModel"/>; cada uma que fica nula vale o comportamento real.</summary>
public sealed class FoldersServices
{
    /// <summary>Leva um trabalho para a thread da tela (o progresso da cópia vem de outra thread).</summary>
    public Action<Action>? Post { get; init; }

    /// <summary>Acha as pastas de novo (o "Tentar de novo" da pasta que sumiu).</summary>
    public Func<UserFolders>? Resolve { get; init; }

    /// <summary>Conta arquivos e bytes de uma pasta.</summary>
    public Func<string, (int Files, long Bytes)>? Measure { get; init; }

    /// <summary>Confere a pasta nova e conta o que vai ser copiado; a captura de tela troca por um plano pronto.</summary>
    public Func<UserFolderKind, string, string, UserFolderMovePlan>? Prepare { get; init; }

    /// <summary>A cópia conferida; a captura de tela troca por uma que falha aos 38%.</summary>
    public Action<UserFolders, UserFolderMovePlan, bool, Action<UserFolderMoveProgress>?, CancellationToken>? Move { get; init; }
}

/// <summary>Uma das duas pastas na aba Pastas: o caminho, a etiqueta de onde veio, o tamanho e os botões.</summary>
public sealed partial class FolderCardViewModel : ObservableObject
{
    private readonly FoldersViewModel owner;
    private readonly UserFolders folders;

    internal FolderCardViewModel(FoldersViewModel owner, UserFolderKind kind, UserFolders folders)
    {
        this.owner = owner;
        this.folders = folders;
        Kind = kind;
    }

    public UserFolderKind Kind { get; }

    public bool IsConfig => Kind == UserFolderKind.Config;

    public string Title => IsConfig ? "Dados e configuração" : "Cache, capturas e registros";

    public string Detail => IsConfig
        ? "Lista de jogos, projetos recentes, fichas, memória de tradução, dicionário e chaves."
        : "Imagens baixadas do VNDB, capturas de tela dos jogos e registros de erro.";

    public string Path => folders.Of(Kind).Path;

    /// <summary>De onde veio a pasta: o padrão do Windows, a escolha feita no app ou a variável de ambiente.</summary>
    public string Tag => folders.Of(Kind) switch
    {
        { Source: UserFolderSource.Environment } => "Variável de ambiente",
        var folder when string.Equals(folder.Path, IsConfig ? folders.DefaultConfig : folders.DefaultCache, StringComparison.OrdinalIgnoreCase) => "Padrão do Windows",
        _ => "Escolhida por você",
    };

    /// <summary>A variável de ambiente vale mais que a escolha do app, então trocar aqui não mudaria nada: o botão fica desligado e a dica diz por quê.</summary>
    public bool CanChange => folders.Of(Kind).Source != UserFolderSource.Environment;

    public string ChangeTip => CanChange
        ? "Escolher outra pasta e mover os arquivos para ela"
        : $"Definida pela variável {(IsConfig ? UserFolders.ConfigVariable : UserFolders.CacheVariable)}; ajuste a variável para mudar.";

    /// <summary>"26 arquivos · 31 MB" nos dados e "248 KB" no cache; vazio até a contagem terminar.</summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>A nota das chaves: só a pasta de dados as guarda.</summary>
    public string KeyNote => "As chaves da Anthropic e do VNDB ficam nesta pasta, cifradas pelo Windows para a sua conta. Em outro PC ou usuário elas não abrem: adicione a chave de novo.";

    internal void SetSize(int files, long bytes)
    {
        var size = UserFolderMover.FormatSize(bytes);
        SizeText = IsConfig ? $"{files} {(files == 1 ? "arquivo" : "arquivos")} · {size}" : size;
    }

    [RelayCommand]
    private void Open() => owner.Open(Kind);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task ChangeAsync() => owner.ChangeAsync(Kind);
}
