resource "aws_cognito_user_pool" "main" {
  name = "${var.project_prefix}-users"

  auto_verified_attributes = ["email"]

  password_policy {
    minimum_length    = 8
    require_lowercase = true
    require_uppercase = true
    require_numbers   = true
    require_symbols   = false
  }

  username_attributes = ["email"]

  admin_create_user_config {
    allow_admin_create_user_only = false
  }

  # Sin SES verificado, el correo de verificación que Cognito manda por default
  # nunca llega (sandbox de SES). Mientras tanto, el trigger pre-signup de Cognito
  # se enruta al Lambda upload-service ya existente (no se puede sumar un Lambda
  # nuevo a la arquitectura) — FunctionHandler detecta el evento de Cognito y
  # autoconfirma/autoverifica sin mandar correo. Ver UploadFunction.cs. Revertir
  # esto en cuanto se configure SES en producción.
  lambda_config {
    pre_sign_up = aws_lambda_function.upload.arn
  }
}

resource "aws_lambda_permission" "cognito_pre_signup" {
  statement_id  = "AllowCognitoInvokePreSignUp"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.upload.function_name
  principal     = "cognito-idp.amazonaws.com"
  source_arn    = aws_cognito_user_pool.main.arn
}

resource "aws_cognito_user_pool_client" "spa" {
  name         = "${var.project_prefix}-spa-client"
  user_pool_id = aws_cognito_user_pool.main.id

  generate_secret = false

  explicit_auth_flows = [
    "ALLOW_USER_SRP_AUTH",
    "ALLOW_REFRESH_TOKEN_AUTH",
    # Habilitado para poder probar el pipeline por CLI (admin-initiate-auth) sin
    # implementar SRP a mano. El frontend real (Fase 6/7) sigue usando Hosted UI/SRP.
    "ALLOW_ADMIN_USER_PASSWORD_AUTH",
  ]

  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["openid", "email", "profile"]

  callback_urls = distinct(concat(
    var.cognito_callback_urls,
    ["https://${aws_cloudfront_distribution.frontend.domain_name}"]
  ))
  logout_urls = distinct(concat(
    var.cognito_logout_urls,
    ["https://${aws_cloudfront_distribution.frontend.domain_name}"]
  ))

  supported_identity_providers = ["COGNITO"]
}

resource "aws_cognito_user_pool_domain" "hosted_ui" {
  domain       = "${var.project_prefix}-${data.aws_caller_identity.current.account_id}"
  user_pool_id = aws_cognito_user_pool.main.id
}
