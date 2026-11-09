param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$AssemblyPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$dll=if ($AssemblyPath) { (Resolve-Path -LiteralPath $AssemblyPath).Path } else { Join-Path $repo "bin\$Configuration\MEP_Radial_Routing_ver1.dll" }
if (!(Test-Path -LiteralPath $dll)) { throw 'Build the selected configuration first.' }
$destination=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2023'
New-Item -ItemType Directory -Force $destination | Out-Null
$manifest=[xml](Get-Content -LiteralPath (Join-Path $repo 'MEP_Radial_Routing_ver1.addin') -Raw)
$manifest.RevitAddIns.AddIn.Assembly=$dll
$manifest.Save((Join-Path $destination 'MEP_Radial_Routing_ver1.addin'))
Write-Output "Installed manifest referencing $dll. Restart Revit 2023."
