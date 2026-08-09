param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publishPath = [IO.Path]::GetFullPath($PublishDirectory)
$executablePath = Join-Path $publishPath 'TalesAlarm.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw 'TalesAlarm.exe가 없습니다.'
}

$executable = Get-Item -LiteralPath $executablePath
if ($executable.Length -le 0) {
    throw 'TalesAlarm.exe가 비어 있습니다.'
}

$forbiddenFiles = @(Get-ChildItem -LiteralPath $publishPath -File | Where-Object {
    $_.Name -like '*.dll' -or
    $_.Name -like '*.deps.json' -or
    $_.Name -like '*.runtimeconfig.json' -or
    $_.Name -like '*.wav' -or
    $_.Name -like '*.ico'
})
if ($forbiddenFiles.Count -gt 0) {
    $names = ($forbiddenFiles.Name | Sort-Object) -join ', '
    throw "단일 파일 실행에 불필요한 외부 파일이 있습니다: $names"
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryBase ('TalesAlarm-publish-check-' + [Guid]::NewGuid().ToString('N'))
$previousLocalAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA', 'Process')
$process = $null
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    [Environment]::SetEnvironmentVariable('LOCALAPPDATA', $temporaryRoot, 'Process')
    $process = Start-Process -FilePath $executablePath -WindowStyle Hidden -PassThru
    if ($process.WaitForExit(3000)) {
        throw "TalesAlarm.exe가 3초 전에 종료되었습니다. 종료 코드: $($process.ExitCode)"
    }

    $process.Refresh()
    if ($process.HasExited) {
        throw 'TalesAlarm.exe 프로세스가 실행 상태를 유지하지 못했습니다.'
    }
}
finally {
    if ($null -ne $process) {
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit(5000) | Out-Null
            }
        }
        catch {
        }
    }

    [Environment]::SetEnvironmentVariable('LOCALAPPDATA', $previousLocalAppData, 'Process')
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $resolvedTemporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedTemporaryRoot -eq $temporaryBase) {
        throw "안전하지 않은 임시 폴더 정리 경로입니다: $resolvedTemporaryRoot"
    }

    if (Test-Path -LiteralPath $resolvedTemporaryRoot) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

Write-Output "게시 산출물 검증 통과: $executablePath ($($executable.Length) bytes)"
