param([switch]$Once, [string]$ApiUrl = 'https://localhost:7010')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$apiUrl = $ApiUrl.TrimEnd('/')
$token = [Environment]::GetEnvironmentVariable('InternalJobs__Token')
if ([string]::IsNullOrWhiteSpace($token)) {
    $entry = Get-Content -LiteralPath (Join-Path $PSScriptRoot '.env') -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '^\s*InternalJobs__Token\s*=' } | Select-Object -Last 1
    if ($entry) { $token = ($entry -split '=', 2)[1].Trim().Trim('"', "'") }
}
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Configure InternalJobs__Token no ambiente ou no .env antes de iniciar.'
}

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(3)
try {
    Write-Host "Aguardando a API em $apiUrl ..."
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = $client.GetAsync("$apiUrl/health").GetAwaiter().GetResult()
            $ready = $response.IsSuccessStatusCode
            $response.Dispose()
            if ($ready) { break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'A API não ficou disponível. Inicie-a antes do agendador.' }

    Write-Host 'Agendador ativo. Pressione Ctrl+C para encerrar.'
    $nextTick = [DateTimeOffset]::UtcNow
    do {
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
            } finally { $request.Dispose() }
            do { $nextTick = $nextTick.AddMinutes(1) } while ($nextTick -le [DateTimeOffset]::UtcNow)
        }
        if (-not $Once) { Start-Sleep -Seconds 1 }
    } while (-not $Once)
} finally {
    $client.Dispose()
}
