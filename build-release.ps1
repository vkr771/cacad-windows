<#
  Builds the release exes for GitHub Releases.

    .\build-release.ps1 1.3.0

  Makes dist\v1.3.0\ with:
    cacad-x64.exe     single-file, self-contained (no .NET install needed)
    cacad-arm64.exe
    SHA256SUMS.txt    the updater checks downloads against this

  Then create a GitHub release tagged v1.3.0 and attach all three files. Installed copies of cacad
  find it within a few hours and update themselves.
#>
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.3.0" }
$root = $PSScriptRoot
$proj = Join-Path $root "src\Cacad.App\Cacad.App.csproj"
$out = Join-Path $root "dist\v$Version"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($rid in "win-x64", "win-arm64") {
    $arch = $rid.Substring(4)
    $pub = Join-Path $root "dist\.publish\$rid"
    Write-Host "Publishing $rid..."
    dotnet publish $proj -c Release -r $rid -o $pub -p:Version=$Version -p:FileVersion=$Version.0
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
    Copy-Item (Join-Path $pub "cacad.exe") (Join-Path $out "cacad-$arch.exe") -Force
}

$sums = foreach ($f in "cacad-x64.exe", "cacad-arm64.exe") {
    $h = (Get-FileHash (Join-Path $out $f) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$h  $f"
}
[IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), ($sums -join "`n") + "`n")
Remove-Item (Join-Path $root "dist\.publish") -Recurse -Force

Write-Host ""
Write-Host "Done: $out"
Get-ChildItem $out | Format-Table Name, Length
