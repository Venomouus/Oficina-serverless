mock_provider "aws" {
  mock_data "aws_route_table" {
    defaults = {
      vpc_id = "vpc-0123456789abcdef0"
      routes = [{
        cidr_block     = "0.0.0.0/0"
        nat_gateway_id = "nat-0123456789abcdef0"
        gateway_id     = ""
      }]
    }
  }
  mock_data "aws_subnet" {
    defaults = {
      vpc_id                  = "vpc-0123456789abcdef0"
      availability_zone       = "us-east-1a"
      map_public_ip_on_launch = false
    }
  }
  mock_data "aws_security_group" {
    defaults = { vpc_id = "vpc-0123456789abcdef0" }
  }
  mock_resource "aws_iam_role" {
    defaults = {
      arn = "arn:aws:iam::123456789012:role/oficina-staging-autenticacao"
      id  = "oficina-staging-autenticacao"
    }
  }
  mock_resource "aws_cloudwatch_log_group" {
    defaults = { arn = "arn:aws:logs:us-east-1:123456789012:log-group:/aws/lambda/oficina-staging-autenticacao" }
  }
  mock_resource "aws_lambda_function" {
    defaults = { version = "1" }
  }
}

override_data {
  target = data.aws_subnet.lambda["subnet-0123456789abcdef1"]
  values = {
    vpc_id                  = "vpc-0123456789abcdef0"
    availability_zone       = "us-east-1b"
    map_public_ip_on_launch = false
  }
}
override_resource {
  target = aws_secretsmanager_secret.authentication["database"]
  values = { arn = "arn:aws:secretsmanager:us-east-1:123456789012:secret:oficina/staging/database-AbCdEf" }
}
override_resource {
  target = aws_secretsmanager_secret.authentication["jwt"]
  values = { arn = "arn:aws:secretsmanager:us-east-1:123456789012:secret:oficina/staging/jwt-AbCdEf" }
}

variables {
  aws_account_id  = "123456789012"
  jwt_issuer      = "https://staging.example.com"
  lambda_zip_path = "tests/fixtures/lambda.zip"
  platform = {
    contract_version   = 1
    aws_region         = "us-east-1"
    vpc_id             = "vpc-0123456789abcdef0"
    private_subnet_ids = ["subnet-0123456789abcdef0", "subnet-0123456789abcdef1"]
    lambda_security_group_ids = {
      staging  = "sg-0123456789abcdef0"
      producao = "sg-0123456789abcdef1"
    }
  }
  database = {
    contract_version = 1
    aws_region       = "us-east-1"
    vpc_id           = "vpc-0123456789abcdef0"
    address          = "oficina.example.us-east-1.rds.amazonaws.com"
    port             = 5432
    planned_environments = {
      staging  = { branch = "develop", database_name = "oficina_staging", auth_role = "oficina_staging_auth" }
      producao = { branch = "master", database_name = "oficina_producao", auth_role = "oficina_producao_auth" }
    }
  }
}

run "private_lambda_and_own_secrets" {
  command = apply
  assert {
    condition = (
      aws_lambda_function.authentication.runtime == "dotnet8" &&
      aws_lambda_function.authentication.publish &&
      aws_lambda_alias.live.function_version == "1" &&
      aws_lambda_function.authentication.reserved_concurrent_executions == 2 &&
      toset(aws_lambda_function.authentication.vpc_config[0].security_group_ids) == toset(["sg-0123456789abcdef0"]) &&
      length(aws_lambda_function.authentication.vpc_config[0].subnet_ids) == 2
    )
    error_message = "Lambda deve usar rede staging, versao publicada e concorrencia limitada."
  }
  assert {
    condition = (
      aws_lambda_function.authentication.environment[0].variables.DB_NAME == "oficina_staging" &&
      aws_lambda_function.authentication.environment[0].variables.DB_USERNAME == "oficina_staging_auth" &&
      aws_lambda_function.authentication.environment[0].variables.DB_SECRET_ARN == aws_secretsmanager_secret.authentication["database"].arn &&
      aws_lambda_function.authentication.environment[0].variables.JWT_SECRET_ARN == aws_secretsmanager_secret.authentication["jwt"].arn &&
      !contains(keys(aws_lambda_function.authentication.environment[0].variables), "DB_CONNECTION_STRING") &&
      !contains(keys(aws_lambda_function.authentication.environment[0].variables), "JWT_PRIVATE_KEY_PEM") &&
      aws_lambda_function.authentication.environment[0].variables.DB_SSL_ROOT_CERTIFICATE == "/var/task/certificates/rds-global-bundle.pem"
    )
    error_message = "Somente ARNs e parametros publicos devem aparecer no ambiente da Lambda."
  }
  assert {
    condition = (
      toset(jsondecode(aws_iam_role_policy.authentication.policy).Statement[0].Action) == toset(["secretsmanager:GetSecretValue"]) &&
      toset(jsondecode(aws_iam_role_policy.authentication.policy).Statement[0].Resource) == toset([
      aws_secretsmanager_secret.authentication["database"].arn, aws_secretsmanager_secret.authentication["jwt"].arn]) &&
      jsondecode(aws_iam_role_policy.authentication.policy).Statement[3].Effect == "Deny" &&
      jsondecode(aws_iam_role_policy.authentication.policy).Statement[3].Condition.ArnEquals["lambda:SourceFunctionArn"] == local.function_arn
    )
    error_message = "IAM deve limitar leitura aos segredos proprios e negar mutacao de ENI pelo codigo."
  }
  assert {
    condition = (
      alltrue([for secret in aws_secretsmanager_secret.authentication : secret.recovery_window_in_days == 30]) &&
      aws_cloudwatch_log_group.authentication.retention_in_days == 7 &&
      aws_lambda_function.authentication.tracing_config[0].mode == "Active" &&
      length(aws_lambda_permission.gateway) == 0
    )
    error_message = "Segredos, logs e tracing devem estar configurados; sem Gateway informado nao permitir invocacao."
  }
}

