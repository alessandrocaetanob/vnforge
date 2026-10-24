<#
.SYNOPSIS
Gera src/VNForge.App/Themes/Tokens.axaml a partir de docs/design/tokens.json.
.DESCRIPTION
O design system é a fonte: mude o tokens.json e rode este script; nunca edite o Tokens.axaml à mão.
Nomes: "bg-card" vira Vnf.BgCard (pincel) e Vnf.BgCardColor (cor). Tamanhos, espaços e raios são recursos fora dos temas.
#>
[CmdletBinding()]
param(
    [string]$Tokens = (Join-Path $PSScriptRoot '..\docs\design\tokens.json'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\VNForge.App\Themes\Tokens.axaml')
)

$ErrorActionPreference = 'Stop'
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$json = Get-Content -LiteralPath $Tokens -Raw -Encoding UTF8 | ConvertFrom-Json

function ConvertTo-Pascal([string]$name) {
    ($name -split '-' | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join ''
}

function Resolve-Color($token, [string]$theme) {
    $value = $token.value.$theme
    if ($value -match '^\{(.+)\}$') {
        $ref = $json.color.tokens | Where-Object { $_.name -eq $Matches[1] }
        if (-not $ref) { throw "Token '$($token.name)' referencia '$($Matches[1])', que não existe." }
        return Resolve-Color $ref $theme
    }
    $value
}

function ConvertTo-Px([string]$value) {
    if ($value -notmatch '^(\d+)px$') { throw "Valor '$value' não está em px." }
    $Matches[1]
}

function ConvertTo-BoxShadow([string]$css) {
    if ($css -notmatch '^(\d+)(?:px)? (\d+)px (\d+)px rgba\((\d+),(\d+),(\d+),([\d.]+)\)$') { throw "Sombra '$css' fora do formato esperado." }
    $alpha = [int][math]::Round([double]::Parse($Matches[7], $inv) * 255)
    '{0} {1} {2} 0 #{3:X2}{4:X2}{5:X2}{6:X2}' -f $Matches[1], $Matches[2], $Matches[3], $alpha, [int]$Matches[4], [int]$Matches[5], [int]$Matches[6]
}

$sb = [System.Text.StringBuilder]::new()
function Add([string]$line) { [void]$sb.AppendLine($line) }

Add '<!-- Gerado por tools/Generate-Tokens.ps1 a partir de docs/design/tokens.json. Não edite à mão. -->'
Add '<ResourceDictionary xmlns="https://github.com/avaloniaui"'
Add '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'
Add '  <ResourceDictionary.ThemeDictionaries>'
foreach ($theme in @(@{ Key = 'Dark'; Id = 'dark' }, @{ Key = 'Light'; Id = 'light' })) {
    Add "    <ResourceDictionary x:Key=`"$($theme.Key)`">"
    foreach ($token in $json.color.tokens) {
        $name = 'Vnf.' + (ConvertTo-Pascal $token.name)
        $color = (Resolve-Color $token $theme.Id).ToUpperInvariant()
        Add "      <Color x:Key=`"${name}Color`">$color</Color>"
        Add "      <SolidColorBrush x:Key=`"$name`" Color=`"$color`"/>"
    }
    foreach ($shadow in $json.shadow.tokens) {
        $name = 'Vnf.' + (ConvertTo-Pascal $shadow.name)
        Add "      <BoxShadows x:Key=`"$name`">$(ConvertTo-BoxShadow $shadow.value.($theme.Id))</BoxShadows>"
    }
    Add '    </ResourceDictionary>'
}
Add '  </ResourceDictionary.ThemeDictionaries>'
Add ''
foreach ($token in $json.size.tokens) {
    Add "  <x:Double x:Key=`"Vnf.$(ConvertTo-Pascal $token.name)`">$(ConvertTo-Px $token.value)</x:Double>"
}
foreach ($token in $json.spacing.tokens) {
    Add "  <x:Double x:Key=`"Vnf.$(ConvertTo-Pascal $token.name)`">$(ConvertTo-Px $token.value)</x:Double>"
}
foreach ($token in $json.radius.tokens) {
    Add "  <CornerRadius x:Key=`"Vnf.$(ConvertTo-Pascal $token.name)`">$(ConvertTo-Px $token.value)</CornerRadius>"
}
Add ''
Add '  <!-- Famílias embutidas em Assets/Fonts (todas OFL). O japonês nunca cai no fallback da família ui. -->'
Add '  <FontFamily x:Key="Vnf.FontUi">avares://VNForge.App/Assets/Fonts#Noto Sans, avares://VNForge.App/Assets/Fonts#Noto Sans JP</FontFamily>'
Add '  <FontFamily x:Key="Vnf.FontJp">avares://VNForge.App/Assets/Fonts#Noto Sans JP</FontFamily>'
Add '  <FontFamily x:Key="Vnf.FontMono">avares://VNForge.App/Assets/Fonts#JetBrains Mono</FontFamily>'
Add '  <FontFamily x:Key="Vnf.FontDisplay">avares://VNForge.App/Assets/Fonts#Zen Kaku Gothic New, avares://VNForge.App/Assets/Fonts#Noto Sans JP</FontFamily>'
Add '</ResourceDictionary>'

$text = $sb.ToString().Replace("`r`n", "`n")
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output), $text, [System.Text.UTF8Encoding]::new($false))
Write-Host "Gerado: $Output"
