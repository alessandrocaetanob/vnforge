using System.Runtime.CompilerServices;

namespace VNForge.App.Tests;

public class ResilientHeadlessSessionTests
{
    // O nome do método é o que o filtro lê na pilha; o Avalonia lança de um método com este nome.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureIsolatedApplication() => throw new InvalidOperationException("The calling thread cannot access this object because a different thread owns it.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OutroLugar() => throw new InvalidOperationException("outra falha");

    [Fact]
    public async Task Falha_de_preparacao_repete_uma_vez_e_o_corpo_roda_uma_vez()
    {
        var tentativas = 0;
        var corpos = 0;
        var logs = new List<string>();

        var resultado = await ResilientHeadlessSession.RunWithRetry(
            () =>
            {
                if (++tentativas == 1)
                {
                    EnsureIsolatedApplication();
                }

                corpos++;
                return Task.FromResult(7);
            },
            logs.Add);

        Assert.Equal(7, resultado);
        Assert.Equal(2, tentativas);
        Assert.Equal(1, corpos);
        Assert.Single(logs);
        Assert.Contains("#510", logs[0]);
    }

    [Fact]
    public async Task Outra_excecao_nao_e_repetida()
    {
        var tentativas = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ResilientHeadlessSession.RunWithRetry<int>(
            () =>
            {
                tentativas++;
                OutroLugar();
                return Task.FromResult(0);
            },
            _ => Assert.Fail("não devia registrar repetição")));

        Assert.Equal("outra falha", ex.Message);
        Assert.Equal(1, tentativas);
    }

    [Fact]
    public async Task Excecao_de_outro_tipo_vinda_da_preparacao_nao_e_repetida()
    {
        var tentativas = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => ResilientHeadlessSession.RunWithRetry<int>(
            () =>
            {
                tentativas++;
                return Task.FromException<int>(new ArgumentException("x"));
            },
            _ => { }));

        Assert.Equal(1, tentativas);
    }

    [Fact]
    public async Task Duas_falhas_de_preparacao_propagam_a_segunda()
    {
        var tentativas = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResilientHeadlessSession.RunWithRetry<int>(
            () =>
            {
                tentativas++;
                EnsureIsolatedApplication();
                return Task.FromResult(0);
            },
            _ => { }));

        Assert.Equal(2, tentativas);
    }
}
