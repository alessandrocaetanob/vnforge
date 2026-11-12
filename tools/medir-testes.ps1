# Roda um dll de testes num processo e mede o tempo de parede, o pico do conjunto de trabalho e a contagem do JUnit
# (testes, falhas, ignorados). É a medida de tempo e de memória de docs/04-testes.md; no Linux use
# `/usr/bin/time -v dotnet <dll> -noLogo` e leia "Maximum resident set size".
#
# Uso: pwsh tools/medir-testes.ps1 -Dll tests/VNForge.Core.Tests/bin/Release/net10.0/VNForge.Core.Tests.dll -Name VNForge.Core.Tests
#   -ClassesFile  um nome de classe por linha: roda só essas (uma fatia do App, por exemplo)
#   -RenderDir    pasta dos PNGs de render (VNFORGE_RENDER_DIR), dentro de .tmp
# O JUnit e a saída ficam em .tmp/medidas/<Name>.xml e .log. O pico é lido a cada 250 ms do processo do dotnet.
param(
  [Parameter(Mandatory)][string]$Dll,
  [Parameter(Mandatory)][string]$Name,
  [string]$ClassesFile = '',
  [string]$RenderDir = ''
)
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root '.tmp\medidas'
New-Item -ItemType Directory -Force $out | Out-Null
$junit = Join-Path $out "$Name.xml"
if (Test-Path -LiteralPath $junit) { Remove-Item -LiteralPath $junit -Force }
$arguments = @($Dll, '-noLogo', '-result-junit', $junit)
if ($ClassesFile -ne '') {
  foreach ($c in (Get-Content -LiteralPath $ClassesFile)) { if ($c.Trim() -ne '') { $arguments += @('-class', $c.Trim()) } }
}
if ($RenderDir -ne '') { $env:VNFORGE_RENDER_DIR = $RenderDir }
$log = Join-Path $out "$Name.log"
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath dotnet -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardOutput $log -WorkingDirectory $root
$peak = 0
while (-not $p.HasExited) {
  try { $p.Refresh(); if ($p.PeakWorkingSet64 -gt $peak) { $peak = $p.PeakWorkingSet64 } } catch { Write-Verbose "Leitura do pico falhou: $($_.Exception.Message)" }
  Start-Sleep -Milliseconds 250
}
$sw.Stop()
$xml = Get-Content -Raw -LiteralPath $junit
$tests = ([regex]::Matches($xml, '<testcase ')).Count
$fail = ([regex]::Matches($xml, '<failure[ >]|<error[ >]')).Count
$skip = ([regex]::Matches($xml, '<skipped')).Count
$inv = [Globalization.CultureInfo]::InvariantCulture
"{0}`tsaída {1}`t{2}s`t{3} MB`t{4} testes`t{5} falhas`t{6} ignorados" -f $Name, $p.ExitCode, $sw.Elapsed.TotalSeconds.ToString('F1', $inv), [int]($peak / 1MB), $tests, $fail, $skip
