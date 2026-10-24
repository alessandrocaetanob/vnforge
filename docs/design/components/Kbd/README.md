# Kbd

Tecla ou combinação de atalho, mostrada dentro de botões, tooltips e da lista de atalhos.

Escreva as teclas como o Windows: `Ctrl+Enter`, `Ctrl+Espaço`, `Alt+↑`, `Shift+F3`. Uma combinação por `Kbd`.

## Avalonia

`Border Classes="kbd"` com `TextBlock` em `mono-xs`, fundo `bg-inset`, borda `stroke-subtle` com 2px embaixo. Dentro de `Button.accent` a borda segue `Foreground`. O texto vem do mesmo `KeyGesture` registrado no `HotKey` ou `KeyBinding`, para nunca divergir.
