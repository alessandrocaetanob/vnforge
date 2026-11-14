# Auditoria por risco do VNForge.App.Tests (T4, #701)

Auditoria de 14/11/2026 (tester, `modo=auditar`). Nenhum teste foi apagado nem editado: este arquivo é a lista de corte. O builder corta só lendo daqui. Regras: `docs/04-testes.md` (riscos R1 a R8) e `docs/plans/2026-11-14-plano-de-testes.md`.

## 1. Resultado

Medição de partida: `pwsh tools/medir-testes.ps1` sobre o Release da `main` em 66f50c35, num processo só, sem cobertura.

| | Antes | Depois |
|---|---:|---:|
| Classes de teste | 165 (mais 1 vazia, `AppSettingsFileTests`) | 65 |
| Métodos (`[Fact]` e `[Theory]`) | 1.131 | 136 |
| Execuções (cada linha de Theory conta) | 1.348 | 202 |
| Tempo de parede (suíte inteira) | 91,0 s (soma dos testes 85,7 s) | uns 45 s (estimativa: 41 s de soma dos tempos individuais mais a partida do processo) |
| Pico de memória | 712 MB | não medido (cai com o corte das janelas; o smoke segura a maior parte) |

O depois de tempo é soma dos tempos individuais do JUnit da medição completa, não uma rodada da suíte cortada (não dá para rodar a suíte cortada sem apagar os testes). O builder confirma com `pwsh tools/medir-testes.ps1`.

### Composição das 202 execuções, pelo risco principal de cada uma

| Risco | Execuções | O que é |
|---|---:|---|
| R8 | 41 | `ScreenSmokeTests`: um por tela (40 telas avulsas e a passada pelas páginas do projeto) |
| R5 | 43 | chave só por ambiente ou cofre, recusa de gasto antes de chamar a IA (uma por caminho que gasta), parser de valor em reais |
| R2 | 38 | gravação do projeto: falha sem perder edição, arquivo ilegível não sobrescrito, o que não é da tela não se perde |
| R1 | 24 | jogo: injetar, restaurar, aplicar patch, pasta de saída, recusa quando o jogo mudou |
| R6 | 21 | jornadas e erro ao abrir projeto |
| R3 e R4 | 20 | oráculos: ida e volta do campo de tradução, composição de imagem, marcadores, cp1252, falante |
| R7 | 7 | arquivo externo ilegível ou rede fora: recusa com aviso, nada some |
| Novos da #707 e #708 | 8 | `ControlLogicTests` (os três de `LibraryLinkProjectTests`, o do Shell e os do Core entram em R6 e na seção 5) |

### Por que passa de 120

O alvo do documento é "perto de 120, com uns 50 de smoke, uns 20 de jornada e o resto lógica de risco". Fecha em 136 métodos e em 202 execuções. Passa de 120 em execuções por quatro motivos, todos de "fica sempre":

1. A seção "O que fica sempre" do `docs/04-testes.md` manda manter proteção de dado (R1, R2, R7) e chave e gasto (R5) mesmo acima do orçamento. Isso são 112 execuções (R5 43, R2 38, R1 24, R7 7). O App tem ao menos 11 caminhos que gastam com a IA e ~15 que gravam arquivo do projeto, e cada um ficou com um teste, não com vários.
2. Oráculos com valor esperado explícito (regra 8): `MoneyFormattingTests` (10 linhas), `TranslationFieldTextTests` (8), `ImageComposerHeadlessTests` (6) e `ControlLogicTests` (8). São 32 execuções de 9 métodos. Theory com linhas de valor esperado conta uma execução por linha, e o orçamento do documento conta execuções.
3. O smoke fica com 41 em vez de 50, porque o catálogo tem 40 telas avulsas e uma passada pelas páginas do projeto.
4. Os testes da #707 e #708 ficam por ordem do brief.

Se o Alessandro quiser 120 execuções redondas, o único corte que sobra sem tocar nos itens acima são as linhas de Theory dos oráculos e as jornadas duplicadas (`StartV3Tests`, `MainWindowViewModelTests.Falha_ao_abrir_o_projeto_criado...`). Não proponho: a regra 8 pede que oráculo fique.

## 2. Como o builder corta

1. Classe da seção 3: apagar a classe inteira. Se o arquivo ficar vazio, apagar o arquivo.
2. Classe da seção 4: apagar todo método que não estiver na lista dela. Em Theory mantida com linhas escolhidas, apagar os `[InlineData]` que não estão na lista (as linhas listadas ficam como estão).
3. Nada em `src/` muda. Teste novo nenhum.
4. Código de apoio que um teste mantido usa fica. Os que não podem sair com a classe cortada:
   - `HeadlessEntry`, `HeadlessFixture` e `HeadlessSessionFixture` moram em `EditorGridHeadlessTests.cs`; o `ScreenSmokeTests` e o `ImageComposerHeadlessTests` dependem deles. Mover para um arquivo próprio (`HeadlessFixture.cs`) e apagar só a classe `EditorGridHeadlessTests`.
   - `ClippedTextHeadlessTests.FindClipped` (`internal static`) é chamado pelo `ScreenSmokeTests`. Mover o método para `Screens/` e apagar a classe `ClippedTextHeadlessTests`.
   - `PolishViewModelTests.cs` guarda `ToastsFromPagesTests` (fica) e `SwitchableFolderPicker`; saem `ToastServiceTests`, `ThemeSettingsTests` e `EmptyStatesViewModelTests`.
   - `SettingsViewModelTests.cs` guarda também `MemoryApiKeyStoreTests` e `AppSettingsFileTests` (vazia): saem as duas.
   - `BudgetSettingsTests.cs` guarda três classes e as três ficam parcialmente (seção 4).
   - `StartViewModelTests.cs` guarda `StartViewModelTests` (sai) e `StartViewModelFalhasTests` (fica).
   - Ficam inteiros: `Support.cs`, `Wait.cs`, `NoParallelization.cs`, `ResilientHeadlessSession.cs`, `SyntheticDictionary.cs`, `*Samples.cs`, `*Fixtures.cs`, `Demo/DemoBuild.cs`, `Screens/ScreenCatalog.cs`, `Screens/ScreenHost.cs`. Apagar só o que o compilador ou o analisador acusar como sem uso.
