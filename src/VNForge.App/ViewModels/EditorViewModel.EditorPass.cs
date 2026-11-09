using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>As sugestões da segunda passada de editor (#532) no Editor: o ícone na grade, o cartão do inspetor e o fim delas quando a pessoa aceita, ignora ou edita a linha.</summary>
public sealed partial class EditorViewModel
{
    private EditorSuggestionsFile editorSuggestions = EditorSuggestionsFile.Empty;

    /// <summary>Lê as sugestões do editor da pasta do projeto; os testes trocam por uma na memória. O padrão lê <c>.cache/editor-suggestions.json</c>.</summary>
    public Func<string, EditorSuggestionsFile>? EditorSuggestionsReader { get; init; }

    /// <summary>
    /// Aplica uma mudança ao arquivo de sugestões da pasta do projeto; os testes trocam por uma na memória. O padrão é <see cref="EditorSuggestionsFile.Update"/>: relê o arquivo e grava só a
    /// mudança, para não apagar o que a passada de editor gravou enquanto o Editor estava aberto com uma cópia velha.
    /// </summary>
    public Action<string, Func<EditorSuggestionsFile, EditorSuggestionsFile>>? EditorSuggestionsWriter { get; init; }

    /// <summary>Pede ao editor outra proposta para a linha de uma sugestão ("Outra"); a janela principal o liga ao provedor. Nulo deixa o botão desligado.</summary>
    public Func<EditorSuggestion, CancellationToken, Task<EditorSuggestion?>>? EditorSuggestionAnother { get; set; }

    /// <summary>Quantas linhas do script aberto têm uma sugestão do editor que ainda vale.</summary>
    public int EditorSuggestionLines => scriptLines.Count(l => l.HasEditorSuggestion);

    /// <summary>Relê as sugestões do arquivo e refaz a grade e o inspetor da linha aberta, sem trocar de linha: a passada de editor termina com o Editor já aberto.</summary>
    public void RefreshEditorSuggestions()
    {
        LoadEditorSuggestions();
        foreach (var line in scriptLines)
        {
            line.SetEditorSuggestion(null);
        }

        ApplyEditorSuggestions();
        if (SelectedLine is { } selected && Detail is { } detail)
        {
            ShowEditorSuggestion(detail, selected);
        }

        OnPropertyChanged(nameof(EditorSuggestionLines));
    }

    private void LoadEditorSuggestions()
    {
        editorSuggestions = Project?.Root is { } root ? ReadEditorSuggestions(root) : EditorSuggestionsFile.Empty;
    }

    private EditorSuggestionsFile ReadEditorSuggestions(string root)
    {
        if (EditorSuggestionsReader is { } reader)
        {
            return reader(root);
        }

        try
        {
            return EditorSuggestionsFile.Load(ProjectFolder.Open(root));
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return EditorSuggestionsFile.Empty;
        }
    }

    /// <summary>Entrega a cada linha do script aberto a sugestão dela, se houver; chama-se depois de criar as linhas.</summary>
    private void ApplyEditorSuggestions()
    {
        if (SelectedScript is not { } script || editorSuggestions.Suggestions.Count == 0)
        {
            return;
        }

        var byUnit = editorSuggestions.ByUnit(script.Key);
        foreach (var line in scriptLines)
        {
            if (byUnit.TryGetValue(line.Id, out var suggestion))
            {
                line.SetEditorSuggestion(suggestion);
            }
        }
    }

    private void ShowEditorSuggestion(LineDetailViewModel detail, LineViewModel line)
    {
        detail.EditorSuggestionResolved = suggestion => ResolveEditorSuggestion(line, suggestion);
        detail.EditorSuggestionAnother = EditorSuggestionAnother is null ? null : (current, ct) => AskAnotherEditorSuggestionAsync(line, current, ct);
        var live = scriptLines.Where(l => l.HasEditorSuggestion).ToList();
        var position = live.IndexOf(line);
        detail.ShowEditorSuggestion(position < 0 ? null : line.EditorSuggestion, position + 1, live.Count);
    }

    /// <summary>
    /// "Outra": pede a proposta nova e, quando ela chega, a grade e a lista em memória passam a tê-la (o arquivo já foi gravado por quem a pediu). Isso fica aqui, e não no painel, porque
    /// a pessoa pode ter trocado de linha enquanto o modelo pensava e o painel já foi descartado. Se ela aceitou ou ignorou a sugestão nesse meio-tempo, a resposta é descartada.
    /// </summary>
    private async Task<EditorSuggestion?> AskAnotherEditorSuggestionAsync(LineViewModel line, EditorSuggestion current, CancellationToken ct)
    {
        var another = await EditorSuggestionAnother!(current, ct);
        if (another is null || line.EditorSuggestion is not { } now || !string.Equals(now.UnitId, current.UnitId, StringComparison.Ordinal))
        {
            return null;
        }

        editorSuggestions = editorSuggestions.Replacing(another);
        line.SetEditorSuggestion(another);
        return another;
    }

    /// <summary>A pessoa aceitou ou ignorou a sugestão: ela sai do arquivo e da grade.</summary>
    private void ResolveEditorSuggestion(LineViewModel line, EditorSuggestion suggestion)
    {
        line.SetEditorSuggestion(null);
        DropEditorSuggestion(suggestion.ScriptKey, suggestion.UnitId);
        OnPropertyChanged(nameof(EditorSuggestionLines));
    }

    /// <summary>A linha foi gravada: uma sugestão que deixou de valer (outro texto, ou aprovada) sai do arquivo, para não voltar se o texto antigo for escrito de novo.</summary>
    private void PruneEditorSuggestion(string scriptKey, TextUnit saved)
    {
        if (editorSuggestions.Suggestions.Any(s => string.Equals(s.ScriptKey, scriptKey, StringComparison.Ordinal) && string.Equals(s.UnitId, saved.Id, StringComparison.Ordinal) && !s.IsLiveFor(saved)))
        {
            DropEditorSuggestion(scriptKey, saved.Id);
            OnPropertyChanged(nameof(EditorSuggestionLines));
        }
    }

    private void DropEditorSuggestion(string scriptKey, string unitId)
    {
        var without = editorSuggestions.Without(scriptKey, unitId);
        if (ReferenceEquals(without, editorSuggestions) || Project?.Root is not { } root)
        {
            return;
        }

        editorSuggestions = without;
        try
        {
            Func<EditorSuggestionsFile, EditorSuggestionsFile> remove = file => file.Without(scriptKey, unitId);
            if (EditorSuggestionsWriter is { } writer)
            {
                writer(root, remove);
            }
            else
            {
                EditorSuggestionsFile.Update(ProjectFolder.Open(root), remove);
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível gravar a decisão sobre a sugestão do editor: {e.Message}";
        }
    }
}
