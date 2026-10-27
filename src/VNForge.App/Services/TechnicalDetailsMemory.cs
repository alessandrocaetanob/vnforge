namespace VNForge.App.Services;

/// <summary>
/// Lembra, por página, se "Detalhes técnicos" ficou aberto ou fechado. As páginas são recriadas a cada visita, então o estado vive aqui;
/// grava <c>technical-details.json</c> (ver <see cref="BoolMemory"/>).
/// </summary>
public sealed class TechnicalDetailsMemory(string? directory = null) : BoolMemory(directory, "technical-details.json");
