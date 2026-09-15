locals {
  function_name           = "${var.project_name}-${var.environment}-autenticacao"
  function_arn            = "arn:aws:lambda:${var.aws_region}:${var.aws_account_id}:function:${local.function_name}"
  selected_database       = var.database.planned_environments[var.environment]
  selected_security_group = var.platform.lambda_security_group_ids[var.environment]
}

data "aws_subnet" "lambda" {
  for_each = toset(var.platform.private_subnet_ids)
  id       = each.value
}
data "aws_security_group" "lambda" {
  id = local.selected_security_group
}

data "aws_route_table" "lambda" {
  for_each  = toset(var.platform.private_subnet_ids)
  subnet_id = each.value
}

resource "aws_secretsmanager_secret" "authentication" {
  for_each                = toset(["database", "jwt"])
  name                    = "/${var.project_name}/${var.environment}/autenticacao/${each.value}"
  description             = "Bootstrap externo: ${each.value} da autenticacao ${var.environment}."
  recovery_window_in_days = 30
}
# Nao criar secret_version: valores privados sao inseridos pelo bootstrap fora do state.

resource "aws_cloudwatch_log_group" "authentication" {
  name              = "/aws/lambda/${local.function_name}"
  retention_in_days = 7
}

resource "aws_iam_role" "authentication" {
  count = var.academy_role_arn == null ? 1 : 0
  name  = local.function_name
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "authentication" {
  count = var.academy_role_arn == null ? 1 : 0
  name  = "runtime"
  role  = aws_iam_role.authentication[0].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "ReadOnlyOwnSecrets"
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = [for secret in aws_secretsmanager_secret.authentication : secret.arn]
      },
      {
        Sid      = "WriteOwnLogs"
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = ["${aws_cloudwatch_log_group.authentication.arn}:*"]
      },
      {
        Sid    = "LambdaVpcInterfaces"
        Effect = "Allow"
        Action = ["ec2:CreateNetworkInterface", "ec2:DescribeNetworkInterfaces", "ec2:DescribeSubnets",
        "ec2:DeleteNetworkInterface", "ec2:AssignPrivateIpAddresses", "ec2:UnassignPrivateIpAddresses"]
        Resource = "*"
      },
      {
        Sid    = "DenyVpcMutationFromFunctionCode"
        Effect = "Deny"
        Action = ["ec2:CreateNetworkInterface", "ec2:DeleteNetworkInterface",
        "ec2:AssignPrivateIpAddresses", "ec2:UnassignPrivateIpAddresses"]
        Resource  = "*"
        Condition = { ArnEquals = { "lambda:SourceFunctionArn" = local.function_arn } }
      },
      {
        Sid      = "LambdaTracing"
        Effect   = "Allow"
        Action   = ["xray:PutTraceSegments", "xray:PutTelemetryRecords"]
        Resource = "*"
      }
    ]
  })
}

resource "aws_lambda_function" "authentication" {
  function_name                  = local.function_name
  description                    = "Autenticacao CPF/JWT - ${var.environment}"
  role                           = var.academy_role_arn != null ? var.academy_role_arn : aws_iam_role.authentication[0].arn
  runtime                        = "dotnet8"
  architectures                  = ["x86_64"]
  handler                        = "Oficina.Autenticacao::Oficina.Autenticacao.Function::FunctionHandler"
  filename                       = var.lambda_zip_path
  source_code_hash               = filebase64sha256(var.lambda_zip_path)
  publish                        = true
  memory_size                    = 256
  timeout                        = 15
  reserved_concurrent_executions = var.reserved_concurrency

  vpc_config {
    subnet_ids         = var.platform.private_subnet_ids
    security_group_ids = [local.selected_security_group]
  }
  environment {
    variables = {
      DB_SECRET_ARN           = aws_secretsmanager_secret.authentication["database"].arn
      JWT_SECRET_ARN          = aws_secretsmanager_secret.authentication["jwt"].arn
      DB_HOST                 = var.database.address
      DB_NAME                 = local.selected_database.database_name
      DB_USERNAME             = local.selected_database.auth_role
      DB_SSL_ROOT_CERTIFICATE = "/var/task/certificates/rds-global-bundle.pem"
      JWT_ISSUER              = var.jwt_issuer
      JWT_AUDIENCE            = var.jwt_audience
      JWT_LIFETIME_SECONDS    = "900"
      CONFIGURATION_REVISION  = var.configuration_revision
    }
  }
  tracing_config { mode = "Active" }

  depends_on = [aws_cloudwatch_log_group.authentication, aws_iam_role_policy.authentication]
  lifecycle {
    precondition {
      condition = (
        alltrue([for subnet in data.aws_subnet.lambda :
        subnet.vpc_id == var.platform.vpc_id && !subnet.map_public_ip_on_launch]) &&
        length(toset([for subnet in data.aws_subnet.lambda : subnet.availability_zone])) >= 2 &&
        data.aws_security_group.lambda.vpc_id == var.platform.vpc_id &&
        alltrue([for table in data.aws_route_table.lambda :
          table.vpc_id == var.platform.vpc_id &&
          anytrue([for route in table.routes : route.cidr_block == "0.0.0.0/0" && try(length(route.nat_gateway_id) > 0, false)]) &&
          alltrue([for route in table.routes : route.gateway_id == null || route.gateway_id == "" || route.gateway_id == "local"])
        ])
      )
      error_message = "Lambda exige sub-redes privadas em duas AZs, saida NAT sem IGW direto e SG na mesma VPC."
    }
  }
}

resource "aws_lambda_alias" "live" {
  name             = "live"
  description      = "Versao publicada para o Gateway."
  function_name    = aws_lambda_function.authentication.function_name
  function_version = aws_lambda_function.authentication.version
}

# O Gateway e suas rotas permanecem no infra-kubernetes. Permissao apenas quando
# o execution ARN daquele ambiente for fornecido; nunca autorizar origem '*'.
resource "aws_lambda_permission" "gateway" {
  for_each = var.api_gateway_execution_arn == null ? toset([]) : toset([
    "POST/auth/cpf", "GET/.well-known/jwks.json", "GET/.well-known/openid-configuration"
  ])
  statement_id   = "Gateway${substr(sha256(each.value), 0, 16)}"
  action         = "lambda:InvokeFunction"
  function_name  = aws_lambda_function.authentication.function_name
  qualifier      = aws_lambda_alias.live.name
  principal      = "apigateway.amazonaws.com"
  source_account = var.aws_account_id
  source_arn     = "${var.api_gateway_execution_arn}/*/${each.value}"
}
