[CmdletBinding()]
param(
	[string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
	[string]$OutputDir = "",
	[string]$BrowserExe = "",
	[switch]$NoBuild,
	[switch]$KeepWorkingFiles
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
	$OutputDir = Join-Path $WorkspaceRoot "docs/screenshots/aiunit-repl"
}

function Resolve-BrowserExe {
	param([string]$Requested)

	if (-not [string]::IsNullOrWhiteSpace($Requested) -and (Test-Path -LiteralPath $Requested)) {
		return (Resolve-Path -LiteralPath $Requested).Path
	}

	$candidates = @(
		"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
		"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
		"C:\Program Files\Google\Chrome\Application\chrome.exe",
		"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
	)

	foreach ($candidate in $candidates) {
		if (Test-Path -LiteralPath $candidate) {
			return (Resolve-Path -LiteralPath $candidate).Path
		}
	}

	foreach ($commandName in @("msedge", "chrome", "chromium")) {
		$command = Get-Command $commandName -ErrorAction SilentlyContinue
		if ($null -ne $command) {
			return $command.Source
		}
	}

	throw "A Chromium-compatible browser was not found. Pass -BrowserExe with a valid browser executable."
}

function Write-Utf8File {
	param(
		[string]$Path,
		[string]$Content
	)

	$directory = Split-Path -Parent $Path
	New-Item -ItemType Directory -Force -Path $directory | Out-Null
	Set-Content -LiteralPath $Path -Value $Content -Encoding utf8NoBOM
}

function New-FixtureProject {
	param(
		[string]$FixtureRoot,
		[string]$RelativeProjectPath,
		[string]$AssemblyName,
		[string]$Config
	)

	$projectPath = Join-Path $FixtureRoot $RelativeProjectPath
	$project = @"
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
		<AssemblyName>$AssemblyName</AssemblyName>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="SharpNinja.aiUnit" Version="0.5.0-beta" />
	</ItemGroup>
</Project>
"@
	Write-Utf8File -Path $projectPath -Content $project

	if (-not [string]::IsNullOrWhiteSpace($Config)) {
		$configPath = Join-Path (Split-Path -Parent $projectPath) "appsettings.aiunit.json"
		Write-Utf8File -Path $configPath -Content $Config
	}
}

function New-StrategyConfig {
	param(
		[string]$ActiveStrategy,
		[string]$StrategiesJson
	)

	return @"
{
	"AiUnit": {
		"ActiveStrategy": "$ActiveStrategy",
		"Strategies": {
$StrategiesJson
		}
	}
}
"@
}

function ConvertTo-TerminalHtml {
	param(
		[string]$Title,
		[string]$Text
	)

	$encodedTitle = [System.Net.WebUtility]::HtmlEncode($Title)
	$encodedText = [System.Net.WebUtility]::HtmlEncode($Text.TrimEnd())
	return @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=1200,height=760,initial-scale=1">
<title>$encodedTitle</title>
<style>
html, body {
	margin: 0;
	width: 1200px;
	height: 760px;
	overflow: hidden;
	background: #101418;
}
body {
	box-sizing: border-box;
	padding: 20px 24px;
	color: #e6edf3;
	font: 16px/20px Consolas, "Cascadia Mono", "Courier New", monospace;
}
pre {
	box-sizing: border-box;
	margin: 0 auto;
	width: 120ch;
	height: 720px;
	overflow: hidden;
	white-space: pre;
	color: #e6edf3;
	text-shadow: 0 0 0 #e6edf3;
	/* Use ch units + the exact monospace font metrics so that 120 "columns"
	   are pixel-consistent across lines. This prevents column drift that
	   makes box-drawing characters and text look "all over the place"
	   in the browser screenshot compared to the ideal wireframe grid. */
	font-variant-ligatures: none;
}
</style>
</head>
<body>
<pre>$encodedText</pre>
</body>
</html>
"@
}

$browserExePath = Resolve-BrowserExe -Requested $BrowserExe

$projectPath = Join-Path $WorkspaceRoot "src/SharpNinja.AiUnit.Repl/SharpNinja.AiUnit.Repl.csproj"
$workingRoot = Join-Path $WorkspaceRoot "artifacts/aiunit-repl-screenshots"
$fixtureRoot = Join-Path $workingRoot "fixture"
$htmlDir = Join-Path $workingRoot "html"
$browserProfileDir = Join-Path $workingRoot "browser-profile"

if (Test-Path -LiteralPath $workingRoot) {
	Remove-Item -LiteralPath $workingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $fixtureRoot, $htmlDir, $OutputDir | Out-Null

$codexStrategy = @'
			"codex": {
				"Kind": "cli",
				"Command": "codex",
				"Model": "gpt-5.5",
				"TimeoutSeconds": 1800
			}
'@

$claudeStrategy = @'
			"claude": {
				"Kind": "cli",
				"Command": "claude",
				"Model": "sonnet",
				"TimeoutSeconds": 900
			}
'@

$openAiStrategy = @'
			"openai": {
				"Kind": "openai-compatible",
				"BaseUrl": "https://api.openai.com/v1",
				"Model": "gpt-5",
				"ApiKeyEnvVar": "OPENAI_API_KEY",
				"TimeoutSeconds": 900
			}
'@

$grokStrategy = @'
			"grok": {
				"Kind": "openai-compatible",
				"BaseUrl": "https://api.x.ai",
				"Model": "grok-4",
				"ApiKeyEnvVar": "XAI_API_KEY",
				"TimeoutSeconds": 1800
			}
'@

New-FixtureProject `
	-FixtureRoot $fixtureRoot `
	-RelativeProjectPath "projects/01-SharpNinja.AiUnit.Tests/SharpNinja.AiUnit.Tests.csproj" `
	-AssemblyName "SharpNinja.AiUnit.Tests" `
	-Config (New-StrategyConfig -ActiveStrategy "codex" -StrategiesJson "$codexStrategy,`n$claudeStrategy,`n$grokStrategy")

New-FixtureProject `
	-FixtureRoot $fixtureRoot `
	-RelativeProjectPath "projects/02-RiskyStars.Tests/RiskyStars.Tests.csproj" `
	-AssemblyName "RiskyStars.Tests" `
	-Config (New-StrategyConfig -ActiveStrategy "codex" -StrategiesJson "$codexStrategy,`n$openAiStrategy")

New-FixtureProject `
	-FixtureRoot $fixtureRoot `
	-RelativeProjectPath "projects/03-TruckMate.AgentTests/TruckMate.AgentTests.csproj" `
	-AssemblyName "TruckMate.AgentTests" `
	-Config (New-StrategyConfig -ActiveStrategy "openai" -StrategiesJson "$openAiStrategy,`n$grokStrategy")

New-FixtureProject `
	-FixtureRoot $fixtureRoot `
	-RelativeProjectPath "projects/04-Sample.Consumer.Tests/Sample.Consumer.Tests.csproj" `
	-AssemblyName "Sample.Consumer.Tests" `
	-Config ""

if (-not $NoBuild) {
	dotnet build $projectPath | Write-Host
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet build failed for $projectPath."
	}
}

$screens = @(
	@{ Id = "overview"; FileName = "01-workspace-overview" },
	@{ Id = "projects"; FileName = "02-project-strategy-editor" },
	@{ Id = "catalog"; FileName = "03-strategy-catalog" },
	@{ Id = "validate"; FileName = "04-validation-deploy" }
)

foreach ($screen in $screens) {
	$runArgs = @("run", "--project", $projectPath, "--no-build", "--", "tui", $screen.Id, "--workspace", $fixtureRoot)
	$outputLines = & dotnet @runArgs
	if ($LASTEXITCODE -ne 0) {
		throw "TUI render failed for screen '$($screen.Id)'."
	}

	$text = $outputLines -join [Environment]::NewLine
	$html = ConvertTo-TerminalHtml -Title $screen.FileName -Text $text
	Write-Utf8File -Path (Join-Path $htmlDir "$($screen.FileName).html") -Content $html
}

foreach ($screen in $screens) {
	$htmlPath = Join-Path $htmlDir "$($screen.FileName).html"
	$outputPath = Join-Path $OutputDir "$($screen.FileName).png"
	$htmlUri = ([System.Uri](Resolve-Path -LiteralPath $htmlPath).Path).AbsoluteUri
	$browserArgs = @(
		"--headless=new",
		"--disable-gpu",
		"--hide-scrollbars",
		"--window-size=1200,760",
		"--user-data-dir=$browserProfileDir",
		"--screenshot=$outputPath",
		$htmlUri
	)
	& $browserExePath @browserArgs | Out-Host
	if ($LASTEXITCODE -ne 0) {
		throw "Browser screenshot capture failed for $htmlPath."
	}

	if (-not (Test-Path -LiteralPath $outputPath)) {
		throw "Browser screenshot output was not created: $outputPath."
	}

	Write-Host $outputPath
}

$wireframeOutputDir = Join-Path $OutputDir "wireframes"
New-Item -ItemType Directory -Force -Path $wireframeOutputDir | Out-Null

foreach ($screen in $screens) {
	$svgPath = Join-Path $WorkspaceRoot "docs/wireframes/aiunit-repl/$($screen.FileName).svg"
	$outputPath = Join-Path $wireframeOutputDir "$($screen.FileName).png"
	$svgUri = ([System.Uri](Resolve-Path -LiteralPath $svgPath).Path).AbsoluteUri
	$browserArgs = @(
		"--headless=new",
		"--disable-gpu",
		"--hide-scrollbars",
		"--window-size=1200,760",
		"--user-data-dir=$browserProfileDir",
		"--screenshot=$outputPath",
		$svgUri
	)
	& $browserExePath @browserArgs | Out-Host
	if ($LASTEXITCODE -ne 0) {
		throw "Browser wireframe capture failed for $svgPath."
	}

	if (-not (Test-Path -LiteralPath $outputPath)) {
		throw "Browser wireframe output was not created: $outputPath."
	}

	Write-Host $outputPath
}

if (-not $KeepWorkingFiles -and (Test-Path -LiteralPath $workingRoot)) {
	Remove-Item -LiteralPath $workingRoot -Recurse -Force
}
