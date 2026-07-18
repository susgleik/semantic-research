<#
.SYNOPSIS
    Publica los 5 Lambdas de .NET y arma los zips que Terraform sube a AWS.
    Correr desde la raiz del repo o desde infra/scripts/ antes de cada
    `terraform plan`/`apply` que incluya un cambio de codigo.
#>

$ErrorActionPreference = "Stop"

$repoRoot   = Resolve-Path "$PSScriptRoot/../.."
$publishDir = Join-Path (Resolve-Path "$PSScriptRoot/..") "publish"

if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir | Out-Null
}

$functions = @(
    @{ Project = "SemanticSearch.Functions.Upload";    Zip = "UploadFunction.zip" }
    @{ Project = "SemanticSearch.Functions.Indexer";   Zip = "IndexerFunction.zip" }
    @{ Project = "SemanticSearch.Functions.Query";     Zip = "QueryFunction.zip" }
    @{ Project = "SemanticSearch.Functions.Documents"; Zip = "DocumentsFunction.zip" }
    @{ Project = "SemanticSearch.Functions.Reports";   Zip = "ReportFunction.zip" }
)

foreach ($fn in $functions) {
    $projectDir = Join-Path $repoRoot "src/$($fn.Project)"
    $publishOut = Join-Path $repoRoot "src/$($fn.Project)/bin/publish-lambda"
    $zipPath    = Join-Path $publishDir $fn.Zip

    Write-Host "==> Publicando $($fn.Project)"

    if (Test-Path $publishOut) {
        Remove-Item -Recurse -Force $publishOut
    }

    dotnet publish $projectDir -c Release -o $publishOut --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish fallo para $($fn.Project)"
    }

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Compress-Archive -Path "$publishOut/*" -DestinationPath $zipPath
    Write-Host "    -> $zipPath"
}

Write-Host "Listo. Zips en $publishDir"
