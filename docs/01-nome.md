# 01 — Nome do projeto

## Critérios

- Curto, fácil de digitar como comando de CLI (3 a 5 letras de alias).
- Não preso ao YU-RIS: o plano é suportar outras engines depois.
- Funciona como namespace C# (`Xxx.Core`, `Xxx.Cli`...).
- Disponível no GitHub e no NuGet (verificar antes de criar o repo).

## Opções

| Nome | Comando | Ideia | Prós | Contras |
|---|---|---|---|---|
| **VNForge** | `vnf` | "forja" de VNs: desmonta, traduz, remonta | Diz o que faz (extrair, reconstruir); bom namespace | "Forge" é um sufixo muito usado |
| **Ponte** | `ponte` | Ponte entre o japonês e o português | Nome brasileiro, memorável | Genérico demais para busca |
| **Kotoba Studio** | `kotoba` | 言葉 = "palavras" | Tem cara de VN | Pode soar como ferramenta só de japonês |
| **Honyaku** | `hny` | 翻訳 = "tradução" | Direto ao ponto | Difícil de soletrar para quem não conhece |
| **Tradutora VN** | `tvn` | Literal | Ninguém tem dúvida do que é | Sem personalidade |

## Recomendação

**VNForge**, com o executável da CLI chamado `vnf`.

O nome descreve o ciclo completo (não só traduzir, mas desmontar e remontar os arquivos), é neutro em relação à engine e gera namespaces limpos: `VNForge.Core`, `VNForge.Engines.YuRis`, `VNForge.Cli`, `VNForge.App`.

Os documentos seguintes usam VNForge. Se escolhermos outro nome, é só trocar.

## Decidido

- [x] Nome: **VNForge**, CLI `vnf`.
- [x] Repositório **privado** no GitHub por enquanto. Licença fica para quando (e se) abrirmos o código.

## Em aberto

- [ ] Verificar disponibilidade de `vnforge` no NuGet, caso um dia publiquemos a biblioteca.
