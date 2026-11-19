# Publishes the app and its browser host and builds the installer: windows\publish\FlyyLink and windows\publish\FlyyLink-Setup-<version>.exe.
# Run from the windows folder: .\scripts\package.ps1 -Version 1.2.3
param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

# LICENCE_PUBLIC_KEY turns on the licence check; without it the build is unlimited.
# UPDATE_PUBLIC_KEY turns on updates; without it the build doesn't update itself.
dotnet publish src/FlyyLink -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:Version=$Version "-p:LicencePublicKey=$env:LICENCE_PUBLIC_KEY" "-p:UpdatePublicKey=$env:UPDATE_PUBLIC_KEY" -o publish/FlyyLink
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The browser add-on's host, as one self-contained file next to FlyyLink.exe.
dotnet publish src/FlyyLink.BrowserHost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:Version=$Version -o publish/FlyyLink
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    choco install innosetup --no-progress -y
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $iscc /Qp "/DAppVersion=$Version" installer\FlyyLink.iss
exit $LASTEXITCODE
