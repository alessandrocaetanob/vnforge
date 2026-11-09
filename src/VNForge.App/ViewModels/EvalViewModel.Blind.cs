using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Eval;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

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
        PairFraction = current.Pairs.Count == 0 ? 0 : (double)next.Index / current.Pairs.Count;
        OriginalMeta = sample is null ? string.Empty : MetaFor(sample);
        OriginalSpeaker = sample is null ? string.Empty : SpeakerFor(sample);
        OriginalText = sample?.Source ?? string.Empty;
        OriginalContext = sample is null ? string.Empty : ContextFor(sample);
        TextA = first?.Translation ?? string.Empty;
        TextB = second?.Translation ?? string.Empty;
        var wrap = TryWrap();
        FitA = Fit(wrap, TextA);
        FitB = Fit(wrap, TextB);
    }

    /// <summary><c>Original · Cena 13, linha 0495</c> (o OverlineText desenha em caixa alta): o nome da cena, ou "Cena N" (a posição dela no projeto) quando ela não tem nome; nunca a chave do script.</summary>
    private string MetaFor(EvalSample sample)
    {
        var name = folder?.LoadScriptNames().DisplayName(sample.ScriptKey) ?? SceneLabel(sample.ScriptKey);
        var number = sample.LineId.Contains(':', StringComparison.Ordinal) && int.TryParse(sample.LineId[(sample.LineId.LastIndexOf(':') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? $", linha {n.ToString("0000", CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"Original · {name}{number}";
    }

    private string SceneLabel(string key)
    {
        if (folder is null || !Directory.Exists(folder.TextDirectory))
        {
            return key;
        }

        var keys = ScriptGrouping.InStoryOrder(Directory.EnumerateFiles(folder.TextDirectory, "*.json").Select(path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant()));
        var position = keys.ToList().IndexOf(key);
        return position < 0 ? key : $"Cena {position + 1}";
    }

    /// <summary>
    /// Quem fala e para quem, como o quadro desenha ("Misaki → Aizawa e Sakura"). O projeto não guarda o destinatário de cada fala: vale o elenco da cena na bíblia
    /// sem o falante. Sem elenco, só o falante; o nome vem do <c>characters.json</c> quando o personagem está cadastrado.
    /// </summary>
    private string SpeakerFor(EvalSample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.Speaker) || folder is null)
        {
            return sample.Speaker ?? string.Empty;
        }

        var characters = SafeCharacters(folder);
        var speaker = characters.FirstOrDefault(c => IsSame(c, sample.Speaker));
        if (speaker is null)
        {
            // Sem o cadastro não dá para saber quem, do elenco da cena, é o falante: mostrar só o nome dele evita listá-lo como destinatário.
            return sample.Speaker;
        }

        var shown = speaker.Target;
        var others = folder.LoadBibleOrEmpty(out _).Scene(sample.ScriptKey)?.Characters
            .Where(name => !string.Equals(name, shown, StringComparison.OrdinalIgnoreCase) && !IsSame(speaker, name))
            .ToList() ?? [];
        return others.Count == 0 ? shown : $"{shown} → {JoinNames(others)}";
    }

    private static IReadOnlyList<CharacterEntry> SafeCharacters(ProjectFolder project)
    {
        try
        {
            return project.LoadCharacters();
        }
        catch (VNForgeException)
        {
            return [];
        }
    }

    private static bool IsSame(CharacterEntry entry, string name) =>
        CharacterBook.NameComparer.Equals(entry.Source, name)
        || string.Equals(entry.Target, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Romaji, name, StringComparison.OrdinalIgnoreCase);

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";

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
