# PipelineStepper

As cinco etapas do pipeline (Extrair, Traduzir, Conferir, Build, Injetar) como botões contíguos, cada um com número, nome e pendência. Clicar abre a tela da etapa.

- Concluída: círculo cheio em `seg-approved` com Checkmark.
- Atual (a primeira não concluída): número em `accent-text` e sublinhado de 2px `accent`.
- O subtítulo sempre diz o que falta, não o que já foi feito, exceto em Extrair.

## Avalonia

`ItemsControl` horizontal com `UniformGrid Rows="1"` e `ControlTheme` de `Button` com pseudo-classes `:done` e `:active` definidas pelo ViewModel (`PseudoClasses.Set`).