5. CI: depois do corte só o `ScreenSmokeTests` leva o trait `Tipo=Tela`. O `.circleci/run-tests.sh` continua válido (o filtro `-trait- Tipo=Tela` roda as outras uns 8 s). Não mudar o script.
6. Prova: `dotnet build tests/VNForge.App.Tests -c Release` e `pwsh tools/medir-testes.ps1 -Dll tests/VNForge.App.Tests/bin/Release/net10.0/VNForge.App.Tests.dll -Name VNForge.App.Tests`. Esperado: 202 testes, 0 falhas, perto de 45 s.

## 3. CORTAR: classes inteiras (100, e a vazia)

Entre parênteses, as execuções que a classe tinha. Total cortado nesta seção: 500 execuções.

### 3.1 Janela headless: layout, teclado, foco, rolagem (23 classes, 63 execuções)

Motivo: apresentação e comportamento de janela. O `ScreenSmokeTests` abre cada tela sem exceção e sem texto cortado (R8); a lógica de cada tela fica nos testes de ViewModel. O ganho de tempo é de uns 21 s.

`AddressTermsViewHeadlessTests` (1), `BuildViewHeadlessTests` (2), `ClippedTextHeadlessTests` (4; duplicada do `ScreenSmokeTests.As_paginas_do_projeto_abrem_sem_excecao_e_sem_texto_cortado`, 3,8 s), `ConferenceViewHeadlessTests` (3), `ControlStatesHeadlessTests` (4), `EditorChaptersHeadlessTests` (1), `EditorCompactTests` (2), `EditorGridHeadlessTests` (3), `EditorV2HeadlessTests` (2), `GameTextboxHeadlessTests` (1; 2,6 s), `ImagesViewHeadlessTests` (1), `MainWindowFrameHeadlessTests` (1), `MotionHeadlessTests` (2; 2,5 s), `ReadSceneEditorHostTests` (4), `ReleaseViewHeadlessTests` (4), `ReviewModeKeyTests` (9; 5,2 s), `ScenesCollapseTests` (1), `SettingsViewHeadlessTests` (3), `SpeakerCopyHeadlessTests` (4), `SpellingMenuHeadlessTests` (2), `StartViewHeadlessTests` (6), `TaskBarLayoutHeadlessTests` (1), `TextAppearanceHeadlessTests` (2).

### 3.2 Apresentação: texto, rótulo, cor, ordem, formatação, estado de botão, navegação entre telas (54 classes, 298 execuções)

Motivo: confere texto exato, visibilidade, cor, ordem de chip, cabeçalho, dica, ou o destino de um link. A tela se confere pelo render ao lado do quadro (R8 não é teste), e a navegação pelo smoke.

`BackgroundTaskTests` (3), `ChangedSinceTestTests` (8), `CharacterColorTests` (10; contraste de cor), `CharacterSheetTests` (11), `CommandPaletteTests` (8), `ConferenceSettingTests` (12), `ConferenceSpeakerTests` (1), `DashboardNavigationTests` (10), `DashboardViewModelDataTests` (12), `DashboardViewModelTests` (2), `DictionaryGlossaryTests` (1), `EditorGoToLineTests` (2), `EditorGridUpdateTests` (1), `EditorRecheckTests` (5), `EditorScriptDensityTests` (1), `EditorScriptsTests` (7), `EditorSuggestionReviewTextTests` (3), `EmptyScenesTests` (7), `EmptyStatesViewModelTests` (1), `FitWarningViewModelTests` (6), `GlossaryPageTests` (7), `LineDetailInspectorTests` (14), `LineDetailSpellingTests` (3), `LineDoubtTests` (4), `LineExplainTests` (5), `LineSuggestionTests` (5), `MainWindowBuildNavTests` (2), `MainWindowLineLinksTests` (1), `MainWindowRailTests` (2), `MainWindowRecheckTests` (1), `MarkerScannerTests` (7; só alimenta os chips da tela), `NewProjectWizardStepsTests` (3), `ProjectLabelsTests` (24; rótulos em português), `RelativeTimeTests` (3), `ReleasePublisherVersionTests` (1), `ReviewModeNavigationTests` (6), `ReviewModeSpeakerTests` (5), `ReviewStageTests` (5; conta de encaixe do palco), `SpeakerPlateTests` (2), `StartContinueCardTests` (3), `StartDiaryTests` (3), `StartScreenTests` (14), `SuggestionTextTests` (1), `TaskEntriesTests` (16), `TechnicalDetailsMemoryTests` (4), `TextAppearanceSettingsTests` (7), `TextboxPreviewTests` (4), `ThemeSettingsTests` (1), `ToastServiceTests` (1), `TokensTests` (1), `TypographyTokensTests` (3), `UndoBatchDialogViewModelTests` (9; texto do botão e da mensagem), `ViewModelStatesTests` (17), `WordDiffTests` (3).

### 3.3 Duplicado de um teste mantido, ou do mesmo padrão já provado (13 classes, 69 execuções)

