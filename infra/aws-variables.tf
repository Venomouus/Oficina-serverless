variable "aws_region" {
  type    = string
  default = "us-east-1"
  validation {
    condition     = can(regex("^[a-z]{2}(-[a-z]+)+-[0-9]+$", var.aws_region))
    error_message = "Informe uma regiao AWS valida."
  }
}

variable "configuration_revision" {
  description = "Alterar apos atualizar segredos para publicar nova versao e substituir caches; nao habilita rotacao RSA sem sobreposicao de JWKS."
  type        = string
  default     = "initial"
  validation {
    condition     = can(regex("^[a-zA-Z0-9._-]{1,60}$", var.configuration_revision))
    error_message = "Use uma revisao publica de 1 a 60 caracteres alfanumericos, ponto, hifen ou underscore."
  }
}
variable "aws_account_id" {
  type = string
  validation {
    condition     = can(regex("^[0-9]{12}$", var.aws_account_id))
    error_message = "Informe a conta AWS de 12 digitos."
  }
}
variable "platform" {
  description = "Campos do contrato platform do infra-kubernetes."
  type = object({
    contract_version          = number
    aws_region                = string
    vpc_id                    = string
    private_subnet_ids        = list(string)
    lambda_security_group_ids = map(string)
  })
  validation {
    condition = (
      var.platform.contract_version == 1 && var.platform.aws_region == var.aws_region &&
      can(regex("^vpc-[0-9a-f]{8}([0-9a-f]{9})?$", var.platform.vpc_id)) &&
      length(toset(var.platform.private_subnet_ids)) >= 2 &&
      length(toset(var.platform.private_subnet_ids)) == length(var.platform.private_subnet_ids) &&
      alltrue([for id in var.platform.private_subnet_ids : can(regex("^subnet-[0-9a-f]{8}([0-9a-f]{9})?$", id))]) &&
      toset(keys(var.platform.lambda_security_group_ids)) == toset(["staging", "producao"]) &&
      length(toset(values(var.platform.lambda_security_group_ids))) == 2 &&
      alltrue([for id in values(var.platform.lambda_security_group_ids) : can(regex("^sg-[0-9a-f]{8}([0-9a-f]{9})?$", id))])
    )
    error_message = "Contrato platform v1 exige mesma regiao, VPC, duas sub-redes e SGs distintos por ambiente."
  }
}
variable "database" {
  description = "Campos publicos do contrato database do infra-database; nunca o segredo mestre."
  type = object({
    contract_version = number
    aws_region       = string
    vpc_id           = string
    address          = string
    port             = number
    planned_environments = map(object({
      branch        = string
      database_name = string
      auth_role     = string
    }))
  })
  validation {
    condition = (
      var.database.contract_version == 1 && var.database.aws_region == var.aws_region &&
      var.database.vpc_id == var.platform.vpc_id && var.database.port == 5432 &&
      can(regex("^[a-zA-Z0-9.-]+[.]rds[.]amazonaws[.]com$", var.database.address)) &&
      toset(keys(var.database.planned_environments)) == toset(["staging", "producao"]) &&
      try(var.database.planned_environments.staging.branch == "develop", false) &&
      try(var.database.planned_environments.producao.branch == "master", false) &&
      length(toset([for item in values(var.database.planned_environments) : item.database_name])) == 2 &&
      length(toset([for item in values(var.database.planned_environments) : item.auth_role])) == 2 &&
      alltrue([for item in values(var.database.planned_environments) :
      can(regex("^[a-z][a-z0-9_]{1,31}$", item.database_name)) && item.auth_role == "${item.database_name}_auth"])
    )
    error_message = "Contrato database v1 deve corresponder a VPC/regiao e separar bancos/roles auth staging/develop e producao/master."
  }
}
variable "jwt_issuer" {
  description = "URL HTTPS publica final do emissor, distinta por ambiente, sem barra final. Gateway sera preparado separadamente."
  type        = string
  validation {
    condition     = can(regex("^https://[a-zA-Z0-9][a-zA-Z0-9.-]*(:[0-9]+)?(/[a-zA-Z0-9._~-]+)*$", var.jwt_issuer))
    error_message = "Issuer deve ser HTTPS sem credenciais, query, fragmento ou barra final."
  }
}
variable "jwt_audience" {
  type    = string
  default = "oficina-api"
  validation {
    condition     = length(trimspace(var.jwt_audience)) > 0 && length(var.jwt_audience) <= 200
    error_message = "Audiencia deve ter entre 1 e 200 caracteres."
  }
}
variable "lambda_zip_path" {
  description = "ZIP linux-x64 gerado pelo publish; nao incluir PEM privado ou conexao."
  type        = string
  validation {
    condition     = endswith(var.lambda_zip_path, ".zip") && fileexists(var.lambda_zip_path)
    error_message = "Gere o ZIP da Lambda e informe seu caminho existente."
  }
}
variable "reserved_concurrency" {
  description = "Limita concorrencia; cada instancia abre no maximo cinco conexoes PostgreSQL."
  type        = number
  default     = 2
  validation {
    condition     = var.reserved_concurrency >= 1 && var.reserved_concurrency <= 5 && floor(var.reserved_concurrency) == var.reserved_concurrency
    error_message = "Use entre 1 e 5 execucoes simultaneas por ambiente."
  }
}
variable "api_gateway_execution_arn" {
  description = "Execution ARN do HTTP API deste ambiente; null mantem a Lambda sem permissao de invocacao pelo Gateway."
  type        = string
  default     = null
  validation {
    condition = var.api_gateway_execution_arn == null ? true : can(regex(
    "^arn:aws:execute-api:${var.aws_region}:${var.aws_account_id}:[a-z0-9]+$", var.api_gateway_execution_arn))
    error_message = "Use o execution ARN de uma unica API na mesma conta/regiao, sem curingas ou estagio."
  }
}
