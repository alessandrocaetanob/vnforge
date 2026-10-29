using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class CharacterSheetView : UserControl
{
    public CharacterSheetView()
    {
        InitializeComponent();
    }

    /// <summary>Esc fecha a ficha de qualquer ponto dela.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && DataContext is CharacterSheetViewModel sheet)
        {
            sheet.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Traz a ficha para a área visível da página rolada e põe o foco nela, para o teclado e o leitor de tela começarem por ela.</summary>
    public void FocusSheet()
    {
        this.BringIntoView();
        Focus();
    }
}
