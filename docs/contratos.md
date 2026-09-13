# Contratos de autenticacao e notificacoes

## Autenticacao implementada

`POST /auth/cpf`, JSON de ate 2048 bytes (excedente retorna 400 no handler Lambda;
o servidor local tambem possui limite de transporte e pode retornar 413).

```json
{ "cpf": "529.982.247-25" }
```

Somente CPF valido de cliente existente e ativo recebe HTTP 200:

```json
{ "accessToken": "<JWT RS256>", "expiresIn": 900, "tokenType": "Bearer" }
```

| Codigo | Significado |
|---|---|
| 400 | Corpo JSON/base64 ou CPF invalido |
| 401 | Cliente inexistente, inativo, ID invalido ou CPF ambiguo na base |
| 404 | Rota desconhecida |
| 405 | Metodo nao permitido |
| 503 | Banco indisponivel, timeout ou configuracao Lambda indisponivel |
| 500 | Falha inesperada, sem detalhes internos na resposta |

CPF ausente/inativo tem resposta identica `{"message":"Cliente nao autorizado."}`.
O adaptador consulta `Clientes.Id`, `Clientes.CpfCnpj` e `Clientes.Ativo`, usando
parametros para o CPF normalizado e sua mascara. Se houver os dois formatos em
cadastros distintos, a autenticacao e negada para evitar escolher a pessoa errada.

JWT: `alg=RS256`, `typ=at+jwt`, `kid` configurado; claims `sub` (UUID do cliente),
`role=Cliente`, `scope=oficina:cliente`, `iss`, `aud`, `iat`, `nbf`, `exp`, `jti`.
O CPF e a chave privada nao aparecem no token. Validade padrao 900 segundos,
configuravel entre 60 e 900. Desativar cliente impede novas emissoes; tokens
anteriores continuam validos ate expirar, salvo futura revogacao/verificacao na API.

Todas as respostas incluem `X-Correlation-ID`. Na Lambda, o ID vem do contexto
do API Gateway, com fallback para o ID da invocacao. No host local, vem do servidor.
Logs JSON contem operacao, correlacao, codigo HTTP, duracao e tipo da falha;
nao incluem corpo, CPF, JWT, segredo ou mensagem interna da excecao.
Respostas de autenticacao usam `Cache-Control: no-store`.

## Chave publica e emissor

- `GET /.well-known/jwks.json`: JWKS com `kty`, `use`, `alg`, `kid`, `n` e `e`.
- `GET /.well-known/openid-configuration`: `issuer` e `jwks_uri`.

Esses endpoints nao consultam o banco e usam cache publico de 300 segundos.
A descoberta e minima para validadores JWT; nao implementa login OAuth/OIDC
completo, authorization code, refresh token nem ID token. A API Gateway recebera
issuer HTTPS e audience correspondentes, com JWKS acessivel sem autenticacao.
Configuracao/chave sao carregadas uma vez por instancia, sem geracao automatica
em cold start. Rotacao com sobreposicao de chaves publicas ainda esta pendente.

## Integracao ainda pendente

A API principal usa JWT administrativo simetrico. Este token RSA de cliente nao
sera aceito antes de configurar um esquema de validacao para esse emissor. Manter
o acesso administrativo separado; exigir `role=Cliente`, audience/issuer corretos,
assinatura, validade e escopo nas rotas destinadas ao cliente. A API deve conferir
`sub == ClienteId` da OS e nao usar um CPF enviado pelo consumidor como autorizacao.

CPF sozinho identifica cadastro, mas nao comprova posse da identidade. Para o
fluxo academico, a emissao segue o requisito do desafio; uso real exige fator de
verificacao adicional. Limitacao de tentativas no Gateway e autorizacao por OS
tambem fazem parte da proxima integracao, antes de expor o fluxo publicamente.

## Notificacoes (planejadas)

Mensagem de negocio dentro de um futuro registro SQS:

```json
{
  "eventoId": "11111111-1111-4111-8111-111111111111",
  "ordemServicoId": "22222222-2222-4222-8222-222222222222",
  "destinatario": "cliente@example.com",
  "mensagem": "Seu orcamento esta disponivel."
}
```

O caso de uso valida identificadores, destinatario e mensagem. Handler SQS,
outbox na API, fila/DLQ, idempotencia, envio real e observabilidade estao pendentes.

## Referencias

- [OpenAPI serverless](openapi.yaml); Swagger local: http://127.0.0.1:5081/swagger.
- [API principal e Swagger](https://github.com/Venomouus/Oficina-Mecanica#collection--swagger).
- [AWS: handler C#](https://docs.aws.amazon.com/lambda/latest/dg/csharp-handler.html).
- [AWS: integracao HTTP API v2](https://docs.aws.amazon.com/apigateway/latest/developerguide/http-api-develop-integrations-lambda.html).
- [AWS: autorizador JWT](https://docs.aws.amazon.com/apigateway/latest/developerguide/http-api-jwt-authorizer.html).
- [Npgsql: conexoes e consultas parametrizadas](https://www.npgsql.org/doc/basic-usage.html).
