resource "aws_dynamodb_table" "dapr_iot_state" {
  name         = "dapr-iot-state"
  billing_mode = "PAY_PER_REQUEST"   # on-demand — no minimum charge
  hash_key     = "key"

  attribute {
    name = "key"
    type = "S"
  }

  ttl {
    attribute_name = "ttl"
    enabled        = true
  }

  tags = local.common_tags
}
