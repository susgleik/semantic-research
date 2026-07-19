variable "aws_region" {
  description = "Región AWS de despliegue"
  type        = string
  default     = "us-east-1"
}

variable "project_prefix" {
  description = "Prefijo de nombre para todos los recursos (Lambdas, buckets, tablas, roles)"
  type        = string
  default     = "semantic-search"
}

variable "gemini_ssm_parameter_name" {
  description = "Nombre del parámetro SSM SecureString con la API key de Gemini (Fase 0)"
  type        = string
  default     = "/semantic-search/gemini-api-key"
}

variable "dynamodb_read_capacity" {
  description = "RCU provisionadas para la tabla chunks (Always Free hasta 25 combinadas)"
  type        = number
  default     = 5
}

variable "dynamodb_write_capacity" {
  description = "WCU provisionadas para la tabla chunks (Always Free hasta 25 combinadas)"
  type        = number
  default     = 5
}

variable "spa_dev_origin" {
  description = "Origen del frontend en desarrollo local, para permitir CORS de API Gateway/S3 mientras se prueba contra AWS real"
  type        = string
  default     = "http://localhost:5173"
}

variable "cognito_callback_urls" {
  description = "URLs de callback permitidas para el App Client de Cognito (Hosted UI). Se agrega el dominio de CloudFront automáticamente."
  type        = list(string)
  default     = ["http://localhost:5173"]
}

variable "cognito_logout_urls" {
  description = "URLs de logout permitidas para el App Client de Cognito (Hosted UI)"
  type        = list(string)
  default     = ["http://localhost:5173"]
}

variable "report_expiration_days" {
  description = "Días antes de expirar objetos del bucket reports"
  type        = number
  default     = 7
}

variable "query_cache_read_capacity" {
  description = "RCU provisionadas para la tabla query-cache (Always Free hasta 25 combinadas entre todas las tablas)"
  type        = number
  default     = 2
}

variable "query_cache_write_capacity" {
  description = "WCU provisionadas para la tabla query-cache (Always Free hasta 25 combinadas entre todas las tablas)"
  type        = number
  default     = 2
}

variable "query_cache_ttl_seconds" {
  description = "TTL corto para respuestas cacheadas de query-service (evita re-embeddear y re-generar con Gemini en preguntas repetidas)"
  type        = number
  default     = 600
}

variable "alarm_email" {
  description = "Email para notificaciones de las alarmas de CloudWatch (SNS). Vacío = las alarmas se crean igual (visibles en la consola) pero sin topic/subscription de SNS ni notificación"
  type        = string
  default     = ""
}
