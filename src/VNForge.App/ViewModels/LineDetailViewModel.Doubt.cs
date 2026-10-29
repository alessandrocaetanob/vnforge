namespace VNForge.App.ViewModels;

public sealed partial class LineDetailViewModel
{
    /// <summary>A IA registrou uma dúvida sobre a tradução gravada; aprovar a linha ou reescrever a tradução a encerra, e o cartão some quando a gravação chega.</summary>
    public bool HasDoubt => Saved.Doubt is not null;

    /// <summary>O tipo da dúvida no título do cartão ("Dúvida · Sujeito oculto"); vazio sem dúvida.</summary>
    public string DoubtTitle => Saved.Doubt is { } doubt ? $"Dúvida · {doubt.KindLabel}" : string.Empty;

    /// <summary>A frase da IA no cartão; vazia sem dúvida.</summary>
    public string DoubtNote => Saved.Doubt?.Note ?? string.Empty;

    private void NotifyDoubtChanged()
    {
        OnPropertyChanged(nameof(HasDoubt));
        OnPropertyChanged(nameof(DoubtTitle));
        OnPropertyChanged(nameof(DoubtNote));
    }
}
