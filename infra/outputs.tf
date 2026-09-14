output "authentication" {
  description = "Contrato para integracao HTTP API v2 no infra-kubernetes; nao cria Gateway."
  value = {
    contract_version = 1
    environment      = var.environment
    aws_region       = var.aws_region
    function_name    = aws_lambda_function.authentication.function_name
    alias_arn        = aws_lambda_alias.live.arn
    invoke_arn       = aws_lambda_alias.live.invoke_arn
    issuer           = var.jwt_issuer
    audience         = var.jwt_audience
    public_routes    = ["POST /auth/cpf", "GET /.well-known/jwks.json", "GET /.well-known/openid-configuration"]
    secret_arns      = { for key, secret in aws_secretsmanager_secret.authentication : key => secret.arn }
  }
}
