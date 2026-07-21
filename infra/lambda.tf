locals {
  publish_dir = "${path.module}/publish"

  gemini_env = {
    GEMINI_API_KEY_SSM_PARAM    = var.gemini_ssm_parameter_name
    GEMINI_EMBEDDING_MODEL      = "gemini-embedding-001"
    GEMINI_CHAT_MODEL           = "gemini-flash-latest"
    GEMINI_EMBEDDING_DIMENSIONS = "768"
  }
}

# ---------------- upload-service ----------------

resource "aws_iam_role" "upload" {
  name               = "${var.project_prefix}-upload-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "upload_logs" {
  role       = aws_iam_role.upload.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "upload" {
  name = "${var.project_prefix}-upload-policy"
  role = aws_iam_role.upload.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["s3:PutObject"]
      Resource = "${aws_s3_bucket.docs.arn}/*"
    }]
  })
}

resource "aws_lambda_function" "upload" {
  function_name = "${var.project_prefix}-upload"
  role          = aws_iam_role.upload.arn
  runtime       = "dotnet8"
  handler       = "SemanticSearch.Functions.Upload::SemanticSearch.Functions.Upload.UploadFunction::FunctionHandler"
  timeout       = 30
  memory_size   = 512

  filename         = "${local.publish_dir}/UploadFunction.zip"
  source_code_hash = filebase64sha256("${local.publish_dir}/UploadFunction.zip")

  environment {
    variables = {
      S3_BUCKET_DOCS = aws_s3_bucket.docs.bucket
    }
  }
}

# ---------------- indexer-service ----------------

resource "aws_iam_role" "indexer" {
  name               = "${var.project_prefix}-indexer-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "indexer_logs" {
  role       = aws_iam_role.indexer.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "indexer" {
  name = "${var.project_prefix}-indexer-policy"
  role = aws_iam_role.indexer.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
        Resource = "${aws_s3_bucket.docs.arn}/*"
      },
      {
        Effect   = "Allow"
        Action   = ["dynamodb:PutItem", "dynamodb:BatchWriteItem", "dynamodb:DescribeTable"]
        Resource = aws_dynamodb_table.chunks.arn
      },
      {
        Effect   = "Allow"
        Action   = ["ssm:GetParameter"]
        Resource = "arn:aws:ssm:${var.aws_region}:${local.account_id}:parameter${var.gemini_ssm_parameter_name}"
      },
      {
        Effect   = "Allow"
        Action   = ["kms:Decrypt"]
        Resource = "arn:aws:kms:${var.aws_region}:${local.account_id}:alias/aws/ssm"
      }
    ]
  })
}

resource "aws_lambda_function" "indexer" {
  function_name = "${var.project_prefix}-indexer"
  role          = aws_iam_role.indexer.arn
  runtime       = "dotnet8"
  handler       = "SemanticSearch.Functions.Indexer::SemanticSearch.Functions.Indexer.IndexerFunction::FunctionHandler"
  timeout       = 60
  memory_size   = 512

  filename         = "${local.publish_dir}/IndexerFunction.zip"
  source_code_hash = filebase64sha256("${local.publish_dir}/IndexerFunction.zip")

  environment {
    variables = merge(local.gemini_env, {
      S3_BUCKET_DOCS      = aws_s3_bucket.docs.bucket
      DYNAMODB_TABLE_NAME = aws_dynamodb_table.chunks.name
    })
  }
}

# ---------------- query-service ----------------

resource "aws_iam_role" "query" {
  name               = "${var.project_prefix}-query-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "query_logs" {
  role       = aws_iam_role.query.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "query" {
  name = "${var.project_prefix}-query-policy"
  role = aws_iam_role.query.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["dynamodb:Scan", "dynamodb:DescribeTable"]
        Resource = aws_dynamodb_table.chunks.arn
      },
      {
        Effect = "Allow"
        # UpdateItem: DynamoDBContext.SaveAsync emite UpdateItem (no PutItem) por
        # default. Sin este permiso, cachear la respuesta tira AccessDenied y
        # rompe /query entero aunque Gemini ya haya respondido bien.
        Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:DescribeTable"]
        Resource = aws_dynamodb_table.query_cache.arn
      },
      {
        Effect   = "Allow"
        Action   = ["ssm:GetParameter"]
        Resource = "arn:aws:ssm:${var.aws_region}:${local.account_id}:parameter${var.gemini_ssm_parameter_name}"
      },
      {
        Effect   = "Allow"
        Action   = ["kms:Decrypt"]
        Resource = "arn:aws:kms:${var.aws_region}:${local.account_id}:alias/aws/ssm"
      }
    ]
  })
}

