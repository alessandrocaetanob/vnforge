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

# Divide as classes do App. O -list classes imprime um banner e uma linha em branco antes das classes.
classes="$logs/$app.classes"
dotnet "${dlls[$app]}" -list classes | tr -d '\r' | grep -E '^[A-Za-z_][A-Za-z0-9_.+`]*$' | grep -F . | sort > "$classes" || true
[ -s "$classes" ] || abort "o -list classes não devolveu nenhuma classe do $app."
class_total="$(wc -l < "$classes")"

for ((i = 0; i < shards; i++)); do : > "$logs/$app-$i.classes"; done

if command -v circleci > /dev/null; then
  # O circleci tests run reparte as classes pelas durações que o store_test_results publicou nas rodadas anteriores
  # (classname do JUnit), e é ele que habilita "Rerun failed tests". O --command só guarda a lista num arquivo: o
  # script lança um processo por fatia a partir dele, e um xargs que partisse a lista em duas chamadas faria os dois
  # processos gravarem o mesmo JUnit.
  for ((i = 0; i < shards; i++)); do
    circleci tests run --index="$i" --total="$shards" --split-by=timings --timings-type=classname \
      --command="cat > '$root/$logs/$app-$i.classes'" --verbose < "$classes" > "$logs/split-$i.log" 2>&1 \
      || { cat "$logs/split-$i.log" >&2; abort "circleci tests run falhou na fatia $i."; }
  done
else
  # Sem o CLI (rodada local): as classes de Rendering abrem a janela principal a cada caso e levam 81% do tempo,
  # então cada fatia recebe uma parte igual delas e uma parte igual das demais.
  awk -v n="$shards" -v dir="$logs" -v app="$app" '
    /^VNForge\.App\.Tests\.Rendering\./ { print > (dir "/" app "-" (r++ % n) ".classes"); next }
    { print > (dir "/" app "-" (o++ % n) ".classes") }' "$classes"
fi

# Todas as classes, cada uma em exatamente uma fatia. Uma rodada parcial calada é o pior resultado possível aqui.
# O "Rerun failed tests" do CircleCI também entrega só um pedaço das classes, e o job não tem como distingui-lo
# de uma falha do divisor (não há variável de ambiente nem flag, só uma linha de log do CLI): falha antes de rodar
# qualquer teste e manda usar "Rerun workflow from failed".
shard_files=()
for ((i = 0; i < shards; i++)); do shard_files+=("$logs/$app-$i.classes"); done
sort "${shard_files[@]}" | sed '/^$/d' > "$logs/$app.assigned"
duplicated="$(uniq -d "$logs/$app.assigned")"
[ -z "$duplicated" ] || abort $'classe em mais de uma fatia:\n'"$duplicated"
missing="$(comm -23 "$classes" <(uniq "$logs/$app.assigned"))"
if [ -n "$missing" ]; then
  abort $'a lista de classes do App ficou incompleta ('"$(wc -l <<< "$missing")"$' de '"$class_total"$' classes sem fatia).\n'\
$'Numa repetição parcial ("Rerun failed tests") isso é esperado: use "Rerun workflow from failed", que roda tudo.\n'"$missing"
fi
extra="$(comm -13 "$classes" <(uniq "$logs/$app.assigned"))"
[ -z "$extra" ] || abort $'o divisor devolveu classe que o -list classes não tem:\n'"$extra"

names=()
pids=()

# Roda um processo e grava "<código de saída> <segundos>" ao terminar; o arquivo é o que o laço de espera confere.
run_process() {
  local name="$1" start=$SECONDS rc=0
  shift
  "$@" > "$logs/$name.log" 2>&1 || rc=$?
  echo "$rc $((SECONDS - start))" > "$logs/$name.status"
}

launch() {
  local name="$1"
  shift
  names+=("$name")
  run_process "$name" "$@" &
  pids+=("$!")
}

# Se o CircleCI cancela o job, os processos de teste não podem ficar vivos no runner.
trap 'kill "${pids[@]}" 2> /dev/null || true' INT TERM

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
    failures="$(count '<failure' "$xml")"
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
