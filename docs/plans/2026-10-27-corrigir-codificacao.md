# Corrigir com um clique o que a codificação não grava

Issue #417. Achado do teste da teste.11 (roteiro `2026-10-27-roteiro-teste-v4.md`).

## O que aconteceu

O projeto de teste usa a estratégia `cp1252` e tem 6.854 linhas, 6.221 traduzidas. `vnf check` acusou 4.115 erros `caractere-nao-codificavel` em 1.748 linhas: 4.114 são o `…` (U+2026) e 1 é um `♪` (U+266A) na `yst00010`.

A tradução gravada nessas 1.748 linhas tem três pontos (`...`), não o `…`. Quem cria o `…` é o ajuste Reticências = "Caractere …" (`typography.ellipsis: "character"` no `formatting.json`). A regra confere o texto depois da formatação (`BuiltText.For`), então vê o `…`, e a `Cp1252TextEncoder` o recusa porque ele vira o byte `0x85`, que o motor lê como lead byte do Shift-JIS. Três defeitos saem daí:

- A mensagem de cada linha diz "Troque por três pontos (...)" numa linha que já tem três pontos.
- O "Corrigir N com um clique" não muda nada: o corretor (`JapanesePunctuation.FixUnencodable`) olha a tradução gravada, que está certa.
- Ajustes deixa escolher uma opção que a estratégia do projeto nunca grava.

O `♪` é o caso que o corretor também não cobre: a tabela atual só tem pontuação japonesa.

## Decisões

Tomadas com o Alessandro em 27/10/2026.

1. Erro que vem de um ajuste de formatação é acusado como ajuste, e a Conferência oferece trocar o ajuste num clique. Ajustes desliga as opções que a estratégia não grava. A alternativa de a formatação se adaptar em silêncio foi descartada: o ajuste escolhido deixaria de valer sem aviso.
2. O corretor de caractere não gravável usa uma tabela ampla, não só pontuação. Só troca quando o substituto é gravável na estratégia; caractere sem substituto continua como erro, para a pessoa decidir.
3. "Corrigir N com um clique" e `vnf check --fix` também trocam o ajuste em conflito, porque esse ajuste não funciona com a estratégia em nenhuma linha. O aviso depois da correção diz qual ajuste mudou e para quê.

## Core

**Conflito de ajuste (novo, `Formatting/FormattingConflicts.cs`).** `FormattingConflicts.Find(FormattingProfile, ITextEncoder)` devolve, para cada ajuste de tipografia ou largura que produz caractere que o encoder não grava, um `FormattingConflict` com o campo (`typography.ellipsis`), o valor atual, o caractere produzido, o rótulo em português ("Reticências: Caractere …") e o valor seguro. `FormattingConflicts.Resolve` devolve o perfil com os valores seguros. A tabela de valores seguros:

| Ajuste | Produz | Valor seguro |
|---|---|---|
| Reticências = Caractere | `…` | Três pontos (também desfaz `…` digitado na tradução) |
| Aspas = 「 」 | `「 」` | Aspas retas |
| Travessão (`typography.dash`) | o caractere escolhido | nenhum (`null`) |
| Travessão de fala (`typography.dialogueDash`) | o caractere escolhido | `-` quando gravável, senão nenhum |
| Largura = cheia | ASCII de largura cheia | nenhuma |

Substituições do usuário (E4.5) ficam de fora: o texto delas é livre, e o caractere que produzem continua acusado por linha.

**Nova regra `ajuste-nao-codificavel` (erro).** Uma ocorrência por linha afetada, com a mensagem "O ajuste Reticências (Caractere …) gera '…', que a estratégia cp1252 não grava." A `UnencodableCharacterRule` deixa de acusar o caractere que um conflito produz, para a mesma linha não aparecer duas vezes. O id é novo e estável, como pede `IQaRule.Id`.

