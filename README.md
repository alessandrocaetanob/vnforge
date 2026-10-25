# VNForge

Tradutor de visual novels em C# (.NET 10), com CLI (`vnf`) e aplicativo Windows. Extrai o texto do jogo, traduz com ajuda do Claude, confere, reconstrói os arquivos e reinjeta no jogo, tratando espaçamento e acentuação do português.

Primeiro alvo: uma VN na engine **YU-RIS** (arquivos `.ypf` e scripts `.ybn`).

> Status: planejamento. Ainda não há código; o próximo passo é o épico E0 (fundação) e depois o marco M1 (extrair e reinjetar sem tradução, com o jogo abrindo igual ao original).

## Documentação

| Documento | Conteúdo |
|---|---|
| [docs/README.md](docs/README.md) | Visão geral, princípios e decisões tomadas |
| [docs/01-nome.md](docs/01-nome.md) | Nome do projeto |
| [docs/02-arquitetura.md](docs/02-arquitetura.md) | Arquitetura, formato do projeto de tradução, pipeline, IA |
| [docs/03-cli.md](docs/03-cli.md) | Comandos da CLI |
| [docs/04-app-windows.md](docs/04-app-windows.md) | Aplicativo Windows |
| [docs/05-epicos.md](docs/05-epicos.md) | Épicos e marcos |
| [docs/06-yuris-notas-tecnicas.md](docs/06-yuris-notas-tecnicas.md) | Formatos do YU-RIS levantados até agora |

Versões publicadas (privadas, só abrem para quem tiver acesso):

- Planejamento: https://claude.ai/artifact/UJGHP596LPZX7PQmXPgPyy
- Protótipo visual do app: https://claude.ai/artifact/36wMrNUfoMvXmhRNdJQhrn

## Baixar

Os arquivos ficam na página de [Releases](https://github.com/alessandrocaetanob/vnforge/releases) do GitHub. Cada release traz:

| Arquivo | Para quê |
|---|---|
| `vnforge-app-<versão>-win-x64.zip` | O aplicativo (janela). Windows 64 bits. Descompacte e abra `VNForge.App.exe`; não precisa instalar o .NET. |
| `vnf-<versão>-win-x64.zip` | A linha de comando `vnf`, para Windows 64 bits. |
| `vnf-<versão>-linux-x64.tar.gz` | A linha de comando `vnf`, para Linux 64 bits. |
| `SHA256SUMS.txt` | Somas SHA-256 dos pacotes, para conferir o download. |

Para usar o app, baixe só o `vnforge-app-...zip`. O aplicativo só sai para Windows; no Linux use o `vnf`. Na primeira abertura o app extrai suas bibliotecas nativas para uma pasta temporária, então a abertura inicial leva alguns segundos a mais.

## Regra de ouro

**Nenhum arquivo do jogo entra neste repositório**: nem `.ypf`, `.ybn`, `.exe`, nem texto extraído de um jogo comercial. O `.gitignore` bloqueia os formatos conhecidos; testes usam arquivos sintéticos gerados no próprio teste.
