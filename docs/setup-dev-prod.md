# Setup — Development y Production
## SemanticSearch RAG · .NET 8 / AWS Lambda

---

## Servicios externos — qué se puede emular y qué no

| Servicio | Emulable local | Herramienta | Necesita credenciales reales |
|---|---|---|---|
| S3 | SI | **LocalStack** (Docker, free/community, pineada a `3.8`) | No en dev |
| DynamoDB | SI | **DynamoDB Local** (Docker, modo `-inMemory`) | No en dev |
| Google Gemini API (embeddings + LLM) | **NO** | sin emulador oficial | **SI — siempre** |
| Amazon Cognito (auth JWT) | no implementado todavía | — | **SI en producción** (Fase 6) |

> Para dev necesitás una API key real de Gemini (no se puede evitar: no existe
> emulador de modelos de IA). S3 y DynamoDB corren completamente local con
> Docker, sin tocar la cuenta de AWS real.
>
> **Ver [`docs/local-development.md`](local-development.md) para la guía completa
> y actualizada** del entorno local — instalación de AWS CLI/SAM CLI, comandos
> exactos usados en este proyecto, y los problemas reales que aparecieron al
> levantarlo (con su solución). Lo que sigue en este archivo es un resumen; el
> otro documento tiene el detalle verificado.

---

## Setup Development

### Prerrequisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (además de cualquier otra versión que ya tengas — Lambda no soporta net10.0 nativamente)
- Docker Desktop (para LocalStack / DynamoDB Local)
- AWS CLI + AWS SAM CLI (`winget install Amazon.AWSCLI`, `winget install Amazon.SAM-CLI`)
- Una API key de **Google Gemini en tier de pago** (no la gratuita de AI Studio —
  ver razonamiento en [`CLAUDE.md`](../CLAUDE.md))

### Configuración: variables de entorno, no `user-secrets`

Los Lambdas de este proyecto leen su configuración directo de variables de entorno
(`Environment.GetEnvironmentVariable(...)` en el método `LoadXOptions()` de cada
handler) — no usan `dotnet user-secrets` ni el patrón `IOptions<T>` con un host de
ASP.NET Core, porque no hay contenedor de DI (ver "Convenciones de código" en
[`CLAUDE.md`](../CLAUDE.md)). En local, esas variables se inyectan vía
`template.local.yaml` (las no-secretas) + `env.local.json` (la API key de Gemini,
gitignored).

**La guía completa y verificada del entorno local — instalación, comandos exactos,
y los problemas reales que aparecieron al levantarlo — está en
[`docs/local-development.md`](local-development.md).** Resumen rápido:

```powershell
docker compose up -d
sam build --template template.local.yaml
sam local start-api --template .aws-sam/build/template.yaml --docker-network semantic-search-net --env-vars env.local.json
```

---

## Setup Production

### Prerequisitos

- AWS CLI configurado con un usuario/rol con permisos de deploy
- AWS SAM CLI instalado (`sam --version`)
- API key de Gemini (tier de pago) guardada en SSM Parameter Store (SecureString)

---

### Paso 1 — Build y deploy con AWS SAM

```powershell
sam build

# Primer deploy: modo guiado, genera samconfig.toml
sam deploy --guided

# Deploys siguientes
sam deploy
```

`sam deploy` se encarga de empaquetar cada Lambda, crear/actualizar el stack de
CloudFormation con todos los recursos (S3, DynamoDB, API Gateway, Cognito) y
publicar el código — no hay un paso separado de "build de imagen + push a
registry" como en Container Apps.

---

### Paso 2 — Deploy del frontend

```powershell
cd frontend
npm run build

aws s3 sync dist/ s3://semantic-search-frontend-<account-id> --delete

aws cloudfront create-invalidation \
  --distribution-id <DISTRIBUTION_ID> \
  --paths "/*"
```

---

### Paso 3 — Verificar el deploy

```powershell
# URL del API Gateway queda en los Outputs del stack
aws cloudformation describe-stacks --stack-name semantic-search --query "Stacks[0].Outputs"

curl https://xxxxxxxxxx.execute-api.us-east-1.amazonaws.com/health
```

En producción las credenciales/secretos van en **AWS Secrets Manager** o
**SSM Parameter Store**, referenciados desde la configuración de cada Lambda
(variables de entorno que apuntan al ARN del secret) — nunca como texto plano.

---

### Diferencias clave entre entornos

| | Development | Production |
|---|---|---|
| Auth Cognito | no implementada todavía (Fase 6) | activa (JWT Authorizer obligatorio en API Gateway) |
| S3 / DynamoDB | LocalStack + DynamoDB Local (Docker) | recursos reales de AWS |
| Gemini API | real (no hay emulador) — key inyectada vía `env.local.json` | real — key en SSM Parameter Store |
| Credenciales | variables de entorno vía `template.local.yaml` + `env.local.json` | SSM Parameter Store / Secrets Manager |
| Invocación | `sam local start-api` (ver [`docs/local-development.md`](local-development.md)) | API Gateway + Lambda desplegados |
| Logging | Debug, consola local | CloudWatch Logs (Information/Warning) |
| Frontend | `npm run dev` (Vite dev server) | build estático en S3 + CloudFront |
