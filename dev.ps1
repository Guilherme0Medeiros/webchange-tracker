param([switch]$SchedulerOnly, [switch]$Once, [string]$ApiUrl = 'https://localhost:7010')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$root = $PSScriptRoot
$apiUrl = $ApiUrl.TrimEnd('/')
$token = [Environment]::GetEnvironmentVariable('InternalJobs__Token')
if ([string]::IsNullOrWhiteSpace($token)) {
    $entry = Get-Content -LiteralPath (Join-Path $root '.env') -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '^\s*InternalJobs__Token\s*=' } | Select-Object -Last 1
    if ($entry) { $token = ($entry -split '=', 2)[1].Trim().Trim('"', "'") }
}
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Configure InternalJobs__Token no ambiente ou no .env antes de iniciar.'
}

function Stop-ProcessTree([int]$processId) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $process) { return }
    try { & taskkill.exe /PID $processId /T /F *> $null } catch { }
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

$started = @()
$frontendOutput = Join-Path ([IO.Path]::GetTempPath()) "webchange-frontend-$([Guid]::NewGuid().ToString('N')).log"
$frontendErrors = Join-Path ([IO.Path]::GetTempPath()) "webchange-frontend-$([Guid]::NewGuid().ToString('N')).err.log"
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(3)
try {
    if (-not $SchedulerOnly) {
        $oldEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT')
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development')
        try {
            $api = Start-Process -FilePath 'dotnet.exe' -ArgumentList @('run', '--project', 'backend/src/WebChangeRadar.Api', '--launch-profile', 'https') -WorkingDirectory $root -WindowStyle Hidden -PassThru
            $started += $api
        } finally {
            [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $oldEnvironment)
        }
        $oldCertificate = [Environment]::GetEnvironmentVariable('NODE_EXTRA_CA_CERTS')
        if ([string]::IsNullOrWhiteSpace($oldCertificate)) {
            $certificate = Join-Path $root 'frontend/certificates/api-dev.pem'
            if (-not (Test-Path -LiteralPath $certificate)) {
                throw 'Certificado público da API não encontrado em frontend/certificates/api-dev.pem. Consulte o README.'
            }
            [Environment]::SetEnvironmentVariable('NODE_EXTRA_CA_CERTS', $certificate)
        }
        try {
            $frontend = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'dev') -WorkingDirectory (Join-Path $root 'frontend') -RedirectStandardOutput $frontendOutput -RedirectStandardError $frontendErrors -WindowStyle Hidden -PassThru
            $started += $frontend
        } finally {
            [Environment]::SetEnvironmentVariable('NODE_EXTRA_CA_CERTS', $oldCertificate)
        }
    }

    Write-Host "Aguardando a API em $apiUrl ..."
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = $client.GetAsync("$apiUrl/health").GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { break }
        } catch { }
        if ($started | Where-Object { $_.HasExited }) { throw 'Um processo de desenvolvimento terminou antes da API ficar pronta.' }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'A API não ficou disponível. Confira o PostgreSQL e o certificado HTTPS local.' }

    if (-not $SchedulerOnly) {
        $frontendUrl = $null
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            $output = Get-Content -LiteralPath $frontendOutput -Raw -ErrorAction SilentlyContinue
            if ($output -match 'Local:\s*(https://localhost:\d+)') {
                $frontendUrl = $Matches[1]
                break
            }
            if ($frontend.HasExited) { throw 'O frontend terminou antes de ficar pronto. Consulte o certificado e as dependências npm.' }
            Start-Sleep -Seconds 1
        }
        if (-not $frontendUrl) { throw 'O frontend não informou uma URL HTTPS em até 60 segundos.' }
        Write-Host "Frontend: $frontendUrl"
    }

    Write-Host 'Agendador local ativo. Pressione Ctrl+C para encerrar.'
    $nextTick = [DateTimeOffset]::UtcNow
    do {
        if ($started | Where-Object { $_.HasExited }) { throw 'Um processo de desenvolvimento terminou.' }
        if ([DateTimeOffset]::UtcNow -ge $nextTick) {
            $request = [System.Net.Http.HttpRequestMessage]::new('POST', "$apiUrl/internal/jobs/process-due")
            $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
            try {
                $response = $client.SendAsync($request).GetAwaiter().GetResult()
                try {
                    if (-not $response.IsSuccessStatusCode) {
                        Write-Warning "Agendamento retornou HTTP $([int]$response.StatusCode)."
                        if ($Once) { throw 'Falha na chamada de teste do agendador.' }
                    }
                } finally { $response.Dispose() }
            } catch {
                $failure = if ($_.Exception.InnerException) { $_.Exception.InnerException.GetType().Name } else { $_.Exception.GetType().Name }
                Write-Warning "Não foi possível acionar o processamento agendado ($failure)."
                if ($Once) { throw 'Falha na chamada de teste do agendador.' }
            }
            finally { $request.Dispose() }
            do { $nextTick = $nextTick.AddMinutes(1) } while ($nextTick -le [DateTimeOffset]::UtcNow)
        }
        if (-not $Once) { Start-Sleep -Seconds 1 }
    } while (-not $Once)
} finally {
    $client.Dispose()
    foreach ($process in $started) { Stop-ProcessTree $process.Id }
    Remove-Item -LiteralPath $frontendOutput,$frontendErrors -ErrorAction SilentlyContinue
}
