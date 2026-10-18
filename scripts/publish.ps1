# CapsSwitch 发布脚本:产出两个版本
#   dist\CapsSwitch.App.exe                    独立版(self-contained 单文件,免运行时)
#   dist\framework-dependent\CapsSwitch.App.exe 框架依赖版(单文件,需目标机器装 .NET 10 桌面运行时)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$root\CapsSwitch.sln")) { $root = $PSScriptRoot }
Remove-Item "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue

"=== 1/2 独立版 (self-contained) ==="
dotnet publish "$root\CapsSwitch.App" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\dist"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

"=== 2/2 框架依赖版 (framework-dependent) ==="
dotnet publish "$root\CapsSwitch.App" -c Release -r win-x64 --no-self-contained `
    -p:PublishSingleFile=true `
    -o "$root\dist\framework-dependent"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

"=== 产物 ==="
Get-Item "$root\dist\CapsSwitch.App.exe", "$root\dist\framework-dependent\CapsSwitch.App.exe" |
    Select-Object FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
