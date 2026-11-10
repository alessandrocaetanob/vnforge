namespace VNForge.App.Services;

/// <summary>O seletor de pasta que nunca escolhe: vale para a Biblioteca quando o app é montado sem janela (os testes), onde ninguém clica em "Escolher pasta".</summary>
internal sealed class NoFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}
