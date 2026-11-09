param([string]$MSBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$csc = Join-Path (Split-Path $MSBuild) 'Roslyn\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw 'Set -MSBuild to the MSBuild.exe installed with Visual Studio (C# workload).' }
New-Item -ItemType Directory -Force (Join-Path $repo 'bin\Tests') | Out-Null
$testExe = Join-Path $repo 'bin\Tests\GeometryTests.exe'
& $csc /nologo /langversion:7.3 /target:exe "/out:$testExe" (Join-Path $repo 'Core\Geometry.cs') (Join-Path $repo 'Tests\GeometryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
