<#
.SYNOPSIS
    키 입력이 어디서 막히는지 확인하는 실험을 순서대로 진행합니다.

.DESCRIPTION
    5가지 상황을 하나씩 자동으로 띄워 줍니다. 각 상황에서 할 일은 똑같습니다.

        1) 새로 뜬 "Foreground Antagonist" 창을 클릭한다
        2) F5 를 3번 누른다
        3) 이어서 Q 를 1번 누른다        <- 대조 키
        4) 이 창으로 돌아와 Enter 를 누른다

    Q 는 어떤 상황에서도 막히지 않는 대조 키입니다. Q 는 기록됐는데 F5 가 없으면
    "정말로 F5 가 막혔다"는 뜻이고, Q 조차 없으면 "키를 안 눌렀다"는 뜻이라
    자동으로 다시 하자고 안내합니다. 판정은 사람 귀가 아니라 프로브 로그로 합니다.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Run-InputExperiment.ps1
#>
[CmdletBinding()]
param(
    # 특정 번호부터 시작합니다. 예: -From 6 이면 6번부터만 진행합니다.
    [int] $From = 1
)

$ErrorActionPreference = 'Stop'

$probeExe = Join-Path $PSScriptRoot 'RawInputProbe\bin\Release\net10.0-windows\RawInputProbe.exe'
$antagonistExe = Join-Path $PSScriptRoot 'ForegroundAntagonist\bin\Release\net10.0-windows\ForegroundAntagonist.exe'

foreach ($path in @($probeExe, $antagonistExe)) {
    if (-not (Test-Path $path)) {
        Write-Host "실행 파일이 없습니다: $path" -ForegroundColor Red
        Write-Host "먼저 아래 두 명령으로 빌드하세요." -ForegroundColor Yellow
        Write-Host "  dotnet build tools/RawInputProbe/RawInputProbe.csproj -c Release"
        Write-Host "  dotnet build tools/ForegroundAntagonist/ForegroundAntagonist.csproj -c Release"
        exit 1
    }
}

$logDirectory = Join-Path $env:LOCALAPPDATA 'TalesAlarmProbe'
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

$steps = @(
    [pscustomobject]@{ Probe = 'sink';   Antagonist = 'none';     Label = '기준 확인 — 방해하는 프로그램 없음' }
    [pscustomobject]@{ Probe = 'sink';   Antagonist = 'nolegacy'; Label = '현재 방식 vs 키보드를 독점하는 프로그램' }
    [pscustomobject]@{ Probe = 'sink';   Antagonist = 'llhook';   Label = '현재 방식 vs F키를 가로채는 프로그램' }
    [pscustomobject]@{ Probe = 'exsink'; Antagonist = 'nolegacy'; Label = '보강 방식 vs 키보드를 독점하는 프로그램' }
    [pscustomobject]@{ Probe = 'exsink'; Antagonist = 'llhook';   Label = '보강 방식 vs F키를 가로채는 프로그램' }
    [pscustomobject]@{ Probe = 'poll';   Antagonist = 'none';     Label = '폴링 방식 기준 확인 — 방해하는 프로그램 없음' }
    [pscustomobject]@{ Probe = 'poll';   Antagonist = 'llhook';   Label = '폴링 방식 vs F키를 가로채는 프로그램' }
)

function Stop-Quietly {
    param($Process)

    if ($null -eq $Process) {
        return
    }

    try {
        Stop-Process -Id $Process.Id -Force -ErrorAction Stop
    }
    catch {
    }
}

$results = @()
$stepNumber = 0

Write-Host ''
Write-Host '키 입력이 어디서 막히는지 확인하는 실험을 시작합니다.' -ForegroundColor Cyan
Write-Host "총 $($steps.Count)번입니다. 서두르지 마세요 — 판정은 로그로 하므로 천천히 해도 됩니다."
Write-Host '중간에 그만두려면 Ctrl+C 를 누르세요.'
Write-Host ''

