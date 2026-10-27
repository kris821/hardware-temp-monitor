<#
    一键构建：编译 HardwareTemp.exe → 组装分发包 → 生成 zip 与 SHA256

    用法：
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.0.1

    依赖：Windows 10/11 自带的 .NET Framework 4.8 + csc.exe，不需要装 Visual Studio
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "csc.exe 不存在：$csc（需要 64 位 .NET Framework 4.x）"
}

$exe = Join-Path $root "HardwareTemp.exe"
$dll = Join-Path $root "LibreHardwareMonitorLib.dll"
$src = Join-Path $root "Program.cs"
$rel = Join-Path $root "release"

Write-Host "[1/4] 编译"
$argsList = @(
    "/nologo", "/target:winexe", "/codepage:65001", "/optimize+", "/platform:x64",
    "/out:`"$exe`"",
    "/win32manifest:`"$(Join-Path $root 'app.manifest')`"",
    "/win32icon:`"$(Join-Path $root 'app.ico')`"",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:`"$dll`"",
    "`"$src`""
)

$log = & $csc $argsList 2>&1
if ($LASTEXITCODE -ne 0) {
    $log | ForEach-Object { $_.ToString() }
    throw "编译失败"
}
"      OK  " + (Get-Item $exe).Length + " bytes"

Write-Host "[2/4] 组装分发包"
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
$stage = Join-Path $rel "硬件温度监控"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item $exe $stage
Copy-Item $dll $stage
Copy-Item (Join-Path $root "packaging\使用说明.txt") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $stage
"      " + (Get-ChildItem $stage).Count + " 个文件"

Write-Host "[3/4] 压缩"
$zip = Join-Path $rel ("HardwareTemp-v" + $Version + ".zip")
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
"      " + (Split-Path $zip -Leaf) + "  " + [math]::Round((Get-Item $zip).Length / 1KB) + " KB"

Write-Host "[4/4] 校验"
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"      SHA256 " + $hash
$hash | Out-File (Join-Path $rel "SHA256.txt") -Encoding ascii

Write-Host ""
Write-Host "完成：$zip"
