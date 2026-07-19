# OIDC de GitHub Actions -> AWS (Fase 13). Sin access keys estaticas en GitHub
# Secrets: el workflow pide un token temporal a AWS presentando el JWT de OIDC que
# GitHub firma para cada run, y AWS lo valida contra este proveedor + el trust policy
# del rol de abajo (limitado a la rama main del repo).

resource "aws_iam_openid_connect_provider" "github_actions" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
  # AWS ya no valida realmente este campo para proveedores conocidos como GitHub,
  # pero la API igual lo exige. Thumbprints documentados por AWS para este proveedor.
  thumbprint_list = [
    "6938fd4d98bab03faadb97b34396831e3780aea1",
    "1c58a3a8518e8759bf075b76b750d4f2df264fcd",
  ]
}

data "aws_iam_policy_document" "github_actions_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github_actions.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Solo la rama main puede asumir el rol -- ni PRs ni otras ramas. Se aceptan
    # ambos formatos de "sub": el job build-and-plan (sin environment) manda
    # "ref:refs/heads/main"; el job apply (environment: production) manda
    # "environment:production" en su lugar -- GitHub cambia el claim segun si el
    # job tiene un Environment asignado o no.
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values = [
        "repo:susgleik/semantic-research:ref:refs/heads/main",
        "repo:susgleik/semantic-research:environment:production",
      ]
    }
  }
}

resource "aws_iam_role" "github_actions_deploy" {
  name               = "${var.project_prefix}-github-actions-deploy"
  assume_role_policy = data.aws_iam_policy_document.github_actions_assume_role.json
}

resource "aws_iam_role_policy" "github_actions_deploy" {
  name = "${var.project_prefix}-github-actions-deploy-policy"
  role = aws_iam_role.github_actions_deploy.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AppResources"
        Effect = "Allow"
        Action = [
          "lambda:*",
          "apigateway:*",
          "dynamodb:*",
          "s3:*",
          "cloudfront:*",
          "cognito-idp:*",
        ]
        Resource = "*"
      },
      {
        Sid    = "IamForLambdaRoles"
        Effect = "Allow"
        Action = [
          "iam:CreateRole",
          "iam:DeleteRole",
          "iam:GetRole",
          "iam:PutRolePolicy",
          "iam:DeleteRolePolicy",
          "iam:GetRolePolicy",
          "iam:AttachRolePolicy",
          "iam:DetachRolePolicy",
          "iam:ListRolePolicies",
          "iam:ListAttachedRolePolicies",
          "iam:TagRole",
          "iam:UntagRole",
        ]
        Resource = "arn:aws:iam::*:role/${var.project_prefix}-*"
      },
      {
        Sid      = "PassRoleToLambda"
        Effect   = "Allow"
        Action   = "iam:PassRole"
        Resource = "arn:aws:iam::*:role/${var.project_prefix}-*"
        Condition = {
          StringEquals = { "iam:PassedToService" = "lambda.amazonaws.com" }
        }
      },
      {
        Sid    = "TerraformStateBucket"
        Effect = "Allow"
        Action = ["s3:GetObject", "s3:PutObject", "s3:ListBucket"]
        Resource = [
          "arn:aws:s3:::semantic-search-tfstate-${local.account_id}",
          "arn:aws:s3:::semantic-search-tfstate-${local.account_id}/*",
        ]
      },
      {
        Sid      = "TerraformStateLock"
        Effect   = "Allow"
        Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:DeleteItem"]
        Resource = "arn:aws:dynamodb:${var.aws_region}:${local.account_id}:table/semantic-search-tfstate-lock"
      },
      {
        # Terraform necesita leer/gestionar el propio recurso OIDC provider
        # (aws_iam_openid_connect_provider.github_actions) en cada plan/apply.
        Sid    = "OidcProviderManagement"
        Effect = "Allow"
        Action = [
          "iam:GetOpenIDConnectProvider",
          "iam:CreateOpenIDConnectProvider",
          "iam:DeleteOpenIDConnectProvider",
          "iam:UpdateOpenIDConnectProviderThumbprint",
          "iam:TagOpenIDConnectProvider",
          "iam:UntagOpenIDConnectProvider",
        ]
        Resource = "arn:aws:iam::${local.account_id}:oidc-provider/token.actions.githubusercontent.com"
      },
      {
        Sid      = "OidcProviderList"
        Effect   = "Allow"
        Action   = ["iam:ListOpenIDConnectProviders"]
        Resource = "*"
      },
    ]
  })
}