try {
    foreach ($step in $steps) {
        $stepNumber++
        if ($stepNumber -lt $From) {
            continue
        }

        $verdict = $null

        while ($null -eq $verdict) {
            $probe = $null
            $antagonist = $null

            Write-Host ('=' * 64)
            Write-Host "[$stepNumber/$($steps.Count)] $($step.Label)" -ForegroundColor Cyan
            Write-Host ('=' * 64)

            try {
                $startedAt = Get-Date
                $probe = Start-Process -FilePath $probeExe -ArgumentList '--mode', $step.Probe -PassThru
                Start-Sleep -Milliseconds 900
                $antagonist = Start-Process -FilePath $antagonistExe -ArgumentList '--mode', $step.Antagonist -PassThru
                Start-Sleep -Milliseconds 900

                Write-Host ''
                Write-Host '  1) 방금 뜬 "Foreground Antagonist" 창을 클릭하세요.'
                Write-Host '  2) F5 를 3번 누르세요.'
                Write-Host '  3) 이어서 Q 를 1번 누르세요.' -ForegroundColor Yellow
                Write-Host '  4) 이 창으로 돌아오세요.'
                Write-Host ''

                Read-Host '다 눌렀으면 Enter'
            }
            finally {
                Stop-Quietly $antagonist
                Stop-Quietly $probe
                Start-Sleep -Milliseconds 600
            }

            $log = Get-ChildItem (Join-Path $logDirectory "probe-$($step.Probe)-*.log") -ErrorAction SilentlyContinue |
                Where-Object { $_.LastWriteTime -ge $startedAt } |
                Sort-Object LastWriteTime |
                Select-Object -Last 1

            if ($null -eq $log) {
                Write-Host '프로브 로그를 찾지 못했습니다. 다시 시도합니다.' -ForegroundColor Yellow
                continue
            }

            $lines = Get-Content $log.FullName -Encoding UTF8

            # Raw Input 줄은 "flags=0x0000(down)", 폴링 줄은 "POLL vk=0x74(F5) down" 형태다.
            $downPattern = '(\(down\)| down$)'
            $functionKeyDowns = @($lines | Where-Object { $_ -match 'vk=0x74' -and $_ -match $downPattern }).Count
            $controlKeyDowns = @($lines | Where-Object { $_ -match 'vk=0x51' -and $_ -match $downPattern }).Count

            Write-Host ("  로그 판정: F5 키다운 {0}개, 대조키 Q {1}개" -f $functionKeyDowns, $controlKeyDowns)

            if ($controlKeyDowns -eq 0) {
                Write-Host '  대조키 Q 도 기록되지 않았습니다.' -ForegroundColor Yellow
                Write-Host '  키를 안 누른 것일 수도 있고, 이 조합에서는 모든 키가 차단된 것일 수도 있습니다.'
                $again = ''
                while ($again -notin @('r', 'b', 'n')) {
                    $again = (Read-Host '  r = 다시 하기 / b = 키는 눌렀다(모든 키 차단으로 기록) / n = 무효').Trim().ToLower()
                }

                if ($again -eq 'r') {
                    continue
                }

                if ($again -eq 'b') {
                    $verdict = '전부 차단'
                }
                else {
                    $verdict = '무효'
                }
            }
            elseif ($functionKeyDowns -gt 0) {
                $verdict = 'F5 도달'
            }
            else {
                $verdict = 'F5 차단'
            }

            $results += [pscustomobject]@{
                번호   = $stepNumber
                상황   = $step.Label
                프로브 = $step.Probe
                상대역 = $step.Antagonist
                F5     = $functionKeyDowns
                Q      = $controlKeyDowns
                판정   = $verdict
            }
        }
    }
}
finally {
    Get-Process -Name RawInputProbe, ForegroundAntagonist -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '결과 요약' -ForegroundColor Green
$results | Format-Table -AutoSize

$summaryPath = Join-Path $logDirectory 'experiment-summary.txt'
$results | Format-Table -AutoSize | Out-File -FilePath $summaryPath -Encoding utf8
Write-Host "요약을 저장했습니다: $summaryPath"
Write-Host '끝났다고 알려주시면 제가 로그까지 같이 읽고 판별하겠습니다.'
