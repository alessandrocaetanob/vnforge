# VNForge.Installer

O executável que o jogador roda para pôr a tradução no jogo. O `vnf release` junta este executável ao `.vnfpatch` num arquivo só (`InstallerBundle`: executável, patch, tamanho e a marca `VNFINST1`).

## O que o jogador vê

Cinco janelas de 720 por 480, as do quadro da #540, com o tema do app (claro e escuro pelo tema do Windows):

1. Pasta do jogo: procura a pasta (a atual, a do instalador e a de cima), mostra o caminho e o cartão "Versão do jogo conferida". **Procurar** escolhe outra.
2. Instalando: conferir a versão, guardar a cópia dos originais, aplicar a tradução, conferir o resultado. **Cancelar** funciona até o fim de "Guardar a cópia dos originais"; depois o jogo já está sendo trocado e o botão apaga.
3. Concluído: quantos arquivos mudaram e onde estão os originais. **Ler o leia-me**, **Fechar** e **Abrir o jogo**.
4. Versão errada do jogo: o arquivo diferente e os dois SHA-256. Nada é gravado.
5. Já instalada: **Voltar ao jogo original** devolve os arquivos byte a byte, sem mexer nos saves.

O quadro deixou três estados de fora e eles têm janela própria: instalação cancelada, erro (com o que o `PatchInstaller` devolveu ao jogo) e o jogo original de volta. Um instalador sem patch dentro mostra "Este instalador está vazio".

## Regras do jogo

A janela não grava nada no jogo. Tudo passa por `PatchInstaller` (via `IInstallerEngine`): cópia dos originais conferida por SHA-256 antes de qualquer troca, arquivo gravado num temporário e só então trocado, e devolução do que já tinha sido trocado se algo falha (regras 2 e 3 do CLAUDE.md).

## Linha de comando

| Opção | O que faz |
| --- | --- |
| `--pasta <pasta>` | Pasta do jogo. Na janela, já vem escolhida. |
| `--silencioso` | Modo de console: instala sem janela e sem perguntas e escreve o andamento. Com `--desinstalar`, volta ao jogo original. É o que a automação e os testes de ponta a ponta usam. |
| `--desinstalar` | Só vale com `--silencioso`; na janela, a pasta já instalada abre a janela 5. |
| `--ajuda`, `--licencas` | Escrevem no terminal e saem. |

Códigos de saída: 0 feito, 2 falhou, 3 versão errada do jogo, 4 cancelado. O modo de console se liga ao console de quem chamou o instalador (ele é WinExe) e nunca carrega o Avalonia.

## Tema e fontes

Os tokens, a tipografia, os botões e os ícones são os arquivos do `VNForge.App`, ligados no `.csproj` (sem cópia). O `App.axaml` redefine só as famílias de fonte, para embutir Noto Sans e Zen Kaku Gothic New Bold em vez dos 20 MB do pacote do app. O patch não leva a capa do jogo; a capa da coluna da esquerda é desenhada com o nome dele.

## Publicar e tamanho

```
dotnet publish src/VNForge.Installer -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
```

Medido em 09/11/2026: 52 952 134 bytes (50,5 MB) self-contained com compressão; 36 926 040 bytes (35,2 MB) framework-dependent, que exigiria o .NET 10 na máquina do jogador. `PublishTrimmed` falha (IL2026 no JSON por reflexão do Patching), então o instalador vai sem trimming. `VNF_INSTALLER_PUBLISHED` aponta os testes de ponta a ponta para o executável publicado.

## Testes

`tests/VNForge.Installer.Tests`: modo de console, ViewModel das janelas (estados, avanço, cancelamento, erro com restauração), textos e os renders das janelas nos dois temas (`VNFORGE_RENDER_DIR` escolhe a pasta dos PNG).
