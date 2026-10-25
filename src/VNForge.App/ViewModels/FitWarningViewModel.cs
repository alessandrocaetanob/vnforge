using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// O aviso "Não cabe na caixa do jogo" com as duas saídas: "Dividir em N páginas" (<see cref="PageSplitter"/>) e "Encurtar com a IA"
/// (<see cref="ILineShortener"/>). É a lógica que o controle <c>FitWarning</c> mostra, igual no inspetor do Editor, no modo revisão e na
/// Conferência: quem hospeda diz de onde lê a tradução e o que fazer com o texto novo.
/// </summary>
public sealed partial class FitWarningViewModel : ObservableObject
{
    /// <summary>O título do aviso.</summary>
    public const string TitleText = "Não cabe na caixa do jogo";

    private readonly TextUnit unit;
    private readonly FormattingProfile profile;
    private readonly ILineShortener? shortener;
    private readonly Func<string> translation;
    private readonly Func<string, Task> applySplit;
    private readonly Func<string, Task> applyShortened;
    private CancellationTokenSource? cancellation;

    /// <param name="unit">A linha; o papel dela decide se o build a formata.</param>
    /// <param name="profile">Perfil de formatação do projeto (a caixa do jogo).</param>
    /// <param name="shortener">Pede a versão curta à IA; nulo esconde o botão "Encurtar com a IA".</param>
    /// <param name="translation">Lê a tradução de agora (a do campo em edição ou a gravada).</param>
    /// <param name="applySplit">Recebe o texto já dividido em páginas.</param>
    /// <param name="applyShortened">Recebe a versão curta que a IA devolveu e que cabe na caixa.</param>
    public FitWarningViewModel(
        TextUnit unit,
        FormattingProfile profile,
        ILineShortener? shortener,
        Func<string> translation,
        Func<string, Task> applySplit,
        Func<string, Task> applyShortened)
    {
        this.unit = unit ?? throw new ArgumentNullException(nameof(unit));
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.shortener = shortener;
        this.translation = translation ?? throw new ArgumentNullException(nameof(translation));
        this.applySplit = applySplit ?? throw new ArgumentNullException(nameof(applySplit));
        this.applyShortened = applyShortened ?? throw new ArgumentNullException(nameof(applyShortened));
    }

    public string Title => TitleText;

    /// <summary>A tradução passa do limite de linhas da caixa; o aviso só aparece então.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShortenCommand))]
    public partial bool IsVisible { get; private set; }

    /// <summary>"Passa 1 linha: a caixa do jogo tem 3 linhas."</summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    /// <summary>"Dividir em 2 páginas".</summary>
    [ObservableProperty]
    public partial string SplitText { get; private set; } = "Dividir em 2 páginas";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand), nameof(ShortenCommand))]
    public partial bool CanSplit { get; private set; }

    /// <summary>A IA está encurtando a fala.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SplitCommand), nameof(ShortenCommand))]
    [NotifyPropertyChangedFor(nameof(ShortenText))]
    public partial bool IsBusy { get; private set; }

    /// <summary>O rótulo do botão da IA: "Encurtar com a IA", ou "Encurtando..." enquanto ela responde.</summary>
    public string ShortenText => IsBusy ? "Encurtando..." : "Encurtar com a IA";

    /// <summary>Por que o último pedido não deu certo, em português; nulo até o texto mudar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Há quem encurte; sem ele o botão "Encurtar com a IA" não aparece.</summary>
    public bool HasShortener => shortener is not null;

    /// <summary>Refaz o aviso para a tradução de agora e apaga o erro do pedido anterior. O hospedeiro chama a cada mudança do texto.</summary>
    public void Update()
    {
        Error = null;
        if (Measure(translation()) is not { } fit)
        {
            IsVisible = false;
            CanSplit = false;
            return;
        }

        Message = fit.Over == 1
            ? $"Passa 1 linha: a caixa do jogo tem {fit.MaxLines} linhas."
            : $"Passa {fit.Over} linhas: a caixa do jogo tem {fit.MaxLines} linhas.";
        SplitText = $"Dividir em {1 + fit.Split.Added} páginas";
        CanSplit = fit.Split.Changed;
        IsVisible = true;
    }

    /// <summary>Interrompe o pedido à IA em andamento, se houver, sem mostrar erro.</summary>
    public void Cancel() => cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRunSplit))]
    private async Task SplitAsync()
    {
        try
        {
            var split = PageSplitter.Split(profile, unit, translation());
            if (split.Changed)
            {
                await applySplit(split.Text);
            }
        }
        catch (VNForgeException e)
        {
            Error = e.Message;
        }
    }

    private bool CanRunSplit() => CanSplit && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunShorten))]
    private async Task ShortenAsync()
    {
        if (shortener is null)
        {
            return;
        }

        using var source = new CancellationTokenSource();
        cancellation = source;
        Error = null;
        IsBusy = true;
        try
        {
            var shorter = await shortener.ShortenAsync(unit, translation(), profile.Wrap, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (Measure(shorter) is not null)
            {
                Error = "A versão mais curta da IA ainda não cabe na caixa. Tente de novo ou corte o texto à mão.";
                return;
            }

            await applyShortened(shorter);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // Cancelado pelo hospedeiro (a linha mudou): sem erro na tela.
        }
        catch (Exception e)
        {
            // O provedor pode lançar de tudo (rede, SDK, recusa); o aviso mostra a frase em português.
            Error = e is LineSuggestionException ? e.Message : ClaudeErrorMessages.Describe(e);
        }
        finally
        {
            cancellation = null;
            IsBusy = false;
        }
    }

    private bool CanRunShorten() => HasShortener && IsVisible && !IsBusy;

    private sealed record Fit(int Over, int MaxLines, PageSplit Split);

    /// <summary>Quantas linhas a tradução passa do limite da caixa e como ficaria dividida; nulo quando cabe, está vazia ou o texto é recusado.</summary>
    private Fit? Measure(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || profile.Wrap.MaxLines is not { } maxLines)
        {
            return null;
        }

        try
        {
            var over = LinePreviewer.Create(profile, unit, text).Boxes.Max(box => box.Count) - maxLines;
            return over > 0 ? new Fit(over, maxLines, PageSplitter.Split(profile, unit, text)) : null;
        }
        catch (VNForgeException)
        {
            return null;
        }
    }
}
