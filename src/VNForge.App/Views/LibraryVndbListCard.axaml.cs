using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>O cartão da lista do VNDB na gaveta da Biblioteca; a lógica está em <see cref="LibraryVndbListViewModel"/>.</summary>
public partial class LibraryVndbListCard : UserControl
{
    public LibraryVndbListCard()
    {
        InitializeComponent();
        // Voto, datas e notas só vão para o arquivo quando a pessoa sai do campo.
        foreach (var name in new[] { "VoteBox", "StartedBox", "FinishedBox", "NotesBox" })
        {
            this.Get<TextBox>(name).LostFocus += (_, _) => (DataContext as LibraryVndbListViewModel)?.CommitCommand.Execute(null);
        }
    }
}
