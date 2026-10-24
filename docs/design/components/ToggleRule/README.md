# ToggleRule

Linha de configuração liga/desliga: nome da regra, exemplo do efeito e o interruptor à direita. Usada no perfil de formatação e nas regras do QA.

O texto auxiliar mostra um antes/depois real, não uma explicação abstrata.

## Avalonia

`Grid ColumnDefinitions="*,Auto"` com `ToggleSwitch` (sem `OnContent`/`OffContent`). Lista dentro de `Border` `bg-card` com divisórias `stroke-subtle`. Cada interruptor vincula a uma propriedade do `FormattingProfile`; a prévia ao vivo da página recalcula a cada mudança.
