namespace VNForge.Core.Glossary;

/// <summary>O que <c>glossary.json</c> e <c>characters.json</c> têm em comum: uma entrada identificada pelo texto original.</summary>
internal interface IGlossaryEntry
{
    string Source { get; }
}