**Corretor ampliado.** Um tipo novo, `Text/UnwritableCharacters.cs`, troca cada caractere que o encoder não grava pelo primeiro substituto gravável, nesta ordem: a tabela de `JapanesePunctuation`; uma tabela ocidental (aspas curvas para retas, `– — ―` para `-`, `…` para `...`, `♪ ♫` para `~`, `★ ☆` para `*`, `™` para `TM`, `©` para `(c)`, `•` para `*`, a mesma lista do `CharacterFolder`); e a letra decomposta sem as marcas latinas U+0300 a U+036F (`ï` vira `i`, `ő` vira `o`). Marcadores de controle nunca são tocados. Caractere sem substituto gravável fica como está. `UnencodableCharacterRule.Fix` passa a chamar esse tipo. A mensagem da regra sugere o substituto que o corretor usaria, em vez de uma frase fixa por caractere.

**`QaFixer`.** `QaFixResult` ganha a lista de ajustes trocados. `QaFixer.Apply` resolve os conflitos antes das correções de texto e grava o `formatting.json` só quando algo mudou, dentro da mesma trava (`ProjectTextLock`). `Plan` e `Propose` não mudam: continuam só texto.

## CLI

`vnf check` agrupa `ajuste-nao-codificavel` numa linha por ajuste: "Reticências (Caractere …): 4.114 erros em 1.748 linhas. A estratégia cp1252 não grava '…'. `vnf check --fix` troca para Três pontos." `--fix` troca o ajuste e escreve "formatting.json: Reticências passou de Caractere … para Três pontos." O JSON ganha `settingsFixed` com campo, antes e depois.

## App

**Conferência.** Os problemas de `ajuste-nao-codificavel` viram um card por ajuste no topo da lista de erros, no lugar das 4.114 linhas: título "O ajuste Reticências gera 4.114 erros", texto "A estratégia cp1252 não grava o '…'. As traduções têm três pontos; o ajuste é que troca." e o botão de destaque "Usar três pontos". O botão grava o `formatting.json` pelo Core, avisa o resto do app (`TextsChanged`; Ajustes relê o perfil ao abrir, o que a implementação confere) e confere de novo. A contagem do cabeçalho "Corrigir N com um clique" inclui as linhas que o ajuste resolve.

**Ajustes.** O `Segmented` de Reticências e o de Aspas desligam a opção que a estratégia atual não grava, com dica "A estratégia cp1252 não grava o '…'.". Quando a pessoa troca a estratégia e o ajuste atual entra em conflito, uma faixa de aviso abaixo do grupo diz o mesmo e oferece "Usar três pontos". `ChoiceItem<T>` ganha `IsEnabled` e `DisabledReason`.

## Testes

Primeiro os testes, depois o código.

- Core: `FormattingConflicts.Find` acha cada linha da tabela em `cp1252` e nenhuma em `sjis` onde o caractere é gravável; `Resolve` devolve perfil sem conflito. `UnwritableCharacters` troca cada item da tabela em `cp1252` e em `sjis`, não toca marcador, deixa caractere sem substituto e é idempotente. `ajuste-nao-codificavel` acusa uma vez por linha e `caractere-nao-codificavel` cala nesse caractere. `QaFixer.Apply` sobre um projeto sintético com Reticências = Caractere e um `♪` grava o perfil com Três pontos, troca o `♪` por `~` e uma segunda rodada não acha nada.
- CLI: saída agrupada e `--fix` com `settingsFixed` no JSON.
- App: o card aparece com a contagem certa e o botão grava o perfil; a opção fica desligada em Ajustes com a estratégia cp1252. Capturas de Rendering do card e de Ajustes nos dois temas.

## Documentação

`docs/06-yuris-notas-tecnicas.md` (seção "Acentos e fonte") ganha a lista de substitutos do corretor. O roteiro da teste.12 confere a Conferência do projeto de teste: zero erros de codificação depois de um clique.

## Ordem

1. Core: conflitos, regra nova e corretor ampliado (um PR).
2. CLI e App sobre o Core já mesclado (um PR cada, podem andar juntos).
3. Release teste.12 e o reteste na Conferência do projeto real.
