data "aws_caller_identity" "current" {}

# --- IRSA trust policy helper ---
data "aws_iam_policy_document" "irsa_assume" {
  for_each = toset(["ingestor", "processor"])

  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [module.eks.oidc_provider_arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${module.eks.oidc_provider}:sub"
      values   = ["system:serviceaccount:dapr-iot:dapr-iot-${each.key}"]
    }
    condition {
      test     = "StringEquals"
      variable = "${module.eks.oidc_provider}:aud"
      values   = ["sts.amazonaws.com"]
    }
  }
}

# --- Ingestor role: Secrets Manager ---
resource "aws_iam_role" "ingestor" {
  name               = "dapr-iot-ingestor-role"
  assume_role_policy = data.aws_iam_policy_document.irsa_assume["ingestor"].json
  tags               = local.common_tags
}

resource "aws_iam_role_policy" "ingestor" {
  name = "dapr-iot-ingestor-policy"
  role = aws_iam_role.ingestor.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
        Resource = aws_secretsmanager_secret.dapr_iot_api_key.arn
      }
    ]
  })
}

# --- Processor role: DynamoDB (Actor + Workflow state) ---
resource "aws_iam_role" "processor" {
  name               = "dapr-iot-processor-role"
  assume_role_policy = data.aws_iam_policy_document.irsa_assume["processor"].json
  tags               = local.common_tags
}

resource "aws_iam_role_policy" "processor" {
  name = "dapr-iot-processor-policy"
  role = aws_iam_role.processor.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem",
          "dynamodb:DeleteItem", "dynamodb:Query", "dynamodb:Scan",
          "dynamodb:BatchWriteItem", "dynamodb:DescribeTable"
        ]
        Resource = aws_dynamodb_table.dapr_iot_state.arn
      }
    ]
  })
}
