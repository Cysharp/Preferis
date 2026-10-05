# Run against an English settings window in a disposable Windows test environment.
# The configuration must contain at least three uniquely named profiles and rules.
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
$failures = [Collections.Generic.List[string]]::new()
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')

function Invoke-Ui([string[]]$Arguments) {
    $output = @(& winapp ui @Arguments -w $testWindow)
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Arguments failed: $output" }
    $output
}

function Get-Elements($Nodes) {
    foreach ($node in $Nodes) {
        $node
        Get-Elements $node.children
    }
}

function Get-Rows {
    $tree = Invoke-Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    $list = Get-Elements $tree.windows.elements |
        Where-Object { $_.type -eq 'List' -and $_.className -eq 'ListView' } |
        Select-Object -First 1
    if (-not $list) { throw 'The page list was not found.' }
    @(Get-Elements $list.children | Where-Object type -eq 'ListItem')
}

function Get-Selector([string]$Type, [string]$Name) {
    $tree = Invoke-Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    $matches = @(Get-Elements $tree.windows.elements |
        Where-Object { $_.type -eq $Type -and $_.name -ceq $Name })
    if ($matches.Count -ne 1) { throw "Expected one $Type named '$Name'; found $($matches.Count)." }
    $matches[0].selector
}

function Assert-Order([string]$Collection, [string[]]$Expected) {
    # Auto-save runs after 600 ms of inactivity.
    Start-Sleep -Milliseconds 1000
    $saved = (Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json).$Collection.name
    $visible = @(Get-Rows | ForEach-Object {
        (Get-Elements $_.children | Where-Object type -eq 'Text' | Select-Object -First 1).name
    })
    if (($visible -join '|') -cne ($Expected -join '|')) {
        throw "Visible order: $($visible -join ', '); expected $($Expected -join ', ')."
    }
    if (($saved -join '|') -cne ($Expected -join '|')) {
        throw "Saved order: $($saved -join ', '); expected $($Expected -join ', ')."
    }
}

function Test-Reordering([string]$Page, [string]$Collection) {
    Invoke-Ui @('invoke', (Get-Selector 'ListItem' $Page)) | Out-Null
    $initial = @((Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json).$Collection.name)
    if ($initial.Count -lt 3 -or @($initial | Select-Object -Unique).Count -ne $initial.Count) {
        throw 'Use a fixture with at least three uniquely named items.'
    }
    $rows = @(Get-Rows)
    # Also cover dragging an item other than the one currently shown in the editor.
    Invoke-Ui @('invoke', $rows[1].selector) | Out-Null
    $last = $rows[-1]
    $destination = '{0},{1}' -f [int]($last.x + $last.width / 2), [int]($last.y + $last.height - 3)
    Invoke-Ui @('drag', $rows[0].selector, $destination, '--hold-ms', '300', '--dwell-ms', '400') | Out-Null
    $expected = @($initial[1..($initial.Count - 1)]) + @($initial[0])
    Assert-Order $Collection $expected

    # The moved item must remain selected and editable, including after the native remove/insert notifications.
    $name = Invoke-Ui @('get-value', (Get-Selector 'Edit' 'Name'), '--json') | ConvertFrom-Json
    if ($name.text -cne $initial[0]) { throw "The dragged item lost its editor selection: $($name.text)." }

    $rows = @(Get-Rows)
    $first = $rows[0]
    $destination = '{0},{1}' -f [int]($first.x + $first.width / 2), [int]($first.y + 3)
    Invoke-Ui @('drag', $rows[-1].selector, $destination, '--hold-ms', '300', '--dwell-ms', '400') | Out-Null
    Assert-Order $Collection $initial

    # Dropping at the same position must not reorder the configuration.
    $rows = @(Get-Rows)
    Invoke-Ui @('drag', $rows[0].selector, $rows[0].selector, '--hold-ms', '300') | Out-Null
    Assert-Order $Collection $initial

    foreach ($command in @('Move Down', 'Move Up')) {
        Invoke-Ui @('invoke', (Get-Selector 'Button' 'More')) | Out-Null
        Invoke-Ui @('invoke', (Get-Selector 'MenuItem' $command)) | Out-Null
        $expected = if ($command -eq 'Move Down') {
            @($initial[1], $initial[0]) + @($initial[2..($initial.Count - 1)])
        } else { $initial }
        Assert-Order $Collection $expected
    }

    # Recreating the page must read the same order from the view model.
    Invoke-Ui @('invoke', (Get-Selector 'ListItem' 'General')) | Out-Null
    Invoke-Ui @('invoke', (Get-Selector 'ListItem' $Page)) | Out-Null
    Assert-Order $Collection $initial
}

try {
    $windows = & winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Could not find the settings window.' }
    $main = @($windows | Where-Object className -eq 'WinUIDesktopWin32WindowClass')
    if ($main.Count -ne 1) { throw 'Expected exactly one settings window.' }
    $testWindow = [string]$main[0].hwnd
    foreach ($page in @('Profiles', 'Rules')) {
        try {
            Test-Reordering $page $page.ToLowerInvariant()
            Write-Output "PASS: $page drag down/up, same-position drop, selection, menu down/up and page recreation"
        } catch {
            $failures.Add("${page}: $_")
            Write-Output "FAIL: $page - $_"
        }
    }
} finally {
    try { & winapp ui yield | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId }
}

if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
