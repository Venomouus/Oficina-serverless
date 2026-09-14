# ADR 002 — Lambda privada e segredos por ambiente

Status: implementado em codigo e testes; provisionamento AWS pendente.

## Decisao

Manter o handler HTTP API v2 e a assinatura RS256 existentes. Na AWS, substituir PEM/conexao em variaveis por leitura de dois segredos com o SDK oficial: database (username/password) e jwt (keyId/privateKeyPem). Terraform gerencia somente os containers e concede GetSecretValue exclusivamente nesses ARNs.

O contrato RDS informa hostname, banco e usuario esperados. O JSON nao controla o destino; o username deve coincidir com a role auth do ambiente. Npgsql verifica CA/hostname com VerifyFull e usa um bundle publico RDS incluido no ZIP. A credencial mestre RDS nunca e usada pela funcao.

Configurar Lambda em sub-redes privadas de workloads, com SG do ambiente e acesso NAT ao Secrets Manager. Nao colocar a funcao nas sub-redes isoladas do banco. Concorrencia reservada limita pressao de conexoes no RDS.

## Consequencias

O modo local preserva seus comandos e nao precisa de AWS. Na Lambda, configuracao ambigua e rejeitada e a inicializacao inclui o timeout da invocacao. Falhas retornam 503 sem conteudo do segredo e podem ser tentadas novamente.

A configuracao bem-sucedida fica no cache ate substituir o ambiente de execucao. configuration_revision permite gerar nova versao apos mudancas externas de segredo. Rotacao com varias chaves JWKS ainda precisa ser implementada para evitar invalidar tokens durante a transicao.

Cada ambiente possui state, funcao, role e segredos separados. Rede e RDS continuam compartilhados; isolamento SQL exige grants e credenciais distintos, alem das regras de rede.

O Gateway continua no infra-kubernetes; recebe o invoke ARN do alias live e fornece seu execution ARN. A permissao permite somente as tres rotas publicas deste handler. Notificacoes, CD e bootstrap automatizado ficam fora desta entrega.

## Evidencias

Testes cobrem compatibilidade local, construcao da conexao TLS, validacao de segredos, ausencia de vazamento de erros, retry apos falha, cache concorrente, cancelamento, contrato AWSCURRENT e consulta PostgreSQL real. Terraform testa ambientes, IAM, rede privada e escopo de invocacao sem AWS real.

[Operacao e referencias oficiais](../../infra/README.md).
