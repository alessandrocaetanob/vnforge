#!/usr/bin/env bash
# Roda as suítes de tests/*.Tests em paralelo, cada uma num processo, e grava um JUnit por processo
# em test-results/ (o CircleCI lê essa pasta no store_test_results). A suíte do App, que sozinha
# levava 18 dos 23 minutos do job, é dividida em VNFORGE_APP_SHARDS processos (padrão 3).
#
# Chama `dotnet <dll>`, nunca o apphost: assim o mesmo script roda no Linux do CI e no Git Bash do Windows.
# Variáveis: VNFORGE_TEST_CONFIGURATION (padrão Release) e VNFORGE_APP_SHARDS (padrão 3).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

configuration="${VNFORGE_TEST_CONFIGURATION:-Release}"
shards="${VNFORGE_APP_SHARDS:-3}"
results=test-results
logs="$results/logs"
app=VNForge.App.Tests

# O runner é reaproveitado entre jobs: um XML de outra rodada contaria testes que esta não rodou.
rm -rf "$results"
mkdir -p "$logs"

# Sem testes rodados, o coverage.xml sai vazio e o Sonar end não pode publicá-lo por cima da análise anterior.
abort() {
  echo "ERRO: $*" >&2
  rm -f .sonar-begun
  exit 1
}

[[ "$shards" =~ ^[1-9][0-9]*$ ]] || abort "VNFORGE_APP_SHARDS precisa ser um inteiro positivo (veio '$shards')."

