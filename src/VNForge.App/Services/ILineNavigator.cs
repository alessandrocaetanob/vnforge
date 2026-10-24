namespace VNForge.App.Services;

/// <summary>
/// Troca a linha selecionada da grade do Editor. O painel de detalhe só pede; quem implementa (o <c>EditorViewModel</c>) troca o
/// <c>LineDetailViewModel</c> e chama <see cref="IDisposable.Dispose"/> no anterior, o que grava o que estiver pendente.
/// </summary>
public interface ILineNavigator
{
    /// <summary>Seleciona a linha seguinte; no fim da lista não faz nada.</summary>
    void SelectNext();

    /// <summary>Seleciona a linha anterior; no começo da lista não faz nada.</summary>
    void SelectPrevious();
}
