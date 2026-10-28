using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Os nomes que as escolhas de <see cref="StyleConfig"/> têm no <c>vnforge.json</c>.</summary>
internal static class StyleNames
{
    public static readonly IReadOnlyDictionary<string, HonorificsPolicy> Honorifics = new Dictionary<string, HonorificsPolicy>(StringComparer.Ordinal)
    {
        ["keep"] = HonorificsPolicy.Keep,
        ["remove"] = HonorificsPolicy.Remove,
        ["adapt"] = HonorificsPolicy.Adapt,
    };

    public static readonly IReadOnlyDictionary<string, AddressForm> Addresses = new Dictionary<string, AddressForm>(StringComparer.Ordinal)
    {
        ["voce"] = AddressForm.Voce,
        ["tu"] = AddressForm.Tu,
    };

    public static readonly IReadOnlyDictionary<string, NarratorPerson> Persons = new Dictionary<string, NarratorPerson>(StringComparer.Ordinal)
    {
        ["first"] = NarratorPerson.First,
        ["third"] = NarratorPerson.Third,
    };

    public static readonly IReadOnlyDictionary<string, NarratorGender> Genders = new Dictionary<string, NarratorGender>(StringComparer.Ordinal)
    {
        ["neutral"] = NarratorGender.Neutral,
        ["male"] = NarratorGender.Male,
        ["female"] = NarratorGender.Female,
    };

    public static readonly IReadOnlyDictionary<string, SpeakerDelimiterStyle> SpeakerDelimiters = new Dictionary<string, SpeakerDelimiterStyle>(StringComparer.Ordinal)
    {
        ["none"] = SpeakerDelimiterStyle.None,
        ["guillemets"] = SpeakerDelimiterStyle.Guillemets,
    };

    public static string Of(HonorificsPolicy value) => NameOf(Honorifics, value);

    public static string Of(AddressForm value) => NameOf(Addresses, value);

    public static string Of(NarratorPerson value) => NameOf(Persons, value);

    public static string Of(NarratorGender value) => NameOf(Genders, value);

    public static string Of(SpeakerDelimiterStyle value) => NameOf(SpeakerDelimiters, value);

    private static string NameOf<T>(IReadOnlyDictionary<string, T> names, T value) where T : struct, Enum =>
        names.First(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Key;
}
