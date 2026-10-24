# CLAUDE.md — instruções para agentes no VNForge

## Antes de começar

Leia `docs/README.md` e o documento do épico em que vai trabalhar (`docs/05-epicos.md`). A arquitetura está em `docs/02-arquitetura.md`; os formatos do YU-RIS em `docs/06-yuris-notas-tecnicas.md`. Se o código divergir dos documentos, pergunte ou atualize o documento no mesmo PR, explicando o motivo.

## Projeto

- C# 14 / **.NET 10**. CLI com `System.CommandLine` 2.x; app em Avalonia 12 (`net10.0`, tema `FluentTheme`) com CommunityToolkit.Mvvm; o app compila e testa em qualquer sistema.
- Solução: `src/VNForge.Core`, `src/VNForge.Engines.YuRis`, `src/VNForge.AI`, `src/VNForge.Cli`, `src/VNForge.App`, testes em `tests/`.
- `VNForge.Core` não depende de nenhuma engine nem de provedor de IA.
- CLI e app usam o mesmo Core. Toda funcionalidade nova entra no Core primeiro.

## Regras inegociáveis

1. **Nunca versionar arquivos de jogo** (`.ypf`, `.ybn`, `.exe`, texto extraído de jogo comercial). Testes geram arquivos sintéticos em código.
2. **Nunca alterar a pasta do jogo sem backup.** Toda escrita no jogo passa pelo fluxo de injeção com backup e restauração.
3. **Reescrita mínima de binários**: entradas de arquivo e argumentos de script que não mudaram são copiados byte a byte. Ida e volta sem alterações tem que gerar arquivo idêntico; isso é teste obrigatório.
4. **Chaves de API nunca em arquivo do projeto**: só variável de ambiente (`ANTHROPIC_API_KEY`) ou cofre do usuário (DPAPI).

## IA

- Primeira versão: **só Claude**, pelo SDK oficial `Anthropic` para C#. Modelo selecionável: `claude-opus-5-5` (padrão), `claude-sonnet-5-5`, `claude-haiku-5-5`, `claude-fable-5-1`. Use os ids exatamente assim.
- Antes de escrever código que chama a API, use a skill `claude-api` para conferir o formato atual (thinking adaptativo, `output_config.effort`, `output_config.format` para JSON, tratamento de `stop_reason: "refusal"`, prompt caching).
- Toda chamada à IA precisa poder ser trocada por um provedor falso nos testes.

## Código

- Nullable habilitado, warnings como erro.
- Nomes de tipos e membros em inglês; textos para o usuário (CLI, app, mensagens de erro) em português do Brasil.
- Codificação de texto do jogo: registrar `CodePagesEncodingProvider` uma vez; Shift-JIS é a página 932.
- Testes com xUnit. Cada formato binário tem teste de ida e volta.

## Documentação

- Documentos em `docs/`, em português do Brasil, escritos de forma direta.
- Ao concluir um épico ou mudar uma decisão, atualize `docs/` no mesmo PR.

## Git

- Trabalhe em branch (`feat/...`, `fix/...`, `docs/...`) e abra PR para `main`.
- Mensagens de commit em português, no imperativo ("Adiciona leitor de YPF").
