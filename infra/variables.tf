variable "project_name" {
  description = "Prefixo dos nomes planejados para as funcoes."
  type        = string
  default     = "oficina"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,29}$", var.project_name))
    error_message = "Use de 2 a 30 caracteres: letras minusculas, numeros e hifens, iniciando com letra."
  }
}

variable "environment" {
  description = "Ambiente de destino: staging (develop) ou producao (master)."
  type        = string
  default     = "staging"

  validation {
    condition     = contains(["staging", "producao"], var.environment)
    error_message = "O ambiente deve ser staging ou producao."
  }
}
