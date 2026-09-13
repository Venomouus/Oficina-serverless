# ADR 001 - Autenticacao por CPF com PostgreSQL e JWT RSA

Status: aceita para implementacao local e preparacao da Lambda.

## Contexto

O Tech Challenge exige funcao serverless que valide CPF, consulte existencia e
status do cliente e emita token. A API ja adicionou `Clientes.Ativo` e usa
PostgreSQL. O Gateway futuro precisa validar tokens sem receber a chave privada.

## Decisao

Usar Npgsql com consulta parametrizada por CPF e emissao RS256 com chave RSA
persistente configurada fora do codigo. A chave publica e exposta via JWKS,
permitindo preparar a integracao com o autorizador JWT do HTTP API Gateway.
O handler usa payload HTTP API 2.0. O host local compartilha o adaptador HTTP e o
caso de uso, permitindo testes sem AWS e sem duplicar regras.

Identificar o cliente pelo UUID da base (`sub`), emitir somente perfil `Cliente`
e escopo `oficina:cliente`, limitar validade a 15 minutos. Manter o JWT
administrativo existente separado. A verificacao de propriedade da OS sera feita
na API, alem da validacao criptografica no Gateway/API.

## Consequencias e limites

A chave privada nao e distribuida aos validadores. O emissor deve ter URL HTTPS
publica e estavel por ambiente; issuer e audience devem coincidir exatamente.
CPF ausente ou inativo usa o mesmo erro; formatos duplicados na base sao negados.
Usar usuario PostgreSQL com leitura apenas das colunas necessarias e TLS no RDS.

CPF sozinho nao comprova identidade. A demonstracao academica segue o requisito,
mas uma operacao real exige fator adicional. Tokens ja emitidos nao sao revogados
imediatamente ao desativar cliente. Limites de tentativas, rotacao com coexistencia
de JWKS, Secrets Manager e deploy/authorizer AWS serao implementados depois.
O JWT de cliente ainda nao da acesso a API principal nesta entrega.

## Evidencias e referencias

Testes validam assinatura com a chave publica, expiracao, adulteracao, audiencia,
claims, evento Lambda v2 e consulta PostgreSQL real no CI.
[Contrato e fontes oficiais](../contratos.md#referencias).
