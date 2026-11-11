namespace VNForge.App.Tests;

/// <summary>
/// Espera por condição com prazo, no lugar de dormir um tempo fixo: o teste segue assim que o que ele espera acontece e falha com a descrição se não vier.
/// Fica fora do Support.cs porque este usa o xUnit e o Support.cs também é compilado pela ferramenta de capturas.
/// </summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string description, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, $"A condição não chegou a tempo: {description}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
