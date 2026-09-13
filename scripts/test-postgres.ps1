$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$container = 'oficina-auth-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$previousConnection = $env:OFICINA_TEST_POSTGRES
$started = $false
Push-Location $repo
try {
    docker run --detach --rm --name $container --label oficina.purpose=auth-validation --memory 384m --cpus 1 --publish 127.0.0.1::5432 --env POSTGRES_DB=oficina_auth_tests --env POSTGRES_PASSWORD=auth-tests-only postgres:16-alpine
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel iniciar o PostgreSQL temporario. Verifique o Docker Desktop.' }
    $started = $true
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        docker exec $container pg_isready -U postgres -d oficina_auth_tests *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'PostgreSQL de testes nao ficou pronto.' }
    $address = docker port $container 5432/tcp
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao obter porta de testes.' }
    $port = ($address.Trim() -split ':')[-1]
    $env:OFICINA_TEST_POSTGRES = "Host=127.0.0.1;Port=$port;Database=oficina_auth_tests;Username=postgres;Password=auth-tests-only"
    dotnet test Oficina.Serverless.sln --configuration Release --logger trx --results-directory TestResults
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam. Consulte o resultado acima.' }
}
finally {
    $env:OFICINA_TEST_POSTGRES = $previousConnection
    if ($started) { docker stop --timeout 5 $container | Out-Null }
    Pop-Location
}
