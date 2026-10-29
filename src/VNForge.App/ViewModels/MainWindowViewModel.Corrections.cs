using System.Diagnostics;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Review;

namespace VNForge.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string CorrectionOfferKey = "correction-offer";

    private const string CorrectionAddedKey = "correction-added";

    private static readonly TimeSpan CorrectionOfferLifetime = TimeSpan.FromSeconds(20);

    private readonly CorrectionOffers correctionOffers = new();

    private bool scanningCorrections;

    /// <summary>
    /// Depois de uma gravação que mudou o texto da IA, procura a troca que a pessoa repete (Ei por Ô) e, passado o limite, oferece a regra.
    /// A gravação de uma linha sem texto da IA guardado e a aprovação não mudam o que há para comparar.
    /// </summary>
    private void OfferCorrections(LineSavedEventArgs saved)
    {
        if (saved.IsApproval || (!saved.IsBatch && saved.Saved.AiTranslation is null) || CurrentProject is not { } project)
        {
            return;
        }

        _ = OfferCorrectionsAsync(project.Root);
    }

    /// <summary>Lê os textos fora da thread da tela, acha as trocas repetidas e, de volta nela, mostra o aviso da primeira que ainda cabe oferecer.</summary>
    public async Task OfferCorrectionsAsync(string root)
    {
        // Uma varredura por vez, e nenhuma enquanto a oferta anterior está na tela: a segunda oferta trocaria a primeira (mesma chave) e a primeira nunca mais voltaria.
        if (scanningCorrections || Toasts.Toasts.Any(t => t.Key == CorrectionOfferKey))
        {
            return;
        }

        IReadOnlyList<RepeatedCorrection> found;
        scanningCorrections = true;
        try
        {
            found = await Task.Run(() => CorrectionLearner.Find(ProjectFolder.Open(root).LoadAllText().SelectMany(script => script.Value.Select(unit => (script.Key, unit)))));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            return;
        }
        finally
        {
            scanningCorrections = false;
        }

        if (found.Count == 0 || Toasts.Toasts.Any(t => t.Key == CorrectionOfferKey) || !string.Equals(CurrentProject?.Root, root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (correctionOffers.Next(root, found, Settings.StyleGuide, speaker => Glossary.ToneOf(speaker)) is { } next)
        {
            ShowCorrectionOffer(next);
        }
    }

    private void ShowCorrectionOffer(RepeatedCorrection correction)
    {
        var swap = $"“{correction.Swap.From}” por “{correction.Swap.To}”";
        if (correction.Scope == CorrectionScope.Project)
        {
            Toasts.Show(
                $"Você trocou {swap} em {ProjectLabels.Number(correction.Count)} falas. Adicionar ao guia de estilo?",
                ToastKind.Info,
                key: CorrectionOfferKey,
                undoLabel: "Adicionar ao estilo",
                undo: () => AcceptCorrection(correction),
                lifetime: CorrectionOfferLifetime);
            return;
        }

        var speaker = correction.Speaker!;
        Toasts.Show(
            $"Você trocou {swap} em {ProjectLabels.Number(correction.Count)} falas de {Glossary.NameOf(speaker)}. Adicionar ao Como fala do personagem?",
            ToastKind.Info,
            key: CorrectionOfferKey,
            undoLabel: "Adicionar ao Como fala",
            undo: () => AcceptCorrection(correction),
            lifetime: CorrectionOfferLifetime);
    }

    private void AcceptCorrection(RepeatedCorrection correction)
    {
        if (correction.Scope == CorrectionScope.Project)
        {
            Settings.AddStyleRule(correction);
            Toasts.Show("Regra adicionada ao guia de estilo.", ToastKind.Success, key: CorrectionAddedKey);
        }
        else if (Glossary.AddToneRule(correction.Speaker!, correction))
        {
            Toasts.Show($"Regra adicionada ao Como fala de {Glossary.NameOf(correction.Speaker!)}.", ToastKind.Success, key: CorrectionAddedKey);
        }
        else
        {
            Toasts.Show("Não foi possível escrever no Como fala. Abra o Glossário e confira se o arquivo está válido.", ToastKind.Warning, key: CorrectionAddedKey);
        }
    }
}
