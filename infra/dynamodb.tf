# PROVISIONED (no PAY_PER_REQUEST) para quedar dentro del tier Always Free de DynamoDB
# (25 RCU + 25 WCU combinadas, permanente) -- ver docs/architecture.md#cuenta-aws-y-costos.
# Claves iguales a las que ya escribe ChunkRecord.cs: DocumentId (hash), ChunkId (range).
resource "aws_dynamodb_table" "chunks" {
  name           = "${var.project_prefix}-chunks"
  billing_mode   = "PROVISIONED"
  read_capacity  = var.dynamodb_read_capacity
  write_capacity = var.dynamodb_write_capacity

  hash_key  = "DocumentId"
  range_key = "ChunkId"

  attribute {
    name = "DocumentId"
    type = "S"
  }

  attribute {
    name = "ChunkId"
    type = "S"
  }
}
