# Infraestrutura da autenticacao AWS

## Recursos e dependencias

Terraform cria, por ambiente, Lambda .NET 8/x86_64, versao publicada e alias live, role/policy IAM, dois containers Secrets Manager e grupo CloudWatch com retencao de sete dias. Ativa tracing da Lambda e limita concorrencia a duas execucoes por padrao. Nao cria Gateway, VPC, RDS, usuarios SQL nem valores de segredos.

Consome campos dos outputs `platform` (infra-kubernetes) e `database` (infra-database). A Lambda usa as sub-redes privadas de workloads, com NAT para acessar Secrets Manager, e o SG Lambda do ambiente. O RDS deve continuar nas sub-redes isoladas. As regras de saida Lambda→RDS ja pertencem ao infra-database; nao duplicar regras de SG neste state.

As validacoes exigem mesma conta/regiao/VPC, duas AZs, nomes de banco/role separados e rotas privadas via NAT, sem IGW direto. Sao verificacoes de configuracao; conectividade e permissoes reais exigem plano e teste autenticados.

A role pode ler somente seus dois segredos e escrever no proprio log group. Permissoes EC2 com Resource * existem para a integracao VPC do servico Lambda; uma negacao condicionada a lambda:SourceFunctionArn impede que o codigo use essas permissoes para modificar interfaces. X-Ray requer permissoes de escrita sem ARN de recurso. Nao ha ListSecrets, leitura do segredo mestre RDS ou escrita de segredos na role.