Cada uma repete o padrão que outro teste mantido já prova (a edição pendente é gravada antes, falha de gravação mantém a edição, o Desfazer devolve as linhas). O teste mantido que cobre fica ao lado.

`BatchHistoryTests` (8; `MainWindowTranslateTests.A_traducao_da_IA_deixa_um_ponto_de_restauracao...`), `ConferenceConsistencyTests` (6; `EditorSceneTests.Usar_em_todos_grava...`), `EditorChaptersTests` (10; `DashboardChapterRenameTests.Renomear_de_verdade...`), `EditorConsistencyTests` (6; `EditorSceneTests.Usar_em_todos_grava...`), `EditorSearchPanelTests` (5; `SearchReplaceViewModelTests`), `LibraryPlayTests` (4; `LibraryTagsViewModelTests.Falha_de_gravacao...`), `LibraryQueueTests` (5; idem), `MainWindowChaptersTests` (4), `MainWindowEditorPassTests` (5; `EditorPassJobTests`), `MainWindowStartFlowTests` (1; `MainWindowViewModelTests.Projeto_criado...`), `ReadSceneTests` (8; `EditorDetailTests.Gravacao_que_falha...`), `StartViewModelTests` (5; `StartViewModelFalhasTests`), `StyleSettingsTests` (2; `SettingsViewModelTests.Cada_mudanca_e_gravada...`).

### 3.4 Sem risco da tabela (8 classes, 63 execuções)

`AdultPrivacyTests` (4) e `VndbImagePrivacyTests` (10): privacidade do retrato adulto, que não é R1 a R8 (ver seção 7). `CorrectionOfferTests` (6), `GameCapturesViewModelTests` (7; capturas e notas), `LineDictionaryTests` (24; dicionário de japonês), `LineMemoryTests` (7), `PreDictionarySuggesterTests` (2), `SettingsGameTitleReadTests` (3).

### 3.5 Getter, estado ou dublê (2 classes, 7 execuções)

`EditorViewModelTests` (6; expõe os textos da grade, rótulo do QA), `MemoryApiKeyStoreTests` (1; testa o dublê). `AppSettingsFileTests` (0): classe vazia, apagar.

## 4. MANTER: classes e métodos (65 classes, 202 execuções)

Em cada classe: antes, depois, e os métodos que ficam. Todo método fora da lista sai. `x N` é a quantidade de linhas de Theory.

### ScreenSmokeTests (41 para 41) · `Screens/ScreenSmokeTests.cs`
- Toda a classe: `A_tela_abre_sem_excecao_e_sem_texto_cortado` (40 telas) e `As_paginas_do_projeto_abrem_sem_excecao_e_sem_texto_cortado`. R8. 33 s: a única classe cara que fica.

