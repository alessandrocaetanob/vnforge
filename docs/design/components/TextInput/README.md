# TextInput

Campo de uma linha: rótulo em `overline` acima, ícone opcional à esquerda, atalho opcional à direita. Foco: borda `accent` e sublinhado de 1px.

Placeholders descrevem o que digitar ("Texto, id ou personagem"), não repetem o rótulo.

## Avalonia

`TextBox` do Fluent com `InnerLeftContent` (ícone) e `InnerRightContent` (Kbd). Fundo `bg-inset`, `CornerRadius` `radius-sm`, altura `control-md`. Caminhos de pasta usam `StorageProvider.OpenFolderPickerAsync` no botão ao lado.
