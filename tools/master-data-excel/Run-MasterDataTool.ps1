param(
    [ValidateSet("export", "check", "import")]
    [string]$Mode = "export"
)

$ErrorActionPreference = "Stop"
$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $toolDir "..\..")
$bundledNode = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
$exportMarker = Join-Path $repoRoot "outputs\master-data-editor\export.ok"

if (Test-Path $bundledNode) {
    $node = $bundledNode
}
else {
    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if (-not $nodeCommand) {
        throw "Node.js が見つかりません。Codexのワークスペース依存関係を読み込んでから実行してください。"
    }
    $node = $nodeCommand.Source
}

if ($Mode -eq "export" -and (Test-Path $exportMarker)) {
    Remove-Item -LiteralPath $exportMarker -Force
}

& $node (Join-Path $toolDir "master-data-tool.mjs") $Mode
$toolExitCode = $LASTEXITCODE
if ($Mode -eq "export" -and (Test-Path $exportMarker)) {
    exit 0
}
if ($toolExitCode -ne 0) {
    exit $toolExitCode
}
