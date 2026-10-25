<#
.SYNOPSIS
Gera src/VNForge.App/Assets/Icons.axaml com os ícones Fluent UI System Icons (MIT, Regular 20) usados pelo app.
.DESCRIPTION
O traçado (atributo d do SVG) vem do repositório oficial microsoft/fluentui-system-icons. Para usar outro ícone,
acrescente uma linha em $Icons (chave Icon.X = pasta do ícone no repositório, nome do arquivo) e rode o script.
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\VNForge.App\Assets\Icons.axaml'),
    [string]$Ref = 'main'
)

$ErrorActionPreference = 'Stop'

# chave XAML = (pasta no repositório, nome base do arquivo)
$Icons = [ordered]@{
    'Icon.Home'     = @('Home', 'home')
    'Icon.Board'    = @('Board', 'board')
    'Icon.Edit'     = @('Edit', 'edit')
    'Icon.Book'     = @('Book', 'book')
    'Icon.Shield'   = @('Shield Checkmark', 'shield_checkmark')
    'Icon.Box'      = @('Box', 'box')
    'Icon.Settings' = @('Settings', 'settings')
    'Icon.Search'   = @('Search', 'search')
    'Icon.Folder'   = @('Folder Open', 'folder_open')
    'Icon.Add'      = @('Add', 'add')
    'Icon.Info'     = @('Info', 'info')
    'Icon.Warning'  = @('Warning', 'warning')
    'Icon.Check'    = @('Checkmark Circle', 'checkmark_circle')
    'Icon.Error'    = @('Error Circle', 'error_circle')
    'Icon.Dismiss'  = @('Dismiss', 'dismiss')
    'Icon.Checkmark' = @('Checkmark', 'checkmark')
    'Icon.Sparkle'  = @('Sparkle', 'sparkle')
    'Icon.ArrowSync' = @('Arrow Sync', 'arrow_sync')
    'Icon.History'  = @('History', 'history')
    'Icon.More'     = @('More Horizontal', 'more_horizontal')
    'Icon.Person'   = @('Person', 'person')
    'Icon.Key'      = @('Key', 'key')
    'Icon.Glossary' = @('Book Letter', 'book_letter')
    'Icon.ChevronDown' = @('Chevron Down', 'chevron_down')
    'Icon.ChevronRight' = @('Chevron Right', 'chevron_right')
    'Icon.Open'     = @('Open', 'open')
    'Icon.Copy'     = @('Copy', 'copy')
    'Icon.Bookmark' = @('Bookmark', 'bookmark')
    'Icon.Clock'    = @('Clock', 'clock')
    'Icon.Fullscreen' = @('Full Screen Maximize', 'full_screen_maximize')
    'Icon.ArrowRight' = @('Arrow Right', 'arrow_right')
    'Icon.Lightbulb' = @('Lightbulb', 'lightbulb')
    'Icon.Money'    = @('Money', 'money')
    'Icon.Timer'    = @('Timer', 'timer')
    'Icon.Split'    = @('Split Horizontal', 'split_horizontal')
    'Icon.Play'     = @('Play', 'play')
    'Icon.Undo'     = @('Arrow Undo', 'arrow_undo')
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<!-- Gerado por tools/Update-Icons.ps1. Fluent UI System Icons (Microsoft, MIT), Regular 20. -->')
[void]$sb.AppendLine('<ResourceDictionary xmlns="https://github.com/avaloniaui"')
[void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
foreach ($entry in $Icons.GetEnumerator()) {
    $folder, $file = $entry.Value
    $url = "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/$Ref/assets/$([uri]::EscapeDataString($folder))/SVG/ic_fluent_${file}_20_regular.svg"
    $svg = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
    if ($svg -notmatch ' d="([^"]+)"') { throw "Sem atributo d em $url" }
    [void]$sb.AppendLine("  <StreamGeometry x:Key=`"$($entry.Key)`">$($Matches[1])</StreamGeometry>")
}
[void]$sb.AppendLine('</ResourceDictionary>')
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output), $sb.ToString().Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Host "Gerado: $Output"