### R5: chave, gasto, valor em reais (43)
- `ApiKeyStatusTests` (6 para 3): `Describe_diz_a_origem_sem_mostrar_a_chave` x 3.
- `BatchRequestTests` (21 para 3): `Limite_de_gasto_atingido_recusa_o_pedido`, `Sem_chave_o_pedido_e_recusado_antes_de_qualquer_gasto`, `O_gasto_da_IA_fica_registrado_no_projeto`.
- `BibleViewModelTests` (28 para 2): `Gerar_recusa_a_estimativa_que_nao_cabe_no_limite_e_nao_chama_a_IA`, `Reescrever_recusa_o_pedido_que_nao_cabe_no_limite_e_nao_chama_a_IA`.
- `BudgetAppSettingsFileTests` (5 para 3; em `BudgetSettingsTests.cs`): `Cotacao_limite_aviso_e_qualidade_vao_e_voltam_do_app_json` (R5), `Gravar_por_cima_de_um_app_json_ilegivel_guarda_uma_copia` e `Gravar_troca_o_arquivo_por_inteiro_e_nao_deixa_temporario` (R2).
- `BudgetSettingsViewModelTests` (12 para 3; em `BudgetSettingsTests.cs`): `Limite_e_aviso_gravam_no_app_json_e_vazio_tira_o_limite`, `Limite_invalido_mostra_o_erro_e_guarda_o_valor_anterior` (ficam as linhas `"0"` e `"muito"`).
- `MoneyFormattingTests` (23 para 10; em `BudgetSettingsTests.cs`): `Le_o_valor_em_reais_como_a_pessoa_digita` (ficam `"5.40"`, `"R$ 5,4"`, `"1.250,00"`, `"1.250"`, `"  0,5 "`) e `Recusa_o_que_nao_e_valor` (ficam `"cinco"`, `"-5"`, `"5,4,3"`, `"US$ 5"`, `"1.2.3"`). Sai `Tempo_aparece_sem_falsa_precisao` (apresentação).
- `EditorPassJobTests` (7 para 2): `Outra_acima_do_limite_de_gasto_recusa_com_mensagem_e_nao_chama_a_IA`, `Para_quando_o_gasto_do_projeto_chega_ao_limite`.
- `EvalViewModelTests` (21 para 1): `Sem_chave_ou_acima_do_limite_o_botao_de_rodar_fica_desligado`.
- `GlossarySuggestionsTests` (27 para 2): `Pedir_de_novo_nao_manda_quem_ja_tem_sugestao` (R5), `Falha_ao_gravar_mantem_as_edicoes_e_diz_o_motivo_sem_caminho` (R2).
- `ImagesViewModelTests` (35 para 4): `ReadingTheImagesAsksBeforeCallingTheAiThenWritesTheInventoryWithTheSuggestion`, `TheReadingStopsBeforeCallingTheAiWhenTheEstimateBreaksTheSpendLimit` (R5); `AFailedMoveRollsTheEditsBackAndKeepsTheError`, `MarkingSavesTheFinishedImageAndTheEdit` (R2).
- `LineSuggesterTests` (7 para 2): `Chave_em_branco_vale_como_sem_chave`, `Ai_json_invalido_cai_para_o_modelo_padrao_em_vez_de_falhar`.
- `MainWindowSettingsTests` (1 para 1): `Guardar_a_chave_atualiza_a_barra_de_status_sem_mostrar_a_chave`.
- `MainWindowTranslateTests` (18 para 4): `Pedido_acima_do_limite_nao_deixa_traduzir_e_nada_e_gravado`, `Segundo_pedido_durante_uma_traducao_em_andamento_e_recusado` (R5); `A_traducao_da_IA_deixa_um_ponto_de_restauracao_e_o_Desfazer_da_lista_devolve_as_linhas`, `Uma_unica_cena_escolhida_que_nao_e_a_aberta_e_a_que_a_janela_traduz` (R6, jornada 6).
- `NewProjectWizardV3Tests` (17 para 2): `Criar_grava_cotacao_limite_e_qualidade_no_app_json_pelo_mesmo_servico_dos_Ajustes` (R5), `Se_o_app_json_nao_grava_o_projeto_fica_criado_e_a_tela_avisa` (R2).
- `SampleSceneTests` (6 para 3): `Traduzir_sem_chave_usa_a_IA_falsa_que_devolve_o_portugues_do_exemplo_sem_gasto`, `A_sugestao_do_exemplo_vem_da_IA_de_demonstracao_mesmo_com_a_chave_configurada` (R5); `Experimentar_cria_o_projeto_na_pasta_de_exemplos_abre_e_leva_ao_Painel` (R6, jornada 1).
- `SettingsViewModelTests` (24 para 6): `Salvar_chave_guarda_no_cofre_limpa_o_campo_e_nao_deixa_rastro_no_projeto`, `Chave_pelo_cofre_dpapi_real_tambem_nao_aparece_no_projeto` (só Windows), `Chave_da_variavel_de_ambiente_vence_o_cofre_e_o_status_diz_isso` (R5); `Cada_mudanca_e_gravada_na_hora_so_no_projeto_e_preserva_o_resto_do_vnforge_json`, `Falha_ao_gravar_vira_erro_em_portugues_e_nao_derruba_a_tela`, `Salvar_grava_o_formatting_json_e_mantem_as_regras_que_a_tela_nao_edita` (R2).
- `TranslateDialogViewModelTests` (14 para 3): `Pedido_acima_do_limite_desliga_o_botao_e_oferece_a_qualidade_que_cabe`, `O_limite_de_gasto_vale_sobre_a_metade_do_preco_no_lote` (R5); `Iniciar_na_noite_pede_o_lote_e_a_cena_de_exemplo_nao_tem_a_escolha` (R6, jornada 10).
- `VndbListTests` (18 para 3): `O_token_vai_so_para_o_cofre_e_um_token_recusado_nao_e_guardado` (R5), `Campo_mudado_dos_dois_lados_abre_o_dialogo_e_so_grava_depois_de_confirmar` (R2), `Sem_rede_terminar_aqui_fica_so_aqui_e_nao_derruba` (R7).

