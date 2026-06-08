[CmdletBinding()]
Param(
    [Parameter(Position=0,Mandatory=$false,ValueFromRemainingArguments=$true)]
    [string[]]$BuildArguments
)

Write-Output "PowerShell $($PSVersionTable.PSEdition) version $($PSVersionTable.PSVersion)"

Set-StrictMode -Version 2.0; $ErrorActionPreference = "Stop"; $ConfirmPreference = "None"; trap { Write-Error $_ -ErrorAction Continue; exit 1 }
$PSScriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent

###########################################################################
# CONFIGURATION
###########################################################################

$BuildProjectFile = "$PSScriptRoot\_build\_build.csproj"
$TempDirectory = "$PSScriptRoot\.nuke\temp"

$DotNetGlobalFile = "$PSScriptRoot\global.json"
$DotNetInstallUrl = "https://dot.net/v1/dotnet-install.ps1"
$DotNetChannel = "Current"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_NOLOGO = 1

###########################################################################
# EXECUTION
###########################################################################

function ExecSafe([scriptblock] $cmd) {
    $global:LASTEXITCODE = 0
    & $cmd
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

function Get-RequestedDotNetSdkVersion() {
    if (-not (Test-Path $DotNetGlobalFile)) {
        return $null
    }

    $dotNetGlobal = Get-Content $DotNetGlobalFile | ConvertFrom-Json
    if ($dotNetGlobal.PSObject.Properties.Name -notcontains 'sdk') {
        return $null
    }

    return $dotNetGlobal.sdk.version
}

function Test-DotNetSdkInstalled([string] $dotNetExe, [string] $requestedVersion) {
    if ([string]::IsNullOrWhiteSpace($requestedVersion)) {
        return $true
    }

    $global:LASTEXITCODE = 0
    $installedSdks = & $dotNetExe --list-sdks 2>$null
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode) {
        return $false
    }

    foreach ($installedSdk in $installedSdks) {
        $installedVersion = ($installedSdk -split '\s+')[0]
        if ($installedVersion -eq $requestedVersion) {
            return $true
        }
    }

    return $false
}

$RequestedDotNetSdkVersion = Get-RequestedDotNetSdkVersion

# If dotnet CLI is installed globally and it matches requested version, use for execution
$dotNetCommand = Get-Command "dotnet" -ErrorAction SilentlyContinue
if ($null -ne $dotNetCommand -and (Test-DotNetSdkInstalled $dotNetCommand.Path $RequestedDotNetSdkVersion)) {
    $env:DOTNET_EXE = $dotNetCommand.Path
}
else {
    # Download install script
    $DotNetInstallFile = "$TempDirectory\dotnet-install.ps1"
    New-Item -ItemType Directory -Path $TempDirectory -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    (New-Object System.Net.WebClient).DownloadFile($DotNetInstallUrl, $DotNetInstallFile)

    # If global.json exists, load requested SDK version
    if (-not [string]::IsNullOrWhiteSpace($RequestedDotNetSdkVersion)) {
        $DotNetChannel = $RequestedDotNetSdkVersion
    }

    # Install .NET SDK and add to PATH
    ExecSafe { & $DotNetInstallFile -InstallDir $TempDirectory\dotnet -Version $DotNetChannel -NoPath }
    $env:DOTNET_EXE = "$TempDirectory\dotnet\dotnet.exe"
}

Write-Output "Microsoft (R) .NET SDK version $(& $env:DOTNET_EXE --version)"

ExecSafe { & $env:DOTNET_EXE build $BuildProjectFile /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet }
ExecSafe { & $env:DOTNET_EXE run --project $BuildProjectFile --no-build -- $BuildArguments }
