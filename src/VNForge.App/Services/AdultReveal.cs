namespace VNForge.App.Services;

/// <summary>
/// Os jogos de conteúdo adulto que a pessoa pediu para ver sem o borrão no modo Desfocar, pelo id do jogo no VNDB. Vale só nesta sessão: não vai para o disco, e a Biblioteca
/// o zera quando a pessoa sai dela. A grade, a gaveta e a Ficha leem o mesmo registro, então revelar numa tela vale nas outras.
/// </summary>
public sealed class AdultReveal
{
    private readonly HashSet<string> revealed = new(StringComparer.Ordinal);

    /// <summary>Alguém revelou ou borrou de novo um jogo, ou o registro foi zerado.</summary>
    public event EventHandler? Changed;

    public bool IsRevealed(string? vndbId) => vndbId is not null && revealed.Contains(vndbId);

    public void Reveal(string? vndbId)
    {
        if (vndbId is not null && revealed.Add(vndbId))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Conceal(string? vndbId)
    {
        if (vndbId is not null && revealed.Remove(vndbId))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (revealed.Count > 0)
        {
            revealed.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
