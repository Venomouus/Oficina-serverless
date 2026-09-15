variable "academy_role_arn" {
  description = "Somente Academy: role preexistente. null preserva IAM/IRSA proprios."
  type        = string
  default     = null
  validation {
    condition     = var.academy_role_arn == null ? true : can(regex("^arn:aws:iam::${var.aws_account_id}:role/[A-Za-z0-9_+=,.@/-]+$", var.academy_role_arn))
    error_message = "A role Academy deve pertencer a conta configurada."
  }
}

moved {
  from = aws_iam_role.authentication
  to   = aws_iam_role.authentication[0]
}
moved {
  from = aws_iam_role_policy.authentication
  to   = aws_iam_role_policy.authentication[0]
}
