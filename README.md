# Oficina Serverless

Autenticacao por CPF com PostgreSQL e JWT RS256, host local de testes e infraestrutura da Lambda na AWS. A API principal permanece em Oficina-Mecanica.

## Entrega atual

- Consulta parametrizada de cliente existente/ativo e emissao de token de cliente com validade de ate 15 minutos.
- Handler HTTP API v2: autenticacao, discovery do emissor e JWKS publico.
- Modo local com Swagger e chave RSA em arquivo, preservado.
- Modo AWS com dois segredos por ambiente: credencial PostgreSQL da role auth e chave privada JWT.
- Terraform da Lambda privada, IAM, Secrets Manager (containers sem valores), logs, tracing da Lambda, alias e permissao restrita de invocacao pelo Gateway.
- Contratos de rede/RDS, verificacao de duas AZs, rotas NAT e separacao staging/producao.
- 72 testes .NET, incluindo PostgreSQL real descartavel, e 11 testes Terraform com AWS simulada.
- CI gera ZIP linux-x64 incluindo o bundle publico de CA do RDS e o SDK Secrets Manager.

Lambda de autenticacao publicada no Academy. CPF/JWT e consulta real ao RDS demonstrados em homologacao. O modo academy_role_arn reutiliza LabRole; conta normal preserva a role especifica. Notificacoes externas permanecem pendentes.

O workflow `academy-deploy.yml` faz deploy de develop/master no runner Windows autorizado (label academy). Requer PC/Docker ativos e credenciais temporarias Academy validas. O codigo compartilhado de deploy fica em [Oficina-Mecanica/academy](https://github.com/Venomouus/Oficina-Mecanica/tree/master/academy). Evidencias e limites finais estao no [pacote de entrega](https://github.com/Venomouus/Oficina-Mecanica/tree/master/docs/entrega).

## Executar localmente

Requer .NET 8 e PostgreSQL com o schema da API, incluindo `Clientes.Ativo`. Este repositorio nao executa migrations.

No PowerShell, na raiz:

```powershell
dotnet restore Oficina.Serverless.sln
$chaveLocal = Join-Path (Get-Location) 'config/auth.local.pem'
if (!(Test-Path -LiteralPath $chaveLocal)) {
    dotnet run --project src/Oficina.Autenticacao.Local -- --generate-dev-key $chaveLocal
}
$env:DB_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=oficina;Username=postgres;Password=postgres'
$env:JWT_PRIVATE_KEY_FILE = $chaveLocal
$env:JWT_ISSUER = 'http://127.0.0.1:5081'
$env:JWT_AUDIENCE = 'oficina-api'
$env:JWT_KEY_ID = 'local-2026-01'
$env:JWT_LIFETIME_SECONDS = '900'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5081'
dotnet run --project src/Oficina.Autenticacao.Local --no-launch-profile
```

Abra http://127.0.0.1:5081/swagger. Configure apenas uma fonte de PEM local, por arquivo ou `JWT_PRIVATE_KEY_PEM`. Use um terminal sem variaveis `DB_SECRET_ARN`, `JWT_SECRET_ARN` ou contexto Lambda. Os exemplos de senha e HTTP sao exclusivos do ambiente local.

A chave privada local continua ignorada pelo Git. A unica excecao PEM versionada e `src/Oficina.Autenticacao/certificates/rds-global-bundle.pem`, que contem certificados publicos da AWS, sem chave privada.

## Configuracao AWS

O modo AWS e selecionado pelo contexto Lambda ou pela presenca de um ARN de segredo. Fontes locais de conexao/chave e `JWT_KEY_ID` sao rejeitadas nesse modo; nao existe fallback silencioso.

| Variavel | Conteudo |
|---|---|
| DB_SECRET_ARN | ARN do segredo com username/password da role auth do ambiente |
| JWT_SECRET_ARN | ARN do segredo com keyId/privateKeyPem |
| DB_HOST / DB_NAME / DB_USERNAME | Destino e usuario esperados, vindos do contrato do RDS |
| DB_SSL_ROOT_CERTIFICATE | Bundle publico RDS, no ZIP em /var/task/certificates/rds-global-bundle.pem |
| JWT_ISSUER / JWT_AUDIENCE | URL HTTPS publica do emissor e audiencia configurada na API |
| JWT_LIFETIME_SECONDS | Validade de 60 a 900 segundos |
| CONFIGURATION_REVISION | Revisao publica para publicar nova versao apos mudancas de segredos |

O SDK usa a role de execucao Lambda para ler somente AWSCURRENT dos dois segredos. O usuario do JSON deve coincidir com DB_USERNAME. Host/banco nao sao lidos do segredo. Npgsql usa VerifyFull, validando CA e hostname, pool de ate cinco conexoes e timeouts de cinco segundos.

A configuracao bem-sucedida fica em memoria por ambiente de execucao. Falha de inicializacao retorna 503 sem revelar detalhes e permite nova tentativa. A leitura dos segredos compartilha o limite de tempo da invocacao. Discovery/JWKS nao consultam o PostgreSQL, mas em um cold start precisam carregar a configuracao dos segredos.

Atualizar um segredo nao atualiza automaticamente os caches. Leia [operacao e rotacao](infra/README.md) antes de substituir chaves ou senhas.

## Testes e pacote

Com Docker aberto, execute todos os testes em banco temporario:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test-postgres.ps1
```

O script cria e remove somente seu proprio container. Sem Docker, `dotnet test Oficina.Serverless.sln --configuration Release` executa os testes unitarios e marca dois testes PostgreSQL como ignorados quando OFICINA_TEST_POSTGRES nao esta definida.

Terraform, sem credenciais AWS:

```powershell
terraform -chdir=infra fmt -check -recursive
terraform -chdir=infra init -backend=false -input=false -lockfile=readonly
terraform -chdir=infra validate -no-color
terraform -chdir=infra test -no-color
```

Geracao do pacote de deploy:

```powershell
dotnet publish src/Oficina.Autenticacao/Oficina.Autenticacao.csproj --configuration Release --runtime linux-x64 --self-contained false --output artifacts/auth
Compress-Archive -Path artifacts/auth/* -DestinationPath artifacts/oficina-autenticacao.zip -Force
```

DLLs, deps/runtimeconfig e certificates devem estar na raiz adequada do ZIP. Nunca incluir `config/auth.local.pem`, arquivos de segredos ou o host Swagger. O ZIP vazio em infra/tests/fixtures serve apenas ao teste Terraform simulado.

## Git e proximas integracoes

Checks: `build-test` e `validate-terraform`. Fluxo: feature → develop → master. Infraestrutura serverless tem state separado por ambiente, ao contrario dos states compartilhados de rede e RDS.

O infra-kubernetes continua responsavel pelo Gateway e suas integracoes. Este repositorio fornece alias ARN/invoke ARN e configura permissao somente quando receber o execution ARN especifico da API. Ainda nao ha URL AWS ativa.

Referencias do projeto: [contratos HTTP](docs/contratos.md), [OpenAPI](docs/openapi.yaml), [operacao Terraform](infra/README.md), [ADR AWS](docs/adrs/002-lambda-secrets-manager.md).