Referencias: [integracao Lambda/VPC](https://docs.aws.amazon.com/lambda/latest/dg/configuration-vpc.html), [SDK Secrets Manager](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SecretsManager/NSecretsManager.html).

## Preparacao do plano — ainda sem provisionamento

1. Gere o ZIP linux-x64 conforme o README da raiz.
2. Copie terraform.tfvars.example para terraform.tfvars e substitua IDs, conta, endpoint RDS e issuer pelos valores reais.
3. Copie backend.hcl.example para backend.hcl e informe o bucket existente de state com criptografia/versionamento e bloqueio de acesso publico.
4. Use chaves distintas: oficina/staging/serverless.tfstate e oficina/producao/serverless.tfstate. Confira que environment corresponde a chave selecionada; nao apontar os dois ambientes para o mesmo state.
5. Confirme a identidade AWS autorizada e as quotas de Lambda/VPC antes de produzir o plano.

```powershell
terraform -chdir=infra init -reconfigure -backend-config=backend.hcl -input=false
terraform -chdir=infra plan -out=serverless.tfplan
terraform -chdir=infra show -no-color serverless.tfplan
```

O Terraform test usa provider simulado e nao exige credenciais; um resultado aprovado nao demonstra um deploy real. A CI permanece sem plan/apply, sem identidade AWS e sem CD. DEPLOY_ENABLED deve permanecer false nesta etapa.

O deploy real exige revisar o plano e preparar o bootstrap abaixo. Reservar concorrencia depende das quotas da conta; cada ambiente permite por padrao ate dez conexoes PostgreSQL (2 execucoes × pool 5). Considerar tambem conexoes da API, migrations e o outro ambiente. NAT, Lambda, Secrets Manager, logs e tracing podem gerar custos quando usados.

## Bootstrap dos segredos

Terraform cria somente os containers; nao existe aws_secretsmanager_secret_version nem senha/chave no state. Segredos vazios fazem a inicializacao retornar 503. Inserir os valores em AWSCURRENT por uma identidade administrativa limitada, usando Secrets Manager fora do Terraform.

Formato do segredo database:

```json
{
  "username": "oficina_staging_auth",
  "password": "<senha-da-role-auth-criada-no-bootstrap-do-banco>"
}
```

Formato do segredo jwt:

```json
{
  "keyId": "staging-2026-01",
  "privateKeyPem": "<PEM-RSA-privado-com-quebras-de-linha>"
}
```

Use chave RSA de pelo menos 2048 bits, distinta da chave local e da chave do outro ambiente. O keyId deve identificar a chave correspondente. Gerar a chave uma unica vez fora da Lambda; nao gerar em cold start. Ao produzir JSON, usar um serializador para preservar as quebras de linha do PEM.

Os arquivos temporarios de bootstrap, se necessarios, devem ficar fora do Git (por exemplo config/staging.local.json), com acesso restrito. Nao passar o valor do segredo em argumentos literais, logs, comentarios de PR ou variaveis Terraform. O output authentication.secret_arns informa apenas os destinos.

O bootstrap do banco deve criar a role auth com CONNECT no banco correto, USAGE no schema public e SELECT apenas em Clientes.Id, Clientes.CpfCnpj e Clientes.Ativo. Deve revogar acessos PUBLIC indevidos e impedir conexao ao banco do outro ambiente. Nao usar oficina_admin nem seu segredo mestre na Lambda.

## Ordem de integracao com o Gateway

Para evitar dependencias circulares:

1. Preparar rede, RDS, bootstrap SQL e o recurso HTTP API do ambiente no infra-kubernetes, ainda sem integrar a Lambda.
2. Usar o endpoint HTTPS daquele HTTP API como jwt_issuer e seu execution ARN como api_gateway_execution_arn neste repositorio. Preferir um HTTP API por ambiente com stage $default, preservando os caminhos do handler.
3. Provisionar Lambda/containers e preencher os valores dos segredos. O pacote nao depende do Gateway.
4. No infra-kubernetes, consumir authentication.invoke_arn do alias live para integracao AWS_PROXY com payload 2.0 e publicar as tres rotas publicas.
5. Configurar o JWT authorizer para as rotas da API principal com issuer/audience correspondentes e manter autorizacao por cliente dentro da API.

Com api_gateway_execution_arn=null nao ha permissao de invocacao pelo Gateway. Quando informado, as permissoes limitam origem a essa conta/API, alias live e metodos/caminhos de autenticacao, JWKS e discovery. Nunca usar origem global '*'.

Validar JWT emitido na AWS contra a API, cliente ativo/inativo, OS de outro cliente (404), acesso administrativo negado (403) e indisponibilidade sem vazamento de detalhes. Gateway deve aplicar limites de tentativas antes da exposicao publica. Tracing de plataforma da Lambda nao substitui instrumentacao distribuida da consulta SQL e da API.

## Atualizacoes e rotacao

Uma leitura bem-sucedida e armazenada por ambiente de execucao; falhas nao ficam no cache. Para aplicar mudancas de segredo a ambientes ja inicializados, alterar configuration_revision e publicar uma nova versao pelo Terraform, movendo o alias live. Esse marcador e publico e nao deve conter o segredo.

A rotacao RSA sem interrupcao ainda exige publicar simultaneamente as chaves antiga/nova no JWKS, esperar caches e validade dos tokens e so entao retirar a chave antiga. Este codigo publica uma unica chave; nao habilitar rotacao automatica nem substituir o segredo de producao como se isso ja estivesse implementado. Para senha SQL, coordenar alteracao de credencial e renovacao das instancias; a senha mestre gerenciada pelo RDS nao e a senha dessa role.

O bundle publico de CA RDS esta versionado e incluido no publish. Revisar sua validade e atualizar antes de mudancas de CA; manter VerifyFull e o hostname RDS. [Origem e hash](../src/Oficina.Autenticacao/certificates/README.md).

## Teardown e limites

Remover integracoes/permissoes do Gateway, depois recursos serverless e, por ultimo, RDS/rede conforme seus runbooks. Segredos usam janela de recuperacao de 30 dias; recriar imediatamente o mesmo nome pode exigir recuperar o segredo pendente e reconciliar/importar o recurso. Nao usar force delete como rotina.

Notificacoes, fila/DLQ, outbox e envio real permanecem pendentes. O bootstrap automatizado dos bancos/roles e o CD dos ambientes tambem permanecem para as proximas etapas.
