# E10.3 · Painel de detalhe da linha (#100)

Painel da direita do Editor: original, tradução editável com marcadores como chips, aprovar e notas. Salva a cada edição; `Ctrl+Enter` aprova e avança, `Alt+↑/↓` troca de linha. Pronto quando uma edição feita no app aparece no `vnf status`.

## Core primeiro: `VNForge.Core.Projects.LineEditor`

Edição de uma linha de `text/<script>.json`, usada pelo app e disponível para a CLI.

- `LineEditor(ProjectFolder project, TimeProvider clock)`.
- `TextUnit SetTranslation(string scriptKey, string id, string? translation)`: tradução nula, vazia ou só espaços grava `translation: null`, status `untranslated` e origem nula. Texto novo grava status `translated` e origem `manual` com o horário do `clock`, mesmo que a linha estivesse `reviewed` ou `approved` (editar desfaz a revisão). Texto igual ao gravado não regrava o arquivo.
- `TextUnit SetNotes(string scriptKey, string id, string notes)`.
- `TextUnit Approve(string scriptKey, string id)`: exige tradução (`HasTranslation` depois de considerar o texto atual); sem ela, `InvalidOperationException` com mensagem em pt-BR. Grava status `approved` e mantém a origem.
- Cada operação lê o script, troca a unidade pelo `id` e grava com `ProjectFolder.SaveText` (atômico). `id` desconhecido lança `KeyNotFoundException` com mensagem em pt-BR.
- Testes xUnit em `tests/VNForge.Core.Tests`: cada regra acima, gravação idempotente e leitura de volta pelo `ProjectStatusReporter`.

## App

- `ViewModels/LineDetailViewModel.cs`: recebe a `TextUnit`, a chave do script, o `LineEditor`, um `ILineNavigator` (`SelectNext`, `SelectPrevious`) e um `TimeProvider`. Expõe `Speaker`, `Source`, `SourceSegments` (de `ControlMarkers.Split`), `Translation`, `Notes`, `Status`, `MissingMarkers` e os comandos `ApproveAndNext`, `Next`, `Previous`. Mudança em `Translation` ou `Notes` agenda a gravação com debounce de 400 ms (design `TranslationField`); trocar de linha ou aprovar grava antes o que estiver pendente. Erro de gravação vira `ErrorMessage` em pt-BR, sem derrubar o app.
- `Services/ILineNavigator.cs`: o contrato com a grade do #99. O `EditorViewModel` (#99) implementa ou adapta `SelectNext`/`SelectPrevious` e expõe `Detail`.
- `Views/LineDetailView.axaml(.cs)`: original somente leitura com chips; tradução em `AvaloniaEdit.TextEditor` (`WordWrap`, sem numeração); notas em `TextBox`; botão Aprovar; `KeyBinding` de `Ctrl+Enter`, `Alt+Up`, `Alt+Down` na `UserControl`. Tokens `Vnf.*` do `Themes/Tokens.axaml`.
- `Controls/MarkerChipGenerator.cs`: `VisualLineElementGenerator` que reconhece `\\[npcu]` e `\{[^}]+\}` e devolve `InlineObjectElement` com `Border Classes="marker"` (tracejado para controle, contínuo para variável); o cursor salta o chip e Backspace o apaga inteiro. `AutomationProperties.Name` em cada chip.
- Pacote `Avalonia.AvaloniaEdit` na versão compatível com Avalonia 12.1.x, fixado no `Directory.Packages.props`.
- Testes em `tests/VNForge.App.Tests`: o teste de aceite grava pelo ViewModel num projeto temporário e lê com `ProjectStatusReporter.Create(ProjectFolder.Open(root))`, o mesmo caminho do `vnf status`. Mais: debounce, aprovar e avançar, navegação, marcadores faltando, erro de gravação.

## Fora deste PR

- `EditorView.axaml` e `EditorViewModel.cs` são do #99. O encaixe (`ContentControl Content="{Binding Detail}"`) entra no rebase, se o #99 estiver na `main` antes do PR, ou num PR curto depois.
- Sugestão da IA, histórico, prévia da caixa e sublinhado do glossário ficam para as issues do M4.

## Documentos

- `docs/02-arquitetura.md`: regras de edição manual do `LineEditor`.
- `docs/design/avalonia.md`: versão do AvaloniaEdit fixada.
