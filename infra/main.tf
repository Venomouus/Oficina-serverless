# Base de nomenclatura. Ainda nao ha provider AWS nem recursos provisionaveis.
# Funcoes, IAM, fila e DLQ serao adicionados junto aos handlers e adaptadores AWS.
locals {
  function_names = {
    autenticacao = "${var.project_name}-${var.environment}-autenticacao"
    notificacoes = "${var.project_name}-${var.environment}-notificacoes"
  }
}
