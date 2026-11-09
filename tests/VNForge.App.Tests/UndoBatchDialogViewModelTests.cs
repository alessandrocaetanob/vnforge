using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>O diálogo "Desfazer o lote": o título com a preposição e sem aspas, e a contagem de linhas com milhar.</summary>
public class UndoBatchDialogViewModelTests
{
    private static readonly DateTimeOffset Sent = new(2026, 11, 25, 5, 12, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Sala de aula", "Desfazer o lote da Sala de aula?")]
    [InlineData("Esta cena", "Desfazer o lote desta cena?")]
    [InlineData("O capítulo", "Desfazer o lote do capítulo?")]
    [InlineData("O jogo todo", "Desfazer o lote do jogo todo?")]
    [InlineData("As cenas escolhidas", "Desfazer o lote das cenas escolhidas?")]
    public async Task O_titulo_usa_a_preposicao_do_nome_do_lote_sem_aspas(string batch, string title)
    {
        var vm = new UndoBatchDialogViewModel();

        var asking = vm.AskAsync(batch, Sent, new RestoreUndoResult(10, 0, 0));

        Assert.Equal(title, vm.Title);
        vm.KeepCommand.Execute(null);
        Assert.False(await asking);
    }

    [Theory]
    [InlineData(1, 0, "Desfazer 1 linha", "Volta 1 linha ao que eram antes do lote de 25/11.")]
    [InlineData(2, 0, "Desfazer 2 linhas", "Volta 2 linhas ao que eram antes do lote de 25/11.")]
    [InlineData(1187, 17, "Desfazer 1.187 linhas", "Volta 1.204 linhas ao que eram antes do lote de 25/11. As 17 linhas que você editou depois ficam como estão.")]
    [InlineData(12500, 1, "Desfazer 12.500 linhas", "Volta 12.501 linhas ao que eram antes do lote de 25/11. A linha que você editou depois fica como está.")]
    public async Task O_botao_e_a_mensagem_contam_as_linhas_com_milhar_e_no_singular_quando_e_uma(int restored, int edited, string label, string message)
    {
        var vm = new UndoBatchDialogViewModel();

        var asking = vm.AskAsync("Sala de aula", Sent, new RestoreUndoResult(restored, edited, 0));

        Assert.Equal(label, vm.ConfirmLabel);
        Assert.Equal(message, vm.Message);
        vm.KeepCommand.Execute(null);
        Assert.False(await asking);
    }
}
