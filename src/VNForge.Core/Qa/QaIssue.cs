namespace VNForge.Core.Qa;

/// <summary>Um problema que uma regra achou numa linha.</summary>
/// <param name="RuleId">Id da regra (<see cref="IQaRule.Id"/>); é o valor que "marcar como ok" grava em <see cref="Text.TextUnit.Flags"/>.</param>
/// <param name="Severity">Gravidade desta ocorrência.</param>
/// <param name="UnitId">Id da linha (<c>yst00012:000034</c>).</param>
/// <param name="ScriptKey">Chave do script (<c>yst00012</c>), a de <c>text/&lt;chave&gt;.json</c>.</param>
/// <param name="Message">Descrição em português, sem o id da linha (quem lista o junta).</param>
public sealed record QaIssue(string RuleId, QaSeverity Severity, string UnitId, string ScriptKey, string Message);
