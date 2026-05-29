resource "aws_secretsmanager_secret" "dapr_iot_api_key" {
  name                    = "dapr-iot-api-key"
  description             = "Sample API key for DaprIoT.Ingestor — demonstrates Dapr Secrets building block"
  recovery_window_in_days = 0 # immediate deletion on destroy (no 30-day window)

  tags = local.common_tags
}

resource "aws_secretsmanager_secret_version" "dapr_iot_api_key" {
  secret_id     = aws_secretsmanager_secret.dapr_iot_api_key.id
  secret_string = jsonencode({ "api-key" = "demo-key-replace-in-production" })
}
