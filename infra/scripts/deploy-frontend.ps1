<#
.SYNOPSIS
    Build de produccion del frontend (React/Vite) + sync a S3 + invalidacion de
    CloudFront. Lee bucket y distribution ID desde `terraform output`, asi que
    requiere haber corrido `terraform apply` al menos una vez antes.
#>

$ErrorActionPreference = "Stop"

$repoRoot    = Resolve-Path "$PSScriptRoot/../.."
$frontendDir = Join-Path $repoRoot "frontend"
$infraDir    = Join-Path $repoRoot "infra"

Write-Host "==> Leyendo outputs de Terraform"
Push-Location $infraDir
try {
    $bucket         = terraform output -raw s3_bucket_frontend
    $distributionId = terraform output -raw cloudfront_distribution_id
} finally {
    Pop-Location
}

Write-Host "    bucket: $bucket"
Write-Host "    distribution: $distributionId"

Write-Host "==> Build de produccion (npm run build, modo prod -> .env.production)"
Push-Location $frontendDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "npm run build fallo"
    }
} finally {
    Pop-Location
}

$distDir = Join-Path $frontendDir "dist"

Write-Host "==> Sync a s3://$bucket"
aws s3 sync $distDir "s3://$bucket/" --delete
if ($LASTEXITCODE -ne 0) {
    throw "aws s3 sync fallo"
}

Write-Host "==> Invalidando cache de CloudFront ($distributionId)"
aws cloudfront create-invalidation --distribution-id $distributionId --paths "/*"
if ($LASTEXITCODE -ne 0) {
    throw "aws cloudfront create-invalidation fallo"
}

Write-Host "Listo."
