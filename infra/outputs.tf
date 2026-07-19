output "api_gateway_url" {
  description = "URL base de API Gateway (usar como VITE_API_URL en el frontend)"
  value       = aws_apigatewayv2_stage.default.invoke_url
}

output "cloudfront_domain" {
  description = "Dominio de la distribución CloudFront que sirve el frontend"
  value       = aws_cloudfront_distribution.frontend.domain_name
}

output "cloudfront_distribution_id" {
  description = "ID de la distribución CloudFront (para invalidaciones tras el deploy del frontend)"
  value       = aws_cloudfront_distribution.frontend.id
}

output "cognito_user_pool_id" {
  value = aws_cognito_user_pool.main.id
}

output "cognito_client_id" {
  value = aws_cognito_user_pool_client.spa.id
}

output "cognito_domain" {
  description = "Dominio del Hosted UI de Cognito"
  value       = "${aws_cognito_user_pool_domain.hosted_ui.domain}.auth.${var.aws_region}.amazoncognito.com"
}

output "s3_bucket_docs" {
  value = aws_s3_bucket.docs.bucket
}

output "s3_bucket_reports" {
  value = aws_s3_bucket.reports.bucket
}

output "s3_bucket_frontend" {
  value = aws_s3_bucket.frontend.bucket
}

output "dynamodb_table_name" {
  value = aws_dynamodb_table.chunks.name
}

output "sns_alerts_topic_arn" {
  description = "ARN del topic SNS de alarmas (vacío si no se configuró alarm_email)"
  value       = local.alarm_notifications_enabled ? aws_sns_topic.alerts[0].arn : null
}

output "github_actions_role_arn" {
  description = "ARN a pegar como variable AWS_DEPLOY_ROLE_ARN en GitHub Actions (Settings > Secrets and variables > Actions > Variables)"
  value       = aws_iam_role.github_actions_deploy.arn
}
