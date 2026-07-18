# Backend remoto para el state de Terraform. Bucket + tabla de lock se crean UNA VEZ
# con comandos AWS CLI puntuales -- ver docs/terraform-setup.md -- antes del primer
# `terraform init`. Terraform no permite variables/interpolación en este bloque, por
# eso los valores van hardcodeados (atados a la cuenta AWS real del proyecto).
terraform {
  backend "s3" {
    bucket         = "semantic-search-tfstate-491024724951"
    key            = "semantic-search/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "semantic-search-tfstate-lock"
    encrypt        = true
  }
}
