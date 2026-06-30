# Setup — Development y Production
## SemanticSearch RAG · .NET 8 / AWS Lambda

---

## Servicios externos — qué se puede emular y qué no

| Servicio | Emulable local | Herramienta | Necesita credenciales reales |
|---|---|---|---|
| S3 | SI | **LocalStack** (Docker, free/community) | No en dev |
| DynamoDB | SI | **DynamoDB Local** (Docker) o LocalStack | No en dev |
| Amazon Bedrock (embeddings + LLM) | **NO** | sin emulador oficial | **SI — siempre** |
| Amazon Cognito (auth JWT) | parcial | deshabilitado en dev | **SI en producción** |

> Para dev necesitás credenciales reales solo para Bedrock (no se puede evitar:
> no existe emulador de modelos de IA). S3 y DynamoDB corren completamente
> local con Docker, sin tocar la cuenta de AWS real ni gastar free tier.
>
> **Nota:** Bedrock requiere que el modelo (Titan Embed Text v2, Claude Haiku) esté
> habilitado en la consola de AWS antes del primer uso — es un paso manual de
> aprobación, no automático.

---

## Setup Development

### Prerrequisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Docker (para LocalStack / DynamoDB Local)
- AWS CLI instalado y configurado (`aws configure`)
- Una cuenta AWS con:
  - Acceso a Bedrock habilitado para los modelos `amazon.titan-embed-text-v2:0` y
    `anthropic.claude-3-haiku-20240307-v1:0` (consola → Bedrock → Model access)
  - Un usuario IAM con permisos de `bedrock:InvokeModel` (no usar el usuario root)

---

### Paso 1 — Levantar LocalStack (S3 + DynamoDB locales)

```bash
# Desde la root del repo
docker compose up -d localstack
```

Verificar que está corriendo:
```bash
docker compose ps
# localstack   Up   0.0.0.0:4566->4566/tcp
```

Crear el bucket y la tabla localmente (una sola vez):
```bash
aws --endpoint-url=http://localhost:4566 s3 mb s3://semantic-search-docs-dev

aws --endpoint-url=http://localhost:4566 dynamodb create-table \
  --table-name semantic-search-chunks \
  --attribute-definitions AttributeName=documentId,AttributeType=S AttributeName=chunkId,AttributeType=S \
  --key-schema AttributeName=documentId,KeyType=HASH AttributeName=chunkId,KeyType=RANGE \
  --billing-mode PAY_PER_REQUEST
```

El endpoint de LocalStack es siempre `http://localhost:4566` — las credenciales
pueden ser cualquier valor dummy (`test`/`test`), LocalStack no las valida.

---

### Paso 2 — Configurar credenciales con user-secrets

`dotnet user-secrets` guarda las credenciales **fuera del repositorio**, en tu
sistema local. Es el equivalente a un archivo `.env` que nunca se commitea.

**Dónde se guardan físicamente:**
```
Linux/Mac:  ~/.microsoft/usersecrets/semantic-search-functions-dev/secrets.json
Windows:    %APPDATA%\Microsoft\UserSecrets\semantic-search-functions-dev\secrets.json
```

**Cargar los secrets** (correlos desde el proyecto Lambda que corresponda, ej.
`src/SemanticSearch.Functions.Query/`):

```powershell
cd src/SemanticSearch.Functions.Query

# Bedrock — credenciales reales obligatorias (no hay emulador)
dotnet user-secrets set "Bedrock:Region" "us-east-1"
dotnet user-secrets set "Bedrock:EmbeddingModelId" "amazon.titan-embed-text-v2:0"
dotnet user-secrets set "Bedrock:ChatModelId" "anthropic.claude-3-haiku-20240307-v1:0"

# DynamoDB — apuntar a LocalStack en dev
dotnet user-secrets set "DynamoDb:ServiceUrl" "http://localhost:4566"
dotnet user-secrets set "DynamoDb:ChunksTableName" "semantic-search-chunks"

# S3 — apuntar a LocalStack en dev
dotnet user-secrets set "S3:ServiceUrl" "http://localhost:4566"
dotnet user-secrets set "S3:DocsBucket" "semantic-search-docs-dev"
```

Verificar que se guardaron:
```powershell
dotnet user-secrets list
```

> Las credenciales de AWS para Bedrock se resuelven con el **AWS credential
> provider chain** estándar (`aws configure`, variables de entorno, o un
> profile con `AWS_PROFILE`) — no van en `user-secrets`, eso es solo para
> configuración de la app (endpoints, nombres de tabla/bucket, model IDs).

---

### Referencia completa de comandos user-secrets

```powershell
dotnet user-secrets set "Bedrock:Region"            "us-east-1"
dotnet user-secrets set "Bedrock:EmbeddingModelId"  "amazon.titan-embed-text-v2:0"
dotnet user-secrets set "Bedrock:ChatModelId"       "anthropic.claude-3-haiku-20240307-v1:0"
dotnet user-secrets set "DynamoDb:ServiceUrl"       "http://localhost:4566"
dotnet user-secrets set "DynamoDb:ChunksTableName"  "semantic-search-chunks"
dotnet user-secrets set "S3:ServiceUrl"             "http://localhost:4566"
dotnet user-secrets set "S3:DocsBucket"             "semantic-search-docs-dev"

# Listar
dotnet user-secrets list

# Eliminar un secret específico
dotnet user-secrets remove "Bedrock:Region"

# Eliminar TODOS los secrets del proyecto
dotnet user-secrets clear
```

> Este archivo vive **fuera del repo** y nunca se commitea.
> Si cambiás de máquina tenés que volver a correr los `dotnet user-secrets set`.

---

### Paso 3 — Invocar las Lambdas localmente

Con AWS SAM CLI se puede invocar cada función localmente sin desplegar:

```powershell
sam build

# Invocar el Lambda de query con un evento de prueba
sam local invoke QueryFunction --event events/query-event.json

# O levantar el API Gateway completo localmente
sam local start-api
# disponible en http://localhost:3000
```

`sam local start-api` simula el API Gateway HTTP API completo, incluyendo el
ruteo hacia cada Lambda — es el equivalente local más cercano a producción.

---

### Resumen del entorno Development

```
tu máquina
├── Docker
│   └── LocalStack (puerto 4566) ← S3 + DynamoDB locales, sin credenciales reales
├── sam local start-api (puerto 3000)
│   └── lee user-secrets de cada proyecto Lambda
└── Servicios externos (cloud, reales)
    └── Amazon Bedrock ← credencial real vía AWS credential chain (no hay emulador)
```

---

## Setup Production

### Prerequisitos

- AWS CLI configurado con un usuario/rol con permisos de deploy
- AWS SAM CLI instalado (`sam --version`)
- Acceso a Bedrock habilitado en la región de deploy

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
| Auth Cognito | deshabilitada (o User Pool de pruebas) | activa (JWT Authorizer obligatorio en API Gateway) |
| S3 / DynamoDB | LocalStack (Docker, local) | recursos reales de AWS |
| Bedrock | real (no hay emulador) | real |
| Credenciales | `user-secrets` + AWS credential chain local | Secrets Manager / SSM Parameter Store |
| Invocación | `sam local start-api` | API Gateway + Lambda desplegados |
| Logging | Debug, consola local | CloudWatch Logs (Information/Warning) |
| Frontend | `npm run dev` (Vite dev server) | build estático en S3 + CloudFront |
