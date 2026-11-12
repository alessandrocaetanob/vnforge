#!/usr/bin/env bash
# Roda as suítes de tests/*.Tests, cada uma num processo, e grava um JUnit por processo em test-results/ (o CircleCI
# lê essa pasta no store_test_results) e um TRX por processo em test-results/trx/ (o Sonar lê esses arquivos em
# sonar.cs.vstest.reportsPaths, para publicar tests, test_failures, skipped_tests e test_execution_time; o CircleCI só
# lê .xml e ignora os .trx). O App roda em dois processos que dividem os testes pelo trait Tipo=Tela (ver abaixo); cada
# um dos outros projetos roda num processo.
#
# Chama `dotnet <dll>`, nunca o apphost: assim o mesmo script roda no Linux do CI e no Git Bash do Windows.
# Variáveis:
#   VNFORGE_TEST_CONFIGURATION  padrão Release
#   VNFORGE_PARALLEL            quantos projetos rodam ao mesmo tempo; padrão 4 (as CPUs do container do job)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

configuration="${VNFORGE_TEST_CONFIGURATION:-Release}"
parallel="${VNFORGE_PARALLEL:-4}"
results=test-results
logs="$results/logs"
trx="$results/trx"

# O runner é reaproveitado entre jobs: um XML de outra rodada contaria testes que esta não rodou.
rm -rf "$results"
mkdir -p "$logs" "$trx"

# Sem testes rodados, o coverage.xml sai vazio e o Sonar end não pode publicá-lo por cima da análise anterior.
abort() {
  echo "ERRO: $*" >&2
  rm -f .sonar-begun
  exit 1
}

[[ "$parallel" =~ ^[1-9][0-9]*$ ]] || abort "VNFORGE_PARALLEL precisa ser um inteiro positivo (veio '$parallel')."

# Cada pasta tests/*.Tests tem que ter o dll: um projeto que não compilou não pode sumir da rodada em silêncio.
# O ConformanceKit é biblioteca, não casa com *.Tests e não entra.
#
# O App é a suíte mais longa e roda em série (Parallelization Mode=None: a sessão headless do Avalonia é uma só por processo),
# então ele vira dois processos com filtros complementares do runner do xUnit v3: -trait "Tipo=Tela" roda as classes
# marcadas com [Trait("Tipo", "Tela")] (o smoke das telas e as maiores de janela) e -trait- "Tipo=Tela" roda todas as outras.
# Os dois filtros são complementares por construção: um teste cai num processo ou no outro, nunca nos dois nem em nenhum,
# e um teste sem o trait roda no segundo (o AssemblyFixture sobe a sessão headless nos dois). A marca existe só para
# equilibrar o tempo das metades; a conferência abaixo reprova um processo que não rodou teste nenhum.
# Cada processo tem nome próprio (VNForge.App.Tests-tela e VNForge.App.Tests-resto) nos logs, no JUnit e no TRX.
names=()
declare -A dlls=() filters=()
for dir in tests/*.Tests; do
  name="$(basename "$dir")"
  dll="$dir/bin/$configuration/net10.0/$name.dll"
  [[ -f "$dll" ]] || abort "$dll não existe: compile a solução em $configuration antes."
  if [[ "$name" = VNForge.App.Tests ]]; then
    for part in tela resto; do
      dlls[$name-$part]="$dll"
    done
    filters[$name-tela]='-trait Tipo=Tela'
    filters[$name-resto]='-trait- Tipo=Tela'
    names=("$name-tela" "$name-resto" "${names[@]}")
  else
    dlls[$name]="$dll"
    filters[$name]=''
    names+=("$name")
  fi
done
[[ "${#names[@]}" -gt 0 ]] || abort "nenhum projeto em tests/*.Tests."
[[ -n "${dlls[VNForge.App.Tests-tela]:-}" ]] || abort "tests/VNForge.App.Tests não foi encontrado."

# Roda um processo e grava "<código de saída> <segundos>" ao terminar; o arquivo é o que a conferência final lê.
run_process() {
  local name="$1" start=$SECONDS rc=0 child
  # $filters é sem aspas de propósito: são argumentos soltos ("-trait Tipo=Tela") ou nenhum.
  # shellcheck disable=SC2086
  dotnet "${dlls[$name]}" -noLogo ${filters[$name]} -result-junit "$results/$name.xml" -result-trx "$trx/$name.trx" > "$logs/$name.log" 2>&1 &
  child=$!
  # O subshell recebe o TERM do cancelamento e repassa ao dotnet, que senão sobrevive no runner reaproveitado.
  trap 'kill "$child" 2> /dev/null || true' INT TERM
  wait "$child" || rc=$?
  echo "$rc $((SECONDS - start))" > "$logs/$name.status"
}

# Se o CircleCI cancela o job, os processos de teste não podem ficar vivos no runner.
cancel() {
  trap - INT TERM
  echo "Cancelado: encerrando os processos de teste." >&2
  kill $(jobs -rp) 2> /dev/null || true
  wait || true
  exit 130
}
trap cancel INT TERM

echo "Rodando ${#names[@]} projetos de teste, $parallel por vez."
for name in "${names[@]}"; do
  while (($(jobs -rp | wc -l) >= parallel)); do wait -n || true; done
  run_process "$name" &
done
wait

count() {
  local pattern=$1 file=$2
  { grep -o "$pattern" "$file" || true; } | wc -l | tr -d ' '
}

failed=0
total_tests=0
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  xml="$results/$name.xml"
  tests=0 failures=0 skipped=0
  if [[ -f "$xml" ]]; then
    tests="$(count '<testcase ' "$xml")"
    failures=$(($(count '<failure[ >]' "$xml") + $(count '<error[ >]' "$xml")))
    skipped="$(count '<skipped' "$xml")"
  fi
  total_tests=$((total_tests + tests))
  echo
  echo "===== $name: saída $rc, ${seconds}s, $tests testes, $failures falhas, $skipped ignorados ====="
  cat "$logs/$name.log"
  # Código 0 sem JUnit ou sem nenhum teste é processo que não rodou o que devia (ou saiu antes de gravar): também reprova.
  # Um filtro de trait que não casa com nada cairia aqui.
  if [[ "$rc" -ne 0 ]] || [[ ! -f "$xml" ]] || [[ "$tests" -eq 0 ]]; then
    failed=1
    echo ">>> $name reprovou (saída $rc, JUnit $([[ -f "$xml" ]] && echo gravado || echo ausente), $tests testes)."
  fi
done

echo
echo "Resumo: $total_tests testes em ${#names[@]} processos, $SECONDS s no total."
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  printf '  %-36s saída %s, %ss\n' "$name" "$rc" "$seconds"
done
exit "$failed"
