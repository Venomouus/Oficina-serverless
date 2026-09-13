# Contratos planejados

Este documento descreve a integracao futura. Nao existem endpoint HTTP publicado,
handler Lambda ou JWT real neste PR. Os projetos atuais sao bibliotecas de casos
de uso, exercitadas por testes.

## Autenticacao

Rota planejada: `POST /auth/cpf`.

```json
{ "cpf": "529.982.247-25" }
```

Resposta planejada para cliente ativo: accessToken (JWT), tokenType (Bearer) e
expiresIn (segundos). Casos previstos: 400 para entrada invalida; 401 para cliente
nao autorizado; 503 para indisponibilidade da consulta ou assinatura.

O handler futuro traduzira as excecoes de dominio para HTTP e nunca retornara
detalhes internos de falhas ao consumidor. O caso de uso atual normaliza e valida
CPF, consulta `IClienteConsulta`, exige cliente ativo com ID valido e chama
`IEmissorToken`. Os testes usam implementacoes falsas dessas interfaces.

Pendencias:
- Adicionar e migrar o status do cliente no repositorio da API.
- Implementar consulta parametrizada ao PostgreSQL com acesso somente de leitura.
- Emitir JWT RSA com sub (ID do cliente), role, iss, aud, exp e kid.
- Publicar descoberta do emissor e JWKS para o autorizador do API Gateway.
- Definir rotacao de chaves e limite de tentativas.
- Adicionar handler Lambda, serializacao e logs JSON sem CPF/token.
- Adaptar autorizacao da API para perfil e propriedade da OS.

CPF sozinho identifica o cadastro, mas nao comprova identidade. Essa limitacao
do fluxo pedido pelo desafio deve constar na documentacao arquitetural.

## Notificacoes

Mensagem de negocio planejada dentro de um registro SQS:

```json
{
  "eventoId": "11111111-1111-4111-8111-111111111111",
  "ordemServicoId": "22222222-2222-4222-8222-222222222222",
  "destinatario": "cliente@example.com",
  "mensagem": "Seu orcamento esta disponivel."
}
```

O caso de uso valida os identificadores, destinatario e mensagem antes de chamar
`INotificacaoSender`. Falhas do sender propagam para o chamador.

Pendencias:
- Handler SQS com resposta de falhas parciais por mensagem.
- Outbox na API, fila, DLQ e repeticao de tentativas.
- Idempotencia persistente usando eventoId.
- Adaptador de envio real e credenciais no gerenciador de segredos.
- Correlacao de logs/traces, metricas de falha e alertas.

## API relacionada

- [Repositorio da API e instrucoes Swagger](https://github.com/Venomouus/Oficina-Mecanica#collection--swagger).
- Swagger da API em Docker: http://localhost:8080/swagger (inicie a API primeiro).
- URL de autenticacao na nuvem e OpenAPI serverless: pendentes da implementacao HTTP.