# Cada pasta tests/*.Tests tem que ter o dll: um projeto que não compilou não pode sumir da rodada em silêncio.
# O ConformanceKit é biblioteca, não casa com *.Tests e não entra.
declare -A dlls=()
for dir in tests/*.Tests; do
  name="$(basename "$dir")"
  dll="$dir/bin/$configuration/net10.0/$name.dll"
  [ -f "$dll" ] || abort "$dll não existe: compile a solução em $configuration antes."
  dlls[$name]="$dll"
done
[ -n "${dlls[$app]:-}" ] || abort "tests/$app não foi encontrado."

# Divide as classes do App. O -list classes imprime um banner e uma linha em branco antes das classes; só isso é
# descartado. Qualquer outra linha tem que parecer um nome de classe, senão a conferência de completude abaixo
# compararia as fatias com uma lista que já perdeu classes.
classes="$logs/$app.classes"
dotnet "${dlls[$app]}" -list classes > "$logs/$app.list" || abort "o -list classes do $app falhou."
tr -d '\r' < "$logs/$app.list" | sed -e '/^xUnit\.net /d' -e '/^[[:space:]]*$/d' | sort > "$classes"
[ -s "$classes" ] || abort "o -list classes não devolveu nenhuma classe do $app."
unexpected="$(grep -Ev '^[^[:space:]]+$' "$classes" || true)"
[ -z "$unexpected" ] || abort $'linha do -list classes que não é nome de classe:\n'"$unexpected"
class_total="$(wc -l < "$classes")"

# As classes de Rendering abrem a janela principal a cada caso e levam 81% do tempo: cada fatia recebe uma parte
# igual delas e uma parte igual das demais.
local_split() {
  for ((i = 0; i < shards; i++)); do : > "$logs/$app-$i.classes"; done
  awk -v n="$shards" -v dir="$logs" -v app="$app" '
    /^VNForge\.App\.Tests\.Rendering\./ { print > (dir "/" app "-" (r++ % n) ".classes"); next }
    { print > (dir "/" app "-" (o++ % n) ".classes") }' "$classes"
}

# O CIRCLECI também é exigido: quem tem o CLI instalado na máquina de desenvolvimento roda o rodízio local.
use_circleci=0
if [ -n "${CIRCLECI:-}" ] && command -v circleci > /dev/null; then use_circleci=1; fi

# Remove CR e linhas vazias das fatias e monta a lista atribuída (ordenada, com repetições).
assign_shards() {
  local f
  shard_files=()
  for ((i = 0; i < shards; i++)); do
    f="$logs/$app-$i.classes"
    tr -d '\r' < "$f" | sed '/^[[:space:]]*$/d' > "$f.tmp"
    mv "$f.tmp" "$f"
    shard_files+=("$f")
  done
  sort "${shard_files[@]}" > "$logs/$app.assigned"
}

if [ "$use_circleci" -eq 1 ]; then
  # O circleci tests split reparte as classes pelas durações que o store_test_results publicou nas rodadas anteriores
  # (classname do JUnit) e devolve a fatia no stdout. Não é o circleci tests run: no job 2ab99318 (09/11) ele saiu
  # com código 0 sem entregar nenhuma classe ao --command, e as 3 fatias ficaram vazias. O script lança um processo
  # por fatia a partir do arquivo, e um xargs que partisse a lista em duas chamadas faria dois processos gravarem
  # o mesmo JUnit.
  for ((i = 0; i < shards; i++)); do : > "$logs/$app-$i.classes"; done
  for ((i = 0; i < shards; i++)); do
    if ! circleci tests split --index="$i" --total="$shards" --split-by=timings --timings-type=classname --verbose \
      < "$classes" > "$logs/$app-$i.classes" 2> "$logs/split-$i.log"; then
      # Erro do próprio CLI (rede, plugin) não é fatia parcial: o rodízio local ainda roda tudo.
      echo "AVISO: circleci tests split falhou na fatia $i; usando o rodízio local em todas as fatias." >&2
      cat "$logs/split-$i.log" >&2
      use_circleci=0
      break
    fi
  done
fi
if [ "$use_circleci" -eq 0 ]; then local_split; fi
assign_shards

# Todas as classes, cada uma em exatamente uma fatia. Uma rodada parcial calada é o pior resultado possível aqui.
# Se o CLI do CircleCI devolveu fatias que não cobrem a lista, o rodízio local assume em todas as fatias.
if [ "$use_circleci" -eq 1 ] && ! cmp -s "$logs/$app.assigned" "$classes"; then
  echo "AVISO: as fatias do circleci tests split não cobrem as $class_total classes do App exatamente uma vez; usando o rodízio local em todas as fatias." >&2
  for ((i = 0; i < shards; i++)); do
    echo "--- split-$i.log" >&2
    cat "$logs/split-$i.log" >&2
  done
  local_split
  assign_shards
fi

# Daqui em diante uma divergência é bug do script, não do divisor.
duplicated="$(uniq -d "$logs/$app.assigned")"
[ -z "$duplicated" ] || abort $'classe em mais de uma fatia:\n'"$duplicated"
missing="$(comm -23 "$classes" <(uniq "$logs/$app.assigned"))"
if [ -n "$missing" ]; then
  abort $'a lista de classes do App ficou incompleta ('"$(wc -l <<< "$missing")"$' de '"$class_total"$' classes sem fatia):\n'"$missing"
fi
extra="$(comm -13 "$classes" <(uniq "$logs/$app.assigned"))"
[ -z "$extra" ] || abort $'a divisão devolveu classe que o -list classes não tem:\n'"$extra"

names=()
pids=()

# Roda um processo e grava "<código de saída> <segundos>" ao terminar; o arquivo é o que o laço de espera confere.
run_process() {
  local name="$1" start=$SECONDS rc=0 child
  shift
  "$@" > "$logs/$name.log" 2>&1 &
  child=$!
  # O subshell recebe o TERM do cancelamento e repassa ao dotnet, que senão sobrevive no runner reaproveitado.
  trap 'kill "$child" 2> /dev/null || true' INT TERM
  wait "$child" || rc=$?
  echo "$rc $((SECONDS - start))" > "$logs/$name.status"
}

launch() {
  local name="$1"
  shift
  names+=("$name")
  run_process "$name" "$@" &
  pids+=("$!")
}

# Se o CircleCI cancela o job, os processos de teste não podem ficar vivos no runner: cada subshell mata o seu
# dotnet, e o script espera por eles antes de sair com erro.
cancel() {
  trap - INT TERM
  echo "Cancelado: encerrando os processos de teste." >&2
  kill "${pids[@]}" 2> /dev/null || true
  wait || true
  exit 130
}
trap cancel INT TERM

for name in "${!dlls[@]}"; do
  [ "$name" = "$app" ] && continue
  launch "$name" dotnet "${dlls[$name]}" -noLogo -result-junit "$results/$name.xml"
done

for ((i = 0; i < shards; i++)); do
  # Fatia vazia não roda: sem -class o xUnit executaria a suíte inteira.
  [ -s "$logs/$app-$i.classes" ] || continue
  class_args=()
  while IFS= read -r class; do
    [ -n "$class" ] && class_args+=(-class "$class")
  done < "$logs/$app-$i.classes"
  launch "$app-$i" dotnet "${dlls[$app]}" -noLogo "${class_args[@]}" -result-junit "$results/$app-$i.xml"
done

# O passo tem no_output_timeout de 30m: sem esta linha, uma fatia lenta calaria o job.
echo "Rodando ${#names[@]} processos de teste (App em $shards fatias, $class_total classes)."
tick=$SECONDS
while :; do
  running=()
  for name in "${names[@]}"; do
    [ -f "$logs/$name.status" ] || running+=("$name")
  done
  [ "${#running[@]}" -gt 0 ] || break
  if ((SECONDS - tick >= 120)); then
    echo "[$((SECONDS / 60)) min] rodando: ${running[*]}"
    tick=$SECONDS
  fi
  sleep 3
done
wait

count() { { grep -o "$1" "$2" || true; } | wc -l | tr -d ' '; }

failed=0
total_tests=0
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  xml="$results/$name.xml"
  tests=0 failures=0 skipped=0
  if [ -f "$xml" ]; then
    tests="$(count '<testcase ' "$xml")"
    failures=$(($(count '<failure[ >]' "$xml") + $(count '<error[ >]' "$xml")))
    skipped="$(count '<skipped' "$xml")"
  fi
  total_tests=$((total_tests + tests))
  echo
  echo "===== $name: saída $rc, ${seconds}s, $tests testes, $failures falhas, $skipped ignorados ====="
  cat "$logs/$name.log"
  # Código 0 sem JUnit é processo que não rodou teste nenhum (ou saiu antes de gravar): também reprova.
  if [ "$rc" -ne 0 ] || [ ! -f "$xml" ]; then
    failed=1
    echo ">>> $name reprovou (saída $rc, JUnit $([ -f "$xml" ] && echo gravado || echo ausente))."
  fi
done

echo
echo "Resumo: $total_tests testes em ${#names[@]} processos, $SECONDS s no total."
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  printf '  %-36s saída %s, %ss\n' "$name" "$rc" "$seconds"
done
exit "$failed"
