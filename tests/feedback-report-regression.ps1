param([string]$Repo = (Split-Path -Parent $PSScriptRoot))
dotnet run --project (Join-Path $Repo 'tests/MouseDock.ProtocolTests') -c Release -- --verify-feedback-report
exit $LASTEXITCODE
