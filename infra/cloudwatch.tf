# Monitoreo básico (Fase 14): una alarma de errores por Lambda. Dentro del Always Free
# de CloudWatch (10 alarm metrics permanentes) y de SNS (1000 notificaciones por email
# por mes, permanente) — ver docs/architecture.md#cuenta-aws-y-costos.

locals {
  lambda_functions = {
    upload    = aws_lambda_function.upload.function_name
    indexer   = aws_lambda_function.indexer.function_name
    query     = aws_lambda_function.query.function_name
    documents = aws_lambda_function.documents.function_name
    reports   = aws_lambda_function.reports.function_name
  }

  alarm_notifications_enabled = var.alarm_email != ""
}

resource "aws_sns_topic" "alerts" {
  count = local.alarm_notifications_enabled ? 1 : 0
  name  = "${var.project_prefix}-alerts"
}

resource "aws_sns_topic_subscription" "alerts_email" {
  count     = local.alarm_notifications_enabled ? 1 : 0
  topic_arn = aws_sns_topic.alerts[0].arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

# "Sum de Errors >= 1 en 5 min" por función — el umbral más simple que tiene sentido
# para Lambdas de bajo tráfico académico (un solo error ya amerita mirar los logs).
resource "aws_cloudwatch_metric_alarm" "lambda_errors" {
  for_each = local.lambda_functions

  alarm_name          = "${var.project_prefix}-${each.key}-errors"
  alarm_description   = "Errores en la Lambda ${each.value} (>=1 en los últimos 5 min)"
  namespace           = "AWS/Lambda"
  metric_name         = "Errors"
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = each.value
  }

  alarm_actions = local.alarm_notifications_enabled ? [aws_sns_topic.alerts[0].arn] : []
  ok_actions    = local.alarm_notifications_enabled ? [aws_sns_topic.alerts[0].arn] : []
}