### R1: pasta do jogo (24)
- `BuildPatchViewModelTests` (11 para 4): `Aplicar_pede_confirmacao_e_so_grava_depois_de_confirmar_com_backup_do_original`, `Exe_que_mudou_depois_da_confirmacao_nao_e_gravado`, `Patch_ja_aplicado_mostra_o_estado_e_nao_cria_backup`, `Restaurar_originais_devolve_o_exe_byte_a_byte_e_a_lista_mostra_o_executavel`.
- `BuildTestViewModelTests` (18 para 4; 9,6 s para uns 2,3 s): `Testar_no_jogo_confere_injeta_com_backup_abre_o_jogo_e_guarda_os_cinco_passos`, `Voltar_ao_jogo_original_devolve_os_arquivos_byte_a_byte_sem_pedir_confirmacao`, `Jogo_sem_o_executavel_nao_recebe_nada_e_a_mensagem_nao_mostra_o_caminho`, `Erro_de_Conferencia_no_meio_recusa_em_linguagem_comum_e_nao_grava`.
- `BuildViewModelTests` (32 para 5): `Injetar_pede_confirmacao_e_so_grava_depois_de_confirmar`, `Build_que_mudou_depois_do_plano_confirmado_nao_e_gravado_no_jogo`, `Restaurar_pede_confirmacao_e_devolve_o_original`, `Restaurar_remove_o_arquivo_que_a_injecao_criou`, `Backup_mais_novo_que_o_confirmado_nao_e_ignorado_na_restauracao`.
- `LibraryVersionTests` (5 para 3): `Injecao_recusada_porque_o_jogo_mudou_depois_da_extracao_diz_quantos_arquivos_diferem`, `Jogo_que_mudou_depois_da_injecao_recusa_a_volta_e_o_dialogo_explica`, `Troca_para_o_original_e_de_volta_pelo_backup_e_pela_injecao`.
- `LibraryExportTests` (8 para 2): `Dentro_da_pasta_de_um_jogo_o_dialogo_recusa_e_nao_grava_nada` (R1), `Um_index_html_que_nao_e_catalogo_nao_e_trocado_mas_o_catalogo_antigo_e` (R7).
- `ReleaseViewModelTests` (35 para 5): `Pasta_de_saida_nao_pode_ser_a_do_jogo_nem_ficar_dentro_dela` x 4 (a linha `jogo2` é a armadilha do prefixo: fica), `Jogo_fora_da_versao_de_referencia_ao_gerar_trava_a_pagina_com_o_motivo_do_core`.
- `ToastsFromPagesTests` (2 para 2; em `PolishViewModelTests.cs`): `Desfazer_a_injecao_sem_poder_restaurar_explica_em_vez_de_so_navegar` (R1), `Desfazer_recusado_porque_a_linha_mudou_mostra_aviso_de_erro_e_nao_mexe_no_arquivo` (R2).
- `LibraryViewModelTests` (26 para 3): `Desfazer_o_casamento_e_tirar_da_biblioteca_mexem_so_no_library_json` (R1), `Arquivo_ilegivel_vira_aviso_e_nao_lista_vazia_muda`, `Varrer_de_novo_lista_todas_as_pastas_sem_acesso_no_fim_mesmo_com_pasta_boa_no_meio` (R7, #652).

### R2: gravação do projeto (38 no total; as de cima já contam)
- `AddressTermsViewModelTests` (20 para 2): `Falha_ao_gravar_os_tratamentos_avisa_que_ainda_citam_o_nome_antigo_e_a_nova_tentativa_nao_troca_de_novo` (R2, renomear em dois arquivos), `Arquivo_ilegivel_trava_a_edicao_e_nao_e_sobrescrito` (R7).
- `ApprovalLogTests` (5 para 1): `Arquivo_de_versao_mais_nova_nunca_e_sobrescrito`.
- `ConferenceViewModelTests` (35 para 2): `Falha_no_segundo_script_mantem_o_primeiro_corrigido_avisa_e_reconfere` (R2), `Corrigir_tudo_troca_a_pontuacao_japonesa_e_deixa_o_que_nao_tem_substituto` (R4).
- `DashboardChapterRenameTests` (7 para 1): `Renomear_de_verdade_grava_o_scripts_json_do_projeto`.
- `EditorDetailTests` (7 para 2): `Gravacao_que_falha_mantem_a_selecao_e_o_painel_com_a_mensagem`, `Trocar_de_projeto_grava_e_descarta_o_painel`.
- `EditorSceneTests` (15 para 2): `Usar_em_todos_grava_a_traducao_nas_linhas_do_mesmo_original_e_em_nenhuma_outra`, `Trocar_de_projeto_no_meio_do_lote_cancela_o_lote`.
- `EditorSuggestionTests` (11 para 1): `Aceitar_grava_a_proposta_como_traducao_da_pessoa_e_tira_a_sugestao_da_grade_e_do_arquivo`.
- `GameSheetViewModelTests` (29 para 1): `Navegar_ou_reler_com_texto_que_nao_pode_ser_gravado_nao_perde_o_rascunho_e_o_que_pode_e_gravado_antes`.
- `GlossaryViewModelTests` (9 para 2): `Arquivo_invalido_vira_aviso_e_bloqueia_o_salvamento` (R7), `Salvar_o_glossario_mantem_romaji_apelidos_e_descricao_que_vieram_do_vndb`.
- `LibraryTagsViewModelTests` (8 para 1): `Falha_de_gravacao_das_tags_vira_aviso_e_o_filtro_continua_coerente`.
- `LineDetailViewModelTests` (14 para 2): `Aprovar_com_erro_ao_gravar_nao_avanca`, `Descartar_o_painel_grava_o_que_estava_pendente`.
- `LineRewriteTests` (19 para 1): `Editar_a_traducao_depois_das_versoes_impede_que_Usar_apague_a_edicao`.
- `MainWindowConferenceTests` (4 para 1): `Gravacao_pendente_que_falha_impede_de_entrar_na_Conferencia`.
- `MainWindowNightBatchTests` (11 para 4): `Cancelar_lote_pede_o_cancelamento_a_API_e_o_que_ja_voltou_e_gravado_quando_ela_encerra`, `Lote_que_chega_com_outro_projeto_aberto_grava_no_dele_e_nao_mexe_no_projeto_atual` (R2); `Sem_cliente_da_API_o_lote_avisa_que_falta_a_chave_e_nao_cria_tarefa` (R5); `Ao_abrir_o_projeto_o_app_retoma_o_lote_que_ficou_na_pasta` (R6, jornada 10).
- `NewProjectWizardViewModelTests` (16 para 2): `Pasta_que_ja_tem_projeto_nao_e_sobrescrita` (R2), `Criar_grava_o_vnforge_json_com_as_escolhas_e_vai_para_o_passo_final` (R6, jornada 3).
- `PersonalGlossaryTabTests` (12 para 1): `Termo_que_difere_do_projeto_nao_e_trocado_pelo_Usar`.
- `ReviewModeTests` (10 para 2): `Se_a_gravacao_falha_a_revisao_fica_na_linha_e_o_painel_mostra_o_erro` (R2), `Esta_boa_registra_a_aprovacao_com_a_hora_do_relogio_em_sessions_json` (R6, jornada 7).
- `SearchReplaceViewModelTests` (12 para 2): `Aplicar_so_vale_depois_da_previa_e_grava_o_que_ela_mostrou`, `Aplicar_que_falha_no_segundo_script_avisa_e_manda_o_Editor_reler`.
- `SettingsDictionariesTests` (15 para 1): `Ligar_e_desligar_grava_no_formatting_json_sem_apagar_a_regra`.
- `TranslationJobTests` (3 para 2): `Edicao_feita_no_Editor_no_meio_do_lote_sobrevive_e_a_IA_nao_sobrescreve_a_linha_traduzida_a_mao`, `Linha_aprovada_no_meio_da_execucao_nao_e_desfeita_pela_gravacao_do_lote`.

### R3 e R4: oráculos de texto e arquivo (20)
- `TranslationFieldTextTests` (16 para 8): `Editar_o_campo_grava_quebra_de_linha_e_pagina_e_poe_o_fim_de_volta`, `Campo_vazio_grava_vazio_mesmo_com_marcador_no_fim`, `Ida_e_volta_sem_edicao_devolve_o_mesmo_texto` (ficam as 6 linhas `"Oi.\\p"`, `"Primeira linha.\\nSegunda linha.\\p"`, `"Primeira página.\\pSegunda página.\\p"`, `"Olá, {nome}.\\p"`, `"Barra \\\\ e chave \\{\\p"`, `"Marcador solto \\"`).
- `ImageComposerHeadlessTests` (6 para 6): `AnImageThatDeclaresATooBigSizeIsRefusedBeforeDecoding` x 3, `SemiTransparentPixelsOutsideTheAreaComeBackWithTheSameAlphaAndColour`, `SomethingThatIsNotAnImageIsRefused`, `TheTextIsWrittenInsideTheAreaAndNothingIsDrawnOutsideIt`.
- `ConferenceFitTests` (6 para 1): `Corrigir_e_ir_para_o_proximo_divide_a_fala_e_mantem_os_marcadores_do_original_mais_a_virada_a_mais`.
- `LineShortenerTests` (2 para 1): `Versao_que_perde_o_marcador_sai_da_lista_e_sem_nenhuma_aproveitavel_vira_mensagem`.
- `LineSpeakerTests` (3 para 1): `Narracao_tira_o_nome_da_linha_e_o_falante_do_jogo_volta_a_ele`.
- `GlossaryGameCharactersTests` (3 para 1): `Salvar_a_traducao_do_personagem_grava_a_mesma_nas_unidades_name_do_mesmo_original`.
- `SettingsEncodingConflictTests` (1 para 1): `Trocar_para_cp1252_com_o_ajuste_em_conflito_mostra_a_faixa_e_Usar_tres_pontos_resolve`.

### R6: jornadas e abertura de projeto (21; os já listados acima contam)
- `LibraryCompositionTests` (1 para 1): `A_biblioteca_do_app_le_o_library_json_da_pasta_de_configuracao_e_varre_uma_pasta` (jornada 2).
- `LibraryLinkProjectTests` (3 para 3): os três (jornada 4; #707).
- `MainWindowShellTests` (15 para 4): `Abrir_o_jogo_da_biblioteca_sem_projeto_leva_a_Ficha_e_traduz_a_ficha_sem_projeto` (jornada 5; #708), `Abrir_um_projeto_que_ja_existe_da_biblioteca_vazia_leva_a_Ficha`, `Abrir_um_projeto_que_ja_existe_com_a_pasta_ilegivel_vira_aviso_na_Biblioteca_e_nao_excecao_solta`, `O_mesmo_erro_ao_abrir_o_projeto_do_jogo_aparece_de_novo_na_segunda_vez` (#619 e #688).
- `MainWindowViewModelTests` (5 para 2): `Projeto_criado_pelo_assistente_abre_no_app_e_entra_nos_recentes` (jornada 3), `Falha_ao_abrir_o_projeto_criado_vira_mensagem_na_tela_e_nao_derruba_o_processo`.
- `NewProjectWizardVndbTests` (13 para 1): `VNDB_fora_do_ar_avisa_em_portugues_e_deixa_seguir_sem_ele` (R7).
- `PaletteRiskCountTests` (1 para 1): `Com_a_leitura_do_texto_falha_e_o_Editor_sem_dados_a_paleta_abre_sem_estourar_a_pilha` (estouro de pilha derruba o processo).
- `ReviewModeRiskTests` (10 para 1): `Esta_boa_atravessa_as_cenas_grava_cada_linha_e_a_tela_final_nao_oferece_a_proxima_cena` (jornada 7).
- `StartV3Tests` (9 para 1): `A_pasta_solta_abre_o_assistente_com_a_pasta_preenchida` (jornada 3).
- `StartViewModelFalhasTests` (4 para 1; em `StartViewModelTests.cs`): `Falha_ao_gravar_os_recentes_nao_impede_abrir_o_projeto`.
- `CharactersViewModelTests` (12 para 1): `Projeto_com_a_pasta_ausente_avisa_e_nao_impede_o_andamento_e_o_cartao_dos_outros` (R7, o mesmo risco da #652).

### Novos da #708
- `ControlLogicTests` (8 para 8; `Controls/ControlLogicTests.cs`): `A_etapa_atual_e_a_primeira_nao_concluida`, `O_retrato_nunca_passa_da_origem_vezes_a_escala` x 7 (oráculo com valor esperado explícito).

## 5. Fica sempre, por nome (item 2 do brief)

Tudo que está nas seções 4 de R5, R1, R2 e R7 fica sempre. Os grupos que o brief pediu por nome:

**Os que acabaram de entrar (#688, #690 a #692, #697):**
- `LibraryLinkProjectTests.Depois_de_ligar_o_projeto_abrir_o_jogo_abre_o_projeto_e_nao_o_assistente`
- `LibraryLinkProjectTests.Ligar_pelo_app_um_jogo_que_mudou_de_pasta_grava_o_mesmo_library_json_do_vnf_library_link`
- `LibraryLinkProjectTests.Pasta_sem_vnforge_json_mostra_o_erro_em_portugues_e_nao_liga_nada`
- `ProjectGameRelocatorMixedTests` está em `tests/VNForge.Core.Tests/Projects/`, fora deste projeto. Não mexer; entra na auditoria do Core (T5).
- `MainWindowShellTests.Abrir_o_jogo_da_biblioteca_sem_projeto_leva_a_Ficha_e_traduz_a_ficha_sem_projeto` (único novo de #708 nessa classe), e com ele os três de #619 e #688 da mesma classe (seção 4, R6).
- `ControlLogicTests`: os 8 acima.

**Oráculos (regra 8):**
- `TranslationFieldTextTests.Ida_e_volta_sem_edicao_devolve_o_mesmo_texto` (6 linhas), `Editar_o_campo_grava_quebra_de_linha_e_pagina_e_poe_o_fim_de_volta`, `Campo_vazio_grava_vazio_mesmo_com_marcador_no_fim`
- `ImageComposerHeadlessTests`: os 4 métodos (6 execuções)
- `MoneyFormattingTests.Le_o_valor_em_reais_como_a_pessoa_digita` e `Recusa_o_que_nao_e_valor`
- `BuildPatchViewModelTests.Restaurar_originais_devolve_o_exe_byte_a_byte_e_a_lista_mostra_o_executavel`
- `BuildTestViewModelTests.Voltar_ao_jogo_original_devolve_os_arquivos_byte_a_byte_sem_pedir_confirmacao`
- `ReleaseViewModelTests.Pasta_de_saida_nao_pode_ser_a_do_jogo_nem_ficar_dentro_dela` (4 linhas)
- `SettingsEncodingConflictTests.Trocar_para_cp1252_com_o_ajuste_em_conflito_mostra_a_faixa_e_Usar_tres_pontos_resolve`
- `ConferenceFitTests.Corrigir_e_ir_para_o_proximo_divide_a_fala_e_mantem_os_marcadores_do_original_mais_a_virada_a_mais`
- `ConferenceViewModelTests.Corrigir_tudo_troca_a_pontuacao_japonesa_e_deixa_o_que_nao_tem_substituto`
- `LineShortenerTests.Versao_que_perde_o_marcador_sai_da_lista_e_sem_nenhuma_aproveitavel_vira_mensagem`
- `LineSpeakerTests.Narracao_tira_o_nome_da_linha_e_o_falante_do_jogo_volta_a_ele`
- `GlossaryGameCharactersTests.Salvar_a_traducao_do_personagem_grava_a_mesma_nas_unidades_name_do_mesmo_original`
- Só numa plataforma: `SettingsViewModelTests.Chave_pelo_cofre_dpapi_real_tambem_nao_aparece_no_projeto` (Windows)

**Chave e gasto (R5):** todos os de R5 da seção 4. Há um teste de recusa por caminho que gasta: lote noturno (`MainWindowNightBatchTests.Sem_cliente...`), Bíblia, pedido em lote, passada de editor, imagens, Avaliação, tradução de cenas (`MainWindowTranslateTests`), janela de traduzir (`TranslateDialogViewModelTests`), sugestões do Glossário e exemplo (`SampleSceneTests`).

## 6. Jornadas do App (R6)

Uma por fluxo principal. "Cobre" quer dizer que um teste mantido faz o fluxo pelo ViewModel e confere o resultado (arquivo gravado, projeto aberto, jogo alterado).

| # | Jornada | Teste que cobre | Estado |
|---|---|---|---|
| 1 | Abrir o app pela primeira vez | `SampleSceneTests.Experimentar_cria_o_projeto_na_pasta_de_exemplos_abre_e_leva_ao_Painel` (lista de recentes vazia, cria o exemplo, abre, leva ao Painel); a tela de início abre no smoke | cobre |
| 2 | Adicionar pasta à Biblioteca | `LibraryCompositionTests.A_biblioteca_do_app_le_o_library_json_da_pasta_de_configuracao_e_varre_uma_pasta` (monta a Biblioteca como o App, varre, confere o `library.json` gravado) | cobre |
| 3 | Criar projeto a partir de um jogo | `MainWindowViewModelTests.Projeto_criado_pelo_assistente_abre_no_app_e_entra_nos_recentes` (Início, assistente, projeto aberto) e `NewProjectWizardViewModelTests.Criar_grava_o_vnforge_json...`. Falta a entrada pela Biblioteca: o comando `TranslateProjectlessGameCommand` ("Traduzir este jogo" na Ficha sem projeto) não tem teste nenhum | falta 1 (ver abaixo) |
| 4 | Ligar jogo a projeto existente | `LibraryLinkProjectTests` (3) | cobre |
| 5 | Abrir a Ficha sem projeto | `MainWindowShellTests.Abrir_o_jogo_da_biblioteca_sem_projeto_leva_a_Ficha_e_traduz_a_ficha_sem_projeto` | cobre |
| 6 | Traduzir uma cena | `MainWindowTranslateTests.Uma_unica_cena_escolhida_que_nao_e_a_aberta_e_a_que_a_janela_traduz` e `A_traducao_da_IA_deixa_um_ponto_de_restauracao_e_o_Desfazer_da_lista_devolve_as_linhas` | cobre |
| 7 | Revisar e aprovar | `ReviewModeRiskTests.Esta_boa_atravessa_as_cenas_grava_cada_linha_e_a_tela_final_nao_oferece_a_proxima_cena` e `ReviewModeTests.Esta_boa_registra_a_aprovacao_com_a_hora_do_relogio_em_sessions_json` | cobre |
| 8 | Aplicar no jogo | `BuildTestViewModelTests.Testar_no_jogo_confere_injeta_com_backup_abre_o_jogo_e_guarda_os_cinco_passos` e `BuildViewModelTests.Injetar_pede_confirmacao_e_so_grava_depois_de_confirmar` | cobre |
| 9 | Restaurar o original | `BuildViewModelTests.Restaurar_pede_confirmacao_e_devolve_o_original` e `BuildTestViewModelTests.Voltar_ao_jogo_original_devolve_os_arquivos_byte_a_byte_sem_pedir_confirmacao` | cobre |
| 10 | Pausar e retomar a tradução noturna | Retomar: `MainWindowNightBatchTests.Ao_abrir_o_projeto_o_app_retoma_o_lote_que_ficou_na_pasta` (lote enviado, app reaberto, resultado gravado) e `TranslateDialogViewModelTests.Iniciar_na_noite_pede_o_lote_e_a_cena_de_exemplo_nao_tem_a_escolha`. O App não tem botão de pausa: pausar é fechar o app com o lote na API. A jornada inteira (enviar pelo ViewModel, fechar, reabrir, gravar) não existe como um teste só: o teste de retomar monta o envio por fora | falta 1, parcial |

Faltam 2 jornadas, e nenhuma pode ser escrita por esta auditoria:
- **J3b**, criar projeto a partir de um jogo da Biblioteca: abrir o jogo sem projeto, `TranslateProjectlessGameCommand`, assistente com a pasta e o VNDB que a Biblioteca sabe, criar, conferir `vnforge.json` e o `ProjectPath` do jogo no `library.json`.
- **J10b**, noturna de ponta a ponta: pedir o lote pela janela de traduzir, fechar o app, reabrir o projeto, conferir as linhas gravadas e o gasto registrado.

O `docs/04-testes.md` prevê "perto de 20 jornadas"; o doc lista 10 fluxos, então 10 a 12 testes de jornada é o que existe aqui. O resto do orçamento de "jornadas" não tem fluxo para ocupar.

## 7. O que só um teste cortado cobria (regra 9)

Coberturas que ficam sem teste no App depois do corte. Cada uma tem destino.

- Gravação do projeto sem proteção própria no App: capítulos (`EditorChaptersTests.Falha_ao_gravar_mostra_a_mensagem_e_nao_mexe_na_lista`), marca de leitura (`ReadSceneTests.Falha_ao_gravar_a_marca_fica_na_fala_e_diz_o_motivo`), notas de captura (`GameCapturesViewModelTests.A_nota_e_gravada_depois_de_uma_pausa...`) e interruptor do lançador (`LibraryPlayTests.Interruptor_que_nao_gravou...`). Ficam para a #699 (costura de falha de disco no Core, que roda cada caminho de gravação contra rename que zera, arquivo travado, disco cheio e pasta somente leitura).
- Privacidade do retrato adulto (`AdultPrivacyTests`, `VndbImagePrivacyTests`, `LibraryExportTests.So_o_modo_Mostrar_poe_o_jogo_adulto_no_catalogo` x 3): sem risco na tabela, então saem. Se o Alessandro quiser tratar vazamento de conteúdo adulto como risco, é preciso um R9 na tabela antes; nesse caso mantenha `VndbImagePrivacyTests.Arquivo_ausente_ou_ilegivel_vale_o_padrao_e_a_proxima_leitura_tenta_de_novo` e `LibraryExportTests.So_o_modo_Mostrar_poe_o_jogo_adulto_no_catalogo`. Decisão dele, não do builder; o corte acima segue a tabela como está.
- Segurar a tecla Enter na revisão aprova uma fala só (`ReviewModeKeyTests.Segurar_o_Enter_aprova_so_uma_fala_porque_a_repeticao_da_tecla_e_ignorada`): comportamento de teclado de janela, não está na tabela. Sai com a classe.
- Atualização do dicionário de japonês que falha mantém o antigo (`LineDictionaryTests.Atualizar_que_falha_mostra_o_erro_e_mantem_o_dicionario_antigo`): arquivo externo (R7), mas é dado de apoio baixado, não dado do usuário. Sai; o Core guarda o download.
- Valores inválidos do orçamento no `app.json` voltam ao padrão (`BudgetAppSettingsFileTests.Valores_invalidos_do_orcamento_voltam_ao_padrao_sem_derrubar_o_app` x 2) e a cotação inválida (`BudgetSettingsViewModelTests.Cotacao_invalida_mostra_o_erro_e_nao_grava` x 3): saem; o parser fica em `MoneyFormattingTests` e a leitura do arquivo no projeto AI (T5).
- Os testes de contraste de cor (`CharacterColorTests.Cada_cor_passa_de_4_5_para_1_nos_fundos_de_cada_tema` x 6) saem: acessibilidade visual é conferida no render ao lado do quadro.

## 8. Tempo e classes lentas

Tempos da medição completa (91,0 s de parede, 85,7 s de soma):

| Classe | Antes | Depois |
|---|---:|---:|
| `ScreenSmokeTests` | 33,1 s | 33,1 s (fica) |
| `BuildTestViewModelTests` | 9,6 s | uns 2,3 s (4 de 18) |
| `ReviewModeKeyTests` | 5,2 s | sai |
| `ClippedTextHeadlessTests` | 3,8 s | sai |
| `LibraryVersionTests` | 2,7 s | uns 1,9 s (3 de 5) |
| `GameTextboxHeadlessTests` | 2,6 s | sai |
| `MotionHeadlessTests` | 2,5 s | sai |
| `GlossarySuggestionsTests` | 2,4 s | menos de 0,3 s (2 de 27) |
| `AddressTermsViewModelTests` | 1,9 s | menos de 0,2 s (2 de 20) |
| Outras 14 classes de janela | uns 7,6 s | saem |

Sobram uns 8 s fora do smoke. O smoke (41 aberturas de janela, média de 0,8 s) é 80% da suíte restante, e é a parte que o documento manda manter.