run "production_selects_its_database_and_network" {
  command = plan
  variables {
    environment = "producao"
    jwt_issuer  = "https://producao.example.com"
  }
  assert {
    condition = (
      aws_lambda_function.authentication.environment[0].variables.DB_NAME == "oficina_producao" &&
      aws_lambda_function.authentication.environment[0].variables.DB_USERNAME == "oficina_producao_auth" &&
      toset(aws_lambda_function.authentication.vpc_config[0].security_group_ids) == toset(["sg-0123456789abcdef1"]) &&
      aws_secretsmanager_secret.authentication["database"].name == "/oficina/producao/autenticacao/database" &&
      aws_secretsmanager_secret.authentication["jwt"].name == "/oficina/producao/autenticacao/jwt"
    )
    error_message = "Producao deve selecionar seu banco, usuario, SG e nomes de segredos."
  }
}

run "gateway_permission_scoped_to_routes_and_alias" {
  command = plan
  variables {
    api_gateway_execution_arn = "arn:aws:execute-api:us-east-1:123456789012:abc123"
  }
  assert {
    condition = (
      length(aws_lambda_permission.gateway) == 3 &&
      alltrue([for path, permission in aws_lambda_permission.gateway :
        permission.qualifier == "live" &&
        permission.principal == "apigateway.amazonaws.com" &&
        permission.source_account == "123456789012" &&
      permission.source_arn == "arn:aws:execute-api:us-east-1:123456789012:abc123/*/${path}"])
    )
    error_message = "Somente as tres rotas da API informada podem invocar o alias live."
  }
}

run "reject_http_issuer" {
  command = plan
  variables { jwt_issuer = "http://127.0.0.1:5081" }
  expect_failures = [var.jwt_issuer]
}
run "reject_unbounded_concurrency" {
  command = plan
  variables { reserved_concurrency = -1 }
  expect_failures = [var.reserved_concurrency]
}
run "reject_cross_account_gateway" {
  command = plan
  variables { api_gateway_execution_arn = "arn:aws:execute-api:us-east-1:999999999999:abc123" }
  expect_failures = [var.api_gateway_execution_arn]
}
run "reject_cross_region_contracts" {
  command = plan
  variables { aws_region = "us-west-2" }
  expect_failures = [var.platform]
}
run "reject_public_subnet" {
  command = plan
  override_data {
    target = data.aws_subnet.lambda["subnet-0123456789abcdef0"]
    values = {
      vpc_id                  = "vpc-0123456789abcdef0"
      availability_zone       = "us-east-1a"
      map_public_ip_on_launch = true
    }
  }
  expect_failures = [aws_lambda_function.authentication]
}
run "reject_single_az" {
  command = plan
  override_data {
    target = data.aws_subnet.lambda["subnet-0123456789abcdef1"]
    values = {
      vpc_id                  = "vpc-0123456789abcdef0"
      availability_zone       = "us-east-1a"
      map_public_ip_on_launch = false
    }
  }
  expect_failures = [aws_lambda_function.authentication]
}
run "reject_security_group_outside_vpc" {
  command = plan
  override_data {
    target = data.aws_security_group.lambda
    values = { vpc_id = "vpc-0123456789abcdef9" }
  }
  expect_failures = [aws_lambda_function.authentication]
}

run "reject_internet_gateway_route" {
  command = plan
  override_data {
    target = data.aws_route_table.lambda["subnet-0123456789abcdef0"]
    values = {
      vpc_id = "vpc-0123456789abcdef0"
      routes = [{
        cidr_block     = "0.0.0.0/0"
        gateway_id     = "igw-0123456789abcdef0"
        nat_gateway_id = ""
      }]
    }
  }
  expect_failures = [aws_lambda_function.authentication]
}
