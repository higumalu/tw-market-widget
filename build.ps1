<#
.SYNOPSIS
    建置台股即時報價小工具。

.DESCRIPTION
    檢查 .NET SDK、建置專案，並可選擇直接啟動、建立桌面捷徑，
    或產生一份不必安裝 .NET Runtime 就能跑的資料夾。

.EXAMPLE
    .\build.ps1
    建置 Release 版，印出執行檔位置。

.EXAMPLE
    .\build.ps1 -Run -Shortcut
    建置後直接啟動，並在桌面放一個捷徑。

.EXAMPLE
    .\build.ps1 -Publish
    產生 dist\，整個資料夾複製到沒裝 .NET 的電腦也能跑。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # 產生不需安裝 .NET Runtime 的 dist\
    [switch]$Publish,

    # 建置完成後直接啟動
    [switch]$Run,

    # 在桌面建立捷徑
    [switch]$Shortcut
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'TwMarketWidget.csproj'
$sdkUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0'

function Write-Step($message) { Write-Host "`n==> $message" -ForegroundColor Cyan }
function Write-Ok($message) { Write-Host "    $message" -ForegroundColor Green }
function Write-Warn($message) { Write-Host "    $message" -ForegroundColor Yellow }

# dotnet 常常裝好了卻不在 PATH 上（安裝完沒重開終端機就會這樣），所以多找幾個標準位置。
function Find-Dotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    return $null
}

Write-Step '檢查 .NET SDK'

$dotnet = Find-Dotnet
if (-not $dotnet) {
    throw "找不到 dotnet。請先安裝 .NET 8 SDK（要含 Windows Desktop）：$sdkUrl"
}
Write-Ok "dotnet：$dotnet"

$sdkVersions = @(& $dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] })
$majors = @($sdkVersions | ForEach-Object { [int](($_ -split '\.')[0]) })
if (-not ($majors | Where-Object { $_ -ge 8 })) {
    throw "需要 .NET 8 以上的 SDK，目前只有：$($sdkVersions -join ', ')。請安裝：$sdkUrl"
}
Write-Ok "SDK：$($sdkVersions -join ', ')"

$desktopRuntimes = @(& $dotnet --list-runtimes | Where-Object { $_ -like 'Microsoft.WindowsDesktop.App*' })
if ($desktopRuntimes.Count -eq 0) {
    throw "這是 WPF 程式，需要含 Windows Desktop 的 SDK／Runtime。請重新安裝：$sdkUrl"
}
Write-Ok "Windows Desktop Runtime：$(($desktopRuntimes | ForEach-Object { ($_ -split ' ')[1] }) -join ', ')"

Write-Step "建置（$Configuration）"
& $dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "建置失敗（結束碼 $LASTEXITCODE）" }

$exe = Join-Path $root "bin\$Configuration\net8.0-windows\TwMarketWidget.exe"
if (-not (Test-Path $exe)) { throw "建置完成卻找不到執行檔：$exe" }
Write-Ok "執行檔：$exe"

if ($Publish) {
    Write-Step '打包成免安裝 Runtime 的版本'
    $dist = Join-Path $root 'dist'
    & $dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=embedded -o $dist --nologo
    if ($LASTEXITCODE -ne 0) { throw "打包失敗（結束碼 $LASTEXITCODE）" }

    $exe = Join-Path $dist 'TwMarketWidget.exe'
    Write-Ok "已輸出到：$dist"
    Write-Warn 'WPF 有幾個原生 DLL 併不進單一檔案，複製時整個資料夾一起帶走。'
}

if ($Shortcut) {
    Write-Step '建立桌面捷徑'
    $linkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '台股即時報價.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $exe
    $link.WorkingDirectory = Split-Path $exe -Parent
    $link.Description = '台股即時報價小工具'
    $link.Save()
    Write-Ok "捷徑：$linkPath"
}

Write-Host ''
if ($Run) {
    Write-Step '啟動'
    Start-Process -FilePath $exe
    Write-Ok '已啟動。第一次開會停在完整模式，右上角「精簡」可以切成窄長條。'
} else {
    Write-Host '完成。直接執行：' -ForegroundColor Green
    Write-Host "    $exe"
    Write-Host ''
    Write-Host '其他用法：' -ForegroundColor DarkGray
    Write-Host '    .\build.ps1 -Run -Shortcut   建置後啟動並放桌面捷徑' -ForegroundColor DarkGray
    Write-Host '    .\build.ps1 -Publish         產生免安裝 Runtime 的 dist\' -ForegroundColor DarkGray
    Write-Host '    .\build.ps1 -Configuration Debug' -ForegroundColor DarkGray
}
