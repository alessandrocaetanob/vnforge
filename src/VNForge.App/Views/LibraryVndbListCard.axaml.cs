using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>O cartão da lista do VNDB na gaveta da Biblioteca; a lógica está em <see cref="LibraryVndbListViewModel"/>.</summary>
public partial class LibraryVndbListCard : UserControl
{
    public LibraryVndbListCard()
    {
        InitializeComponent();
        // As notas só vão para o arquivo quando a pessoa sai do campo.
        this.Get<TextBox>("NotesBox").LostFocus += (_, _) => (DataContext as LibraryVndbListViewModel)?.CommitNotesCommand.Execute(null);
    }
}
