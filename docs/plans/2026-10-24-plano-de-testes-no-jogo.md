# Plano de testes no jogo

Com a V2 na `main` (issues #239 a #252, #234 e #235), só restam as issues com o label "no jogo". Elas precisam do jogo YU-RIS instalado, da chave da Anthropic e de alguém jogando, então nenhum agente fecha essas issues sozinho. Este plano põe as onze issues numa ordem em que cada etapa usa o resultado da anterior.

## Antes de começar

- Jogo instalado numa pasta própria e uma cópia intacta dela fora do projeto. O `vnf inject` já faz backup, e a cópia cobre o caso de o backup falhar.
- `vnf` publicado da `main` (`dotnet publish src/VNForge.Cli -c Release -r win-x64 -p:PublishSingleFile=true --self-contained`, como na D5 do `docs/03-cli.md`) e o app compilado da mesma revisão.
- `ANTHROPIC_API_KEY` só na sessão do terminal, ou a chave gravada pelo `vnf config ai`. Nunca num arquivo do projeto.
- Projeto criado fora da pasta do jogo: `vnf init minha-vn-ptbr --game "<pasta do jogo>"`.
- Cada problema achado vira issue com o passo, o comando, a saída e o print do jogo. Texto extraído do jogo não entra na issue, só o id da linha.

## Etapa 1 · Ida e volta dos binários

Nada é gravado no jogo nesta etapa.

| Issue | Passos | Pronto quando |
|---|---|---|
| #31 Ida e volta do YPF | Para cada `.ypf`: `vnf ypf unpack <jogo>\ysbin.ypf C:\tmp\ysbin`, `vnf ypf pack C:\tmp\ysbin --original <jogo>\ysbin.ypf --output C:\tmp\ysbin.ypf` e `certutil -hashfile` nos dois arquivos | Todos os `.ypf` saem idênticos; a variante encontrada (versão, compressão, tabela de nomes) está no `docs/06-yuris-notas-tecnicas.md` |
| #41 Ida e volta dos scripts | `vnf extract`; depois reconstruir cada `yst*.ybn` sem mudança e comparar com o de `.cache/game/ysbin.ypf/`. O `vnf build` só reconstrói scripts com tradução diferente do `source`, então ele sozinho não exercita este caso: se o teste de integração do repo não puder apontar para a pasta do jogo, abra uma issue para um modo de reconstrução sem mudanças antes de seguir | Todos os `yst*.ybn` idênticos; versão do YSTB, chave XOR e contagem de unidades por `kind` no doc 06 |

Se um arquivo sair diferente, pare aqui: a regra de reescrita mínima do `CLAUDE.md` vale antes de qualquer teste com tradução.

## Etapa 2 · Caixa de texto e codificação

| Issue | Passos | Pronto quando |
|---|---|---|
| #59 Largura e linhas da caixa | Escrever em `text/<script>.json` uma fala longa em ASCII numa linha da primeira cena, `vnf preview <id>`, `vnf build`, `vnf inject`, `vnf run` e contar colunas e linhas na caixa | A quebra no jogo cai onde o `vnf preview` mostra; colunas e linhas gravadas no perfil de exemplo do `formatting.json` |
| #62 Lead bytes e charset da fonte | Trabalhar só numa cópia do executável: localizar a tabela de 256 lead bytes, testar zerá-la, ver o `charset` passado ao `CreateFontIndirectA` | Endereço da tabela e caminho escolhido para a fonte no doc 06; decisão registrada entre seguir com `cp1252` ou partir para `remap` (#66) |

A decisão do #62 define se o #66 e o #65 entram numa versão seguinte. O critério do #67 é acento certo no jogo, então sem `cp1252` funcionando a etapa 3 espera o `remap` (#66). Com a estratégia `fold` (acento trocado pela letra sem acento) dá para adiantar o #59 e os passos do #85 que não dependem de acento, mas o #67 não fecha.

## Etapa 3 · Marcos M2 e M3

| Issue | Passos | Pronto quando |
|---|---|---|
| #67 Validação do M2 | Traduzir à mão uma cena curta no Editor do app, com diálogo, nome de personagem e escolha; Build no app ou `vnf build` e `vnf inject`; jogar a cena; depois `vnf restore` | Acentos certos nos três tipos de texto, quebras onde a prévia mostra, `vnf restore` deixa a pasta igual à cópia intacta |
| #85 Validação do M3 | Um capítulo inteiro: `vnf translate --files "yst0001*" --dry-run` (troque o padrão pelos scripts do capítulo) para ver o custo, `vnf translate`, `vnf check`, revisão na Conferência e no Editor do app, Build e injeção pelo app, jogar o capítulo | O capítulo roda no jogo sem erro de QA pendente |

O #85 também é o teste de uso da V2: paleta de comandos, Painel, Editor, Conferência e Build com um projeto real. Diferenças de layout ou ação errada viram issue com o label "área: app".

## Etapa 4 · Jogo inteiro (M4)

| Issue | Passos | Pronto quando |
|---|---|---|
| #112 Traduzir e revisar tudo | `vnf translate` com `--dry-run` antes, `vnf review`, revisão no app até tudo aprovado | `vnf status` mostra todas as linhas aprovadas |
| #113 Ajuste fino de formatação | Ajustar o `formatting.json` com o que aparecer no jogo; `vnf check` depois de cada ajuste | Nenhuma fala estoura a caixa de texto |
| #114 Imagens com texto | Listar menus, títulos e outras imagens com texto, com o arquivo e o pacote de cada uma | Lista no doc 06, como entrada para o E12 (#15) |
| #115 Jogar do início ao fim | Jogar com a tradução injetada, todas as rotas | Jogo terminado; cada problema virou issue |

Com o #115 fechado, o #12 (E11, M4) fecha junto.

## Ordem e dependências

```
#31 ─► #41 ─► #59 ─► #62 ─► #67 (M2) ─► #85 (M3) ─► #112 ─► #113 ─► #115 ─► #12 (M4)
                                                    └─► #114
```

O #114 só precisa do jogo extraído e pode correr junto com o #112.
