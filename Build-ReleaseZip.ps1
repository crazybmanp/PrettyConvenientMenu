param (
    [string]$Configuration = "Release"
)

$ProjectName = "PrettyConvenientMenu"
$ReleaseName = "APrettyConvenientMenu"
$ModFolderName = "CRZBP.APrettyConvenientMenu"

# 1. Auto-detect the version from Info.json so the zip is always named correctly
$InfoFile = "$PSScriptRoot\Info.json"
if (Test-Path $InfoFile) {
    $InfoJson = Get-Content -Path $InfoFile -Raw | ConvertFrom-Json
    $Version = $InfoJson.version
} else {
    $Version = "Unknown"
}

$ZipName = "${ReleaseName}_v${Version}.zip"
$BuildsDir = "$PSScriptRoot\builds"
$ZipPath = "$BuildsDir\$ZipName"
$OutputDir = "$PSScriptRoot\bin\ReleasePackage"
$PluginFolder = "$OutputDir\$ModFolderName"

Write-Host "Starting packaging process for $ProjectName v$Version..." -ForegroundColor Cyan

# 0. Check if it already exists before building
if (-not (Test-Path $BuildsDir)) {
    New-Item -ItemType Directory -Path $BuildsDir | Out-Null
}
if (Test-Path $ZipPath) {
    Write-Host "ERROR: $ZipName already exists in the builds folder!" -ForegroundColor Red
    Write-Host "Packaging aborted. You must manually delete the old zip or bump the version in Info.json." -ForegroundColor Red
    exit 1
}

# 2. Run unit tests
Write-Host "Running unit tests in $Configuration mode..." -ForegroundColor Cyan
dotnet test "$PSScriptRoot\PrettyConvenientMenu.slnx" -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed! Aborting packaging." -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Compile the project
Write-Host "Building project in $Configuration mode..." -ForegroundColor Cyan
dotnet build -c $Configuration "$PSScriptRoot\src\${ProjectName}.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Aborting packaging." -ForegroundColor Red
    exit $LASTEXITCODE
}

# 4. Clean and prepare the temporary staging folder
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $PluginFolder | Out-Null

# 5. Copy all required files into the staging folder
Write-Host "Copying files to staging directory..."
Copy-Item -Path "$PSScriptRoot\Info.json" -Destination $PluginFolder
if (Test-Path "$PSScriptRoot\README.md") { Copy-Item -Path "$PSScriptRoot\README.md" -Destination $PluginFolder }
if (Test-Path "$PSScriptRoot\CHANGELOG.md") { Copy-Item -Path "$PSScriptRoot\CHANGELOG.md" -Destination $PluginFolder }
if (Test-Path "$PSScriptRoot\LICENSE") { Copy-Item -Path "$PSScriptRoot\LICENSE" -Destination $PluginFolder }
if (Test-Path "$PSScriptRoot\THIRD-PARTY-NOTICES.md") { Copy-Item -Path "$PSScriptRoot\THIRD-PARTY-NOTICES.md" -Destination $PluginFolder }

Copy-Item -Path "$PSScriptRoot\Assemblies\${ProjectName}.dll" -Destination $PluginFolder
if (Test-Path "$PSScriptRoot\Assemblies\${ProjectName}.pdb") {
    Copy-Item -Path "$PSScriptRoot\Assemblies\${ProjectName}.pdb" -Destination $PluginFolder
}
if (Test-Path "$PSScriptRoot\Assemblies\0Harmony.dll") {
    Copy-Item -Path "$PSScriptRoot\Assemblies\0Harmony.dll" -Destination $PluginFolder
}

# 6. Compress into a Zip archive
Write-Host "Zipping package into builds directory..."
# By pointing to the folder itself, the zip will contain a root "PrettyConvenientMenu" folder.
Compress-Archive -Path $PluginFolder -DestinationPath $ZipPath

# 7. Cleanup
Remove-Item -Recurse -Force $OutputDir

Write-Host "Success! Created package: $ZipName" -ForegroundColor Green
Write-Host "Package location: $ZipPath" -ForegroundColor Green
