using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>O aviso "Não cabe na caixa do jogo" do detalhe do problema, com "Dividir em páginas" e "Encurtar com a IA".</summary>
public sealed partial class ConferenceViewModel
{
    /// <summary>Quem encurta a fala a pedido do aviso; o padrão é o sugestor sem chave (o botão pede a chave em Ajustes), nulo esconde "Encurtar com a IA".</summary>
    public ILineShortener? Shortener { get; set; } = new LineSuggester(() => null);

    /// <summary>O aviso do problema aberto quando ele é de caixa que não cabe; nulo nos outros problemas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFit))]
    public partial FitWarningViewModel? SelectedFit { get; private set; }

    public bool HasSelectedFit => SelectedFit is not null;

    partial void OnSelectedRowChanged(ConferenceIssueRowViewModel? value)
    {
        SelectedFit?.Cancel();
        SelectedFit = CreateFit(value);
    }

    private FitWarningViewModel? CreateFit(ConferenceIssueRowViewModel? row)
    {
        if (row is not { RuleId: BoxLineLimitRule.RuleId, Unit: { } unit } || Project?.Root is not { } root)
        {
            return null;
        }

        FormattingProfile profile;
        try
        {
            profile = ProjectFolder.Open(root).LoadFormatting();
        }
        catch (VNForgeException)
        {
            return null;
        }

        var fit = new FitWarningViewModel(
            unit,
            profile,
            Shortener,
            () => unit.Translation ?? string.Empty,
            text => ReplaceTranslationAsync(row, text, "Fala dividida em páginas."),
            text => ReplaceTranslationAsync(row, text, "Fala encurtada pela IA."));
        fit.Update();
        return fit.IsVisible ? fit : null;
    }

    /// <summary>Grava a tradução nova da linha do problema, avisa o Editor e reconfere o projeto.</summary>
    private async Task ReplaceTranslationAsync(ConferenceIssueRowViewModel row, string text, string notice)
    {
        var root = Project?.Root;
        if (root is null)
        {
            return;
        }

        ErrorMessage = null;
        NoticeMessage = null;
        IsFixing = true;
        try
        {
            try
            {
                await Task.Run(() => new LineEditor(ProjectFolder.Open(root), clock).SetTranslation(row.ScriptKey, row.UnitId, text));
            }
            catch (Exception e)
            {
                ErrorMessage = $"Não foi possível gravar a nova tradução: {Reason(e)}. A tradução continua como estava.";
                return;
            }

            TextsChanged?.Invoke(this, EventArgs.Empty);
            await RefreshAsync();
            if (ErrorMessage is null)
            {
                NoticeMessage = notice;
            }
        }
        finally
        {
            IsFixing = false;
        }
    }
}
