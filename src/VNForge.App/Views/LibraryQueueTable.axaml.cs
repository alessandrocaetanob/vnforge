using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>A lista da Biblioteca como fila de tradução. O modelo é o da Biblioteca; as linhas vêm prontas dele.</summary>
public sealed partial class LibraryQueueTable : UserControl
{
    /// <summary>A folga do Border de fora (8 em cima, 24 embaixo), os 16 entre o cartão e o aviso, o cabeçalho de 38 e as bordas do cartão.</summary>
    private const double Reserved = 8 + 24 + 16 + 38 + 2;

    public LibraryQueueTable()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => FitList();
    }

    /// <summary>
    /// O aviso vem logo abaixo do cartão, então o cartão não pode tomar a altura toda: a lista rola dentro do que sobra depois do cabeçalho e do aviso, e
    /// com poucas linhas o cartão tem a altura delas.
    /// </summary>
    private void FitList()
    {
        var room = Bounds.Height - Reserved - QueueNote.Bounds.Height;
        if (room > 0 && Math.Abs(QueueScroll.MaxHeight - room) > 0.5)
        {
            QueueScroll.MaxHeight = room;
        }
    }
}
