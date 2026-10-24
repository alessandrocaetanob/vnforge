namespace VNForge.Core.Tests.Projects;

/// <summary><see cref="IProgress{T}"/> que entrega na hora; o <see cref="Progress{T}"/> do .NET posta para outra thread e perde a ordem.</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