resource "aws_lambda_function" "query" {
  function_name = "${var.project_prefix}-query"
  role          = aws_iam_role.query.arn
  runtime       = "dotnet8"
  handler       = "SemanticSearch.Functions.Query::SemanticSearch.Functions.Query.QueryFunction::FunctionHandler"
  timeout       = 30
  memory_size   = 512

  filename         = "${local.publish_dir}/QueryFunction.zip"
  source_code_hash = filebase64sha256("${local.publish_dir}/QueryFunction.zip")

  environment {
    variables = merge(local.gemini_env, {
      DYNAMODB_TABLE_NAME     = aws_dynamodb_table.chunks.name
      QUERY_CACHE_TABLE_NAME  = aws_dynamodb_table.query_cache.name
      QUERY_CACHE_TTL_SECONDS = tostring(var.query_cache_ttl_seconds)
    })
  }
}

# ---------------- documents-service ----------------

resource "aws_iam_role" "documents" {
  name               = "${var.project_prefix}-documents-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "documents_logs" {
  role       = aws_iam_role.documents.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "documents" {
  name = "${var.project_prefix}-documents-policy"
  role = aws_iam_role.documents.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["dynamodb:Scan", "dynamodb:Query", "dynamodb:BatchWriteItem", "dynamodb:DeleteItem", "dynamodb:DescribeTable"]
        Resource = aws_dynamodb_table.chunks.arn
      },
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject", "s3:CopyObject", "s3:DeleteObject"]
        Resource = "${aws_s3_bucket.docs.arn}/*"
      }
    ]
  })
}

resource "aws_lambda_function" "documents" {
  function_name = "${var.project_prefix}-documents"
  role          = aws_iam_role.documents.arn
  runtime       = "dotnet8"
  handler       = "SemanticSearch.Functions.Documents::SemanticSearch.Functions.Documents.DocumentsFunction::FunctionHandler"
  timeout       = 30
  memory_size   = 512

  filename         = "${local.publish_dir}/DocumentsFunction.zip"
  source_code_hash = filebase64sha256("${local.publish_dir}/DocumentsFunction.zip")

  environment {
    variables = {
      DYNAMODB_TABLE_NAME = aws_dynamodb_table.chunks.name
      S3_BUCKET_DOCS      = aws_s3_bucket.docs.bucket
    }
  }
}

# ---------------- report-service ----------------

resource "aws_iam_role" "reports" {
  name               = "${var.project_prefix}-reports-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "reports_logs" {
  role       = aws_iam_role.reports.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "reports" {
  name = "${var.project_prefix}-reports-policy"
  role = aws_iam_role.reports.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["dynamodb:Scan", "dynamodb:DescribeTable"]
        Resource = aws_dynamodb_table.chunks.arn
      },
      {
        Effect   = "Allow"
        Action   = ["s3:PutObject", "s3:GetObject"]
        Resource = "${aws_s3_bucket.reports.arn}/*"
      },
      {
        Effect   = "Allow"
        Action   = ["ssm:GetParameter"]
        Resource = "arn:aws:ssm:${var.aws_region}:${local.account_id}:parameter${var.gemini_ssm_parameter_name}"
      },
      {
        Effect   = "Allow"
        Action   = ["kms:Decrypt"]
        Resource = "arn:aws:kms:${var.aws_region}:${local.account_id}:alias/aws/ssm"
      }
    ]
  })
}

resource "aws_lambda_function" "reports" {
  function_name = "${var.project_prefix}-reports"
  role          = aws_iam_role.reports.arn
  runtime       = "dotnet8"
  handler       = "SemanticSearch.Functions.Reports::SemanticSearch.Functions.Reports.ReportFunction::FunctionHandler"
  timeout       = 120
  memory_size   = 512

  filename         = "${local.publish_dir}/ReportFunction.zip"
  source_code_hash = filebase64sha256("${local.publish_dir}/ReportFunction.zip")

  environment {
    variables = merge(local.gemini_env, {
      DYNAMODB_TABLE_NAME = aws_dynamodb_table.chunks.name
      S3_BUCKET_REPORTS   = aws_s3_bucket.reports.bucket
    })
  }
}

# ---------------- shared ----------------

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}
