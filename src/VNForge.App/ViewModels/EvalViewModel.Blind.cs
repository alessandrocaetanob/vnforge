using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Eval;
using VNForge.Core.Formatting;

namespace VNForge.App.ViewModels;

/// <summary>A rodada às cegas: o original e dois textos de configurações escondidas; a pessoa escolhe e a resposta só aparece no relatório.</summary>
public sealed partial class EvalViewModel
{
    private BlindPair? pair;

    [ObservableProperty]
    public partial bool IsBlind { get; private set; }

    [ObservableProperty]
    public partial string PairTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalMeta { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalSpeaker { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalContext { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TextA { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TextB { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FitA { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FitB { get; private set; } = string.Empty;

    /// <summary><c>Par 7 de 12 · você escolhe sem saber qual configuração gerou cada texto</c>.</summary>
    public string PairHeadline => $"{PairTitle} · {BlindSubtitle}";

    partial void OnPairTitleChanged(string value) => OnPropertyChanged(nameof(PairHeadline));

    public string BlindSubtitle => "você escolhe sem saber qual configuração gerou cada texto";

    /// <summary>Os pares respondidos sobre o total, de 0 a 1, na barra de cima da rodada às cegas.</summary>
    [ObservableProperty]
    public partial double PairFraction { get; private set; }

    partial void OnIsBlindChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReport));
        OnPropertyChanged(nameof(ShowStrip));
    }

    [RelayCommand(CanExecute = nameof(CanBlind))]
    private void BlindStart()
    {
        if (round?.NextPair is not { } next)
        {
            return;
        }

        Message = null;
        Show(next);
        IsBlind = true;
    }

    [RelayCommand]
    private void BlindBack()
    {
        IsBlind = false;
        Apply();
    }

    [RelayCommand]
    private void ChooseFirst() => Answer(BlindChoice.First);

    [RelayCommand]
    private void ChooseSecond() => Answer(BlindChoice.Second);

    [RelayCommand]
    private void ChooseTie() => Answer(BlindChoice.Tie);

    [RelayCommand]
    private void ChooseNeither() => Answer(BlindChoice.Neither);

    [RelayCommand]
    private void SkipPair() => Answer(BlindChoice.Skipped);

    private void Answer(BlindChoice choice)
    {
        if (round is null || pair is null)
        {
            return;
        }

        round = round.WithAnswer(pair.Index, choice);
        Save();
        if (round.NextPair is { } next)
        {
            Show(next);
            return;
        }

        IsBlind = false;
        var skipped = round.Pairs.Count - round.PairsDone;
        Message = skipped switch
        {
            0 => "A rodada às cegas acabou. A coluna \"Sua escolha às cegas\" mostra o resultado.",
            1 => "Sobrou 1 par que você pulou. A coluna \"Sua escolha às cegas\" mostra o resultado até aqui.",
            _ => $"Sobraram {skipped} pares que você pulou. A coluna \"Sua escolha às cegas\" mostra o resultado até aqui.",
        };
        Apply();
    }

    private void Show(BlindPair next)
    {
        pair = next;
        var current = round!;
        var sample = current.Sample.FirstOrDefault(s => s.LineId == next.LineId);
        var first = current.Reports.First(r => r.Config.Id == next.First).Lines.FirstOrDefault(l => l.LineId == next.LineId);
        var second = current.Reports.First(r => r.Config.Id == next.Second).Lines.FirstOrDefault(l => l.LineId == next.LineId);
        PairTitle = $"Par {next.Index} de {current.Pairs.Count}";
        PairFraction = current.Pairs.Count == 0 ? 0 : (double)(next.Index - 1) / current.Pairs.Count;
        OriginalMeta = sample is null ? string.Empty : MetaFor(sample);
        OriginalSpeaker = sample?.Speaker ?? string.Empty;
        OriginalText = sample?.Source ?? string.Empty;
        OriginalContext = sample is null ? string.Empty : ContextFor(sample);
        TextA = first?.Translation ?? string.Empty;
        TextB = second?.Translation ?? string.Empty;
        var wrap = TryWrap();
        FitA = Fit(wrap, TextA);
        FitB = Fit(wrap, TextB);
    }

    private string MetaFor(EvalSample sample)
    {
        var name = folder?.LoadScriptNames().DisplayName(sample.ScriptKey) ?? sample.ScriptKey;
        var number = sample.LineId.Contains(':', StringComparison.Ordinal) && int.TryParse(sample.LineId[(sample.LineId.LastIndexOf(':') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? $", linha {n.ToString("0000", CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"Original · {name}{number}";
    }

    private static string ContextFor(EvalSample sample) => (sample.Before.Length > 0, sample.After.Length > 0) switch
    {
        (true, true) => $"Antes: {sample.Before} · Depois: {sample.After}",
        (true, false) => $"Antes: {sample.Before}",
        (false, true) => $"Depois: {sample.After}",
        _ => string.Empty,
    };

    private WrapOptions? TryWrap()
    {
        try
        {
            return folder?.LoadFormatting().Wrap.ToOptions();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string Fit(WrapOptions? options, string text)
    {
        if (options is null || options.MaxLines is not { } max || text.Length == 0)
        {
            return string.Empty;
        }

        var lines = WordWrapper.Wrap(text, options).LinesPerBox.DefaultIfEmpty(0).Max();
        return lines <= max ? $"Cabe na caixa · {lines} de {max} linhas" : $"Passa da caixa · {lines} de {max} linhas";
    }
}
