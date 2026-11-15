using System.Reflection;

namespace VNForge.Tests;

/// <summary>A regra de docs/04-testes.md "toda classe de teste aponta um risco": falha se uma classe com teste não leva <c>[Trait("Risco", "R1" a "R9")]</c>. Este arquivo é compilado em cada projeto de teste, menos no App, que tem o seu; esta classe fica fora da própria conta.</summary>
public sealed class RiskTraitTests
{
    private static readonly string[] Risks = ["R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9"];

    [Fact]
    public void Toda_classe_de_teste_leva_um_trait_de_risco_da_tabela()
    {
        var problems = new List<string>();
        foreach (var type in typeof(RiskTraitTests).Assembly.GetTypes().Where(IsTestClass))
        {
            var risks = Traits(type).ToList();
            if (risks.Count == 0)
            {
                problems.Add($"{type.FullName}: sem [Trait(\"Risco\", ...)]");
            }
            else if (risks.Except(Risks, StringComparer.Ordinal).ToList() is { Count: > 0 } unknown)
            {
                problems.Add($"{type.FullName}: id fora de R1 a R9 ({string.Join(", ", unknown)})");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static bool IsTestClass(Type type) =>
        type != typeof(RiskTraitTests)
        && type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Any(m => m.IsDefined(typeof(FactAttribute), inherit: true));

    private static IEnumerable<string> Traits(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            foreach (var trait in current.GetCustomAttributes<TraitAttribute>())
            {
                if (trait.Name == "Risco")
                {
                    yield return trait.Value;
                }
            }
        }
    }
}
