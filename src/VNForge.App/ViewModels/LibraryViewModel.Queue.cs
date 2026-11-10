using System.Collections.ObjectModel;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Em que ordem os jogos aparecem: a fila de tradução, o título ou a duração.</summary>
public enum LibrarySort
{
    Queue,
    Title,
    Duration,
}

public sealed record LibrarySortItem(LibrarySort Sort, string Label);

/// <summary>A fila de tradução da Biblioteca (d5, #551): a ordem "Fila" e a tabela da lista, e a conferência do português no VNDB que a alimenta.</summary>
public sealed partial class LibraryViewModel
{
    /// <summary>As linhas da lista, na mesma ordem dos <see cref="Cards"/>.</summary>
    public ObservableCollection<LibraryQueueRowViewModel> Rows { get; } = [];

    /// <summary>"Fila", "Título" e "Duração", o controle "Ordenar por".</summary>
    public IReadOnlyList<LibrarySortItem> Sorts { get; } = [new(LibrarySort.Queue, "Fila"), new(LibrarySort.Title, "Título"), new(LibrarySort.Duration, "Duração")];

    public LibrarySort Sort
    {
        get => sort;
        set
        {
            if (SetProperty(ref sort, value))
            {
                OnPropertyChanged(nameof(SelectedSort));
                Refilter();
            }
        }
    }

    public LibrarySortItem SelectedSort
    {
        get => Sorts[(int)Sort];
        set
        {
            if (value is not null)
            {
                Sort = value.Sort;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>A conferência do português em curso, para os testes esperarem.</summary>
    public Task PortugueseChecking { get; private set; } = Task.CompletedTask;

    private LibrarySort sort = LibrarySort.Queue;
    private List<TranslationQueueEntry> arranged = [];

    /// <summary>Os jogos já filtrados, escritos na ordem escolhida.</summary>
    private List<TranslationQueueEntry> Arrange(IReadOnlyList<LibraryGame> shown) =>
        Sort switch
        {
            LibrarySort.Queue => [.. TranslationQueue.Build(shown)],
            LibrarySort.Duration => [.. shown.Select(TranslationQueue.Describe).OrderBy(e => e.ApproximateMinutes ?? int.MaxValue)],
            _ => [.. shown.Select(TranslationQueue.Describe)],
        };

    /// <summary>As linhas só existem na lista: na grade a tabela não é desenhada e não há por que montar uma linha com capa por jogo a cada tecla da busca.</summary>
    private void RebuildRows(List<TranslationQueueEntry> entries)
    {
        arranged = entries;
        RebuildRows();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (!IsList)
        {
            return;
        }

        var place = 0;
        for (var i = 0; i < arranged.Count; i++)
        {
            var queued = Sort == LibrarySort.Queue && arranged[i].State == TranslationQueueState.Queued;
            Rows.Add(new LibraryQueueRowViewModel(Cards[i], arranged[i], queued ? ++place : null));
        }
    }

    /// <summary>
    /// Pergunta ao VNDB, em segundo plano, se os jogos que a fila usa já têm tradução em português, e guarda a resposta no <c>library.json</c> jogo a jogo. Quando termina, relê a
    /// Biblioteca para a fila mudar de ordem. Uma conferência por vez; sem o conferidor (o shell montado sem VNDB) não faz nada.
    /// </summary>
    private void StartPortugueseCheck()
    {
        if (file is null || portugueseChecker is null || !PortugueseChecking.IsCompleted)
        {
            return;
        }

        PortugueseChecking = CheckPortugueseAsync(file, portugueseChecker);
    }

    private async Task CheckPortugueseAsync(LibraryFile source, Func<PortugueseTranslationChecker> createChecker)
    {
        try
        {
            var checker = createChecker();
            var snapshot = await Task.Run(source.Load);
            if (!snapshot.Games.Any(g => PortugueseTranslationChecker.NeedsCheck(g)))
            {
                return;
            }

            var run = await Task.Run(() => checker.CheckAllAsync(snapshot, onChecked: game => KeepCheck(source, game)));
            if (run.Checked.Count > 0)
            {
                post(() => _ = LoadAsync());
            }
        }
        catch (LibraryException)
        {
            // Um library.json ilegível já vira o aviso da leitura; a conferência só espera por um arquivo bom.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // O library.json travado por outro programa: sem este aviso a fila ficaria em "Não conferido" sem explicação.
            post(() => ErrorMessage = $"Não deu para conferir o português no VNDB: {e.Message}");
        }
    }

    // Grava só a conferência: o jogo pode ter mudado no arquivo desde a leitura, e a resposta só vale para o mesmo jogo do VNDB.
    private static void KeepCheck(LibraryFile source, LibraryGame game) =>
        source.Update(current =>
        {
            if (current.Games.FirstOrDefault(g => g.Id == game.Id) is { } now && now.VndbId == game.VndbId)
            {
                current.Replace(now with { Portuguese = game.Portuguese });
            }
        });
}
