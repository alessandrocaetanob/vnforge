# TranslationField

Par Original + Tradução do inspetor. O original é somente leitura em `jp-body`, com o nome do falante antes; a tradução é um editor de várias linhas com marcadores como chips.

- Borda inferior de 2px em `accent` indica o campo ativo da tela.
- Linha de metadados: caracteres, linhas e colunas depois da quebra (mesmo cálculo da prévia) e o estado dos marcadores.
- Salva a cada edição (debounce de 400 ms) no `text/<script>.json`.

## Avalonia

`AvaloniaEdit.TextEditor` (pacote `Avalonia.AvaloniaEdit`, MIT) com `WordWrap=True`, sem numeração de linha, `FontFamily` da família `ui`, e o gerador de chips do `MarkerChip`. Um `TextBox` comum não consegue tratar o marcador como bloco atômico. Verificar a versão do AvaloniaEdit compatível com Avalonia 12 antes de fixar o pacote.

Atalhos: `Ctrl+Enter` aprova e avança, `Ctrl+Espaço` pede sugestão, `Ctrl+Shift+Enter` copia o original para a tradução.
