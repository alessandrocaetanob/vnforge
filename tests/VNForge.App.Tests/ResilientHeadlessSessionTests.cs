using System.Runtime.CompilerServices;

namespace VNForge.App.Tests;

public class ResilientHeadlessSessionTests
{
    // O nome do método é o que o filtro lê na pilha; o Avalonia lança de um método com este nome.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureIsolatedApplication() => throw new InvalidOperationException("The calling thread cannot access this object because a different thread owns it.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OutroLugar() => throw new InvalidOperationException("outra falha");
}
