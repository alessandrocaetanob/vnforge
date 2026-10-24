# TextboxPreview

Simulação da caixa de texto do jogo com o perfil de formatação aplicado: placa do falante, quebra por palavra na largura em colunas e limite de linhas.

- Cores fixas `textbox-*` nos dois temas: representa o jogo.
- Linhas além do limite em `textbox-overflow`, com a mensagem abaixo em `signal-warning-fg`.
- A largura em colunas e o máximo de linhas vêm de `formatting.json`; a fonte é `textbox` (mono) enquanto a largura for medida em colunas. Se o perfil passar a usar a fonte real do jogo, o controle mede em pixels.

## Avalonia

`Control` próprio (`TextboxPreview`) com a propriedade `Layout` (placa, colunas, limite e linhas já quebradas) e `FontFamily`, que desenha em `Render` com `FormattedText`. Quem quebra é o `LinePreviewer` do `VNForge.Core`, o mesmo serviço do build e do `vnf preview`, chamado pelo `TextboxPreviewViewModel`; nada de reimplementar a quebra na interface. A fonte é monoespaçada genérica até a decisão D6 (#105) sair: trocar `FontFamily` e `TextboxPreview.Metrics` é o ponto de troca.
