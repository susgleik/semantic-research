# Terraform — setup y flujo de despliegue (Fase 10)

Prerequisitos (Fase 0, ya resueltos): cuenta AWS, usuario/grupo IAM, `aws configure`
apuntando a `us-east-1`, API key de Gemini en SSM (`/semantic-search/gemini-api-key`).

## 1. Bootstrap del backend remoto (una sola vez)

El backend `s3` de `infra/backend.tf` necesita que el bucket de state y la tabla de
lock **ya existan** antes del primer `terraform init` (Terraform no puede crear su
propio backend). Se crean una única vez con AWS CLI — no está en el `.tf` porque no
tiene sentido gestionar con Terraform algo que Terraform necesita para arrancar:

```powershell
$accountId = (aws sts get-caller-identity --query Account --output text)
# accountId actual del proyecto: 491024724951

aws s3api create-bucket --bucket "semantic-search-tfstate-$accountId" --region us-east-1
aws s3api put-bucket-versioning --bucket "semantic-search-tfstate-$accountId" --versioning-configuration Status=Enabled
aws s3api put-public-access-block --bucket "semantic-search-tfstate-$accountId" `
  --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true

aws dynamodb create-table --table-name semantic-search-tfstate-lock `
  --attribute-definitions AttributeName=LockID,AttributeType=S `
  --key-schema AttributeName=LockID,KeyType=HASH `
  --billing-mode PAY_PER_REQUEST --region us-east-1
```

Si el nombre de bucket no coincide con el hardcodeado en `infra/backend.tf`
(`semantic-search-tfstate-491024724951`), actualizá ese archivo antes de `terraform init`.

## 2. Construir los paquetes de los Lambdas

Terraform no compila C# — necesita un `.zip` por función ya publicado. Correr (y
repetir cada vez que cambie código de algún Lambda):

```powershell
./infra/scripts/build-lambdas.ps1
```

Esto deja `infra/publish/{Upload,Indexer,Query,Documents,Report}Function.zip`
(gitignored — se regeneran, no se commitean).

## 3. Flujo de Terraform

```powershell
cd infra
terraform init
terraform validate
terraform plan -out=tfplan
```

Revisar el plan con cuidado (recursos a crear, nombres de bucket únicos, IAM). Recién
después:

```powershell
terraform apply tfplan
```

`terraform apply` crea recursos reales y facturables (aunque a la escala de este
proyecto son centavos) — no correrlo sin haber revisado el plan.

## 4. Después del primer apply

- `terraform output api_gateway_url` → usar como `VITE_API_URL` del frontend
  (Fase 7/13).
- `terraform output cognito_user_pool_id` / `cognito_client_id` / `cognito_domain` →
  completar `VITE_COGNITO_*` en `frontend/.env` (Fase 6/7, integración de login
  pendiente).
- Subir el build de `frontend/` al bucket `s3_bucket_frontend` e invalidar CloudFront
  (Fase 13/14, todavía no automatizado).

## 5. Variables

Ver `infra/variables.tf` para el detalle; la mayoría tiene default razonable. Si
querés pisar alguna, copiá `infra/terraform.tfvars.example` a `infra/terraform.tfvars`
(gitignored) y ajustá.

## 6. CI/CD (Fase 13) — setup manual de GitHub, una sola vez

`infra/oidc.tf` crea el proveedor OIDC + el rol `semantic-search-github-actions-deploy`
que los workflows de `.github/workflows/` asumen sin ninguna access key guardada en
GitHub. Falta este setup manual en la UI de GitHub (no se puede hacer con Terraform):

1. **Settings → Secrets and variables → Actions → pestaña Variables** → *New repository
   variable* → nombre `AWS_DEPLOY_ROLE_ARN`, valor el output `github_actions_role_arn`
   (`terraform output github_actions_role_arn`). Es un ARN, no un secreto — va como
   variable, no como secret.
2. **Settings → Environments → New environment** → nombre `production` → en
   "Deployment protection rules" agregar **Required reviewers** con tu propio usuario.
   Esto hace que el job `apply` de `.github/workflows/deploy.yml` quede en pausa
   pidiendo tu aprobación antes de tocar la cuenta AWS real.

Con eso: `ci.yml` corre build+test en cada push/PR (sin credenciales AWS), y
`deploy.yml` en cada push a `main` corre build+test+`terraform plan` solo, y espera tu
aprobación manual en la pestaña **Actions** antes del `terraform apply`.
