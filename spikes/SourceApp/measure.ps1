# Spike 10 (Design.md §12): opens preferis-probe: URLs along the shell's launch paths and prints what the probe saw.
# Only the custom scheme is registered (and removed at the end); the default browser is left alone.
# Publish the probe first: dotnet publish spikes/SourceApp
#
# The foreground in these runs is whatever window has focus while the script runs, not the launcher: what a link
# opened from the background looks like. Clicks in real applications need SourceApp --register-browser (README).
$ErrorActionPreference = 'Stop'

$dir = Join-Path $PSScriptRoot '..\..\artifacts\spikes\SourceApp' | Resolve-Path
$exe = Join-Path $dir 'SourceApp.exe'
$log = Join-Path $dir 'source-app.log'
$from = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }

& $exe --register | Out-Host
try {
    # Windows PowerShell 5.1 still loads WinRT types: the Launcher API that UWP and packaged apps use.
    $winrt = @'
[void][Windows.System.Launcher, Windows.System, ContentType = WindowsRuntime]
$null = [Windows.System.Launcher]::LaunchUriAsync([Uri]'preferis-probe://winrt-launcher')
Start-Sleep -Seconds 2
'@
    $paths = [ordered]@{
        'start-process (ShellExecuteEx)'   = { Start-Process 'preferis-probe://start-process' }
        'cmd /c start'                     = { cmd.exe /c start '""' 'preferis-probe://cmd-start' }
        'rundll32 url.dll'                 = { rundll32.exe url.dll,FileProtocolHandler 'preferis-probe://rundll32' }
        'explorer.exe <url>'               = { explorer.exe 'preferis-probe://explorer' }
        'Windows.System.Launcher (WinRT)'  = { powershell.exe -NoProfile -Command $winrt }
    }
    foreach ($name in $paths.Keys) {
        Write-Host "-> $name"
        & $paths[$name]
        Start-Sleep -Milliseconds 1500
    }
}
finally {
    & $exe --unregister | Out-Host
}

$stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
try {
    $stream.Position = $from
    (New-Object IO.StreamReader($stream)).ReadToEnd()
}
finally {
    $stream.Dispose()
}
