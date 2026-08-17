$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "..\.tools\dotnet\dotnet.exe"
$solution = Join-Path $PSScriptRoot "WitchDrawer.sln"
$nugetConfig = Join-Path $PSScriptRoot "NuGet.Config"
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE ".nuget\packages"

if (!(Test-Path -LiteralPath $dotnet)) {
  throw "Local .NET SDK not found at $dotnet"
}
if (!(Test-Path -LiteralPath $nugetConfig)) {
  throw "Project NuGet.Config not found at $nugetConfig"
}

& $dotnet restore $solution --configfile $nugetConfig --ignore-failed-sources
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

& $dotnet run --project "$PSScriptRoot\src\WitchDrawer.App\WitchDrawer.App.csproj" --configuration Debug --no-restore
exit $LASTEXITCODE
