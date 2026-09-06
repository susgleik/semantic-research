# Desarrollo local — CLI de AWS, SAM CLI y Docker Compose

Referencia completa del entorno local (Fase 12 de [`TODO.md`](../TODO.md)): cómo se
instala, qué hace cada herramienta, qué comandos se usan realmente en este proyecto,
y los problemas concretos que aparecieron al levantarlo (con su solución). Todo lo
documentado acá fue probado de punta a punta — no es un plan teórico.

Ver [`docs/architecture.md`](architecture.md#entorno-local-docker-compose--sam-cli--desarrollo-sin-aws)
para el diagrama y la comparación pieza por pieza con AWS real.

---

## 1. Qué hace cada herramienta

| Herramienta | Rol en este proyecto |
|---|---|
| **Docker / Docker Compose** | Levanta LocalStack (S3) y DynamoDB Local — los dos únicos servicios de datos que se pueden emular. Gemini nunca se emula. |
| **AWS CLI** | Cliente de línea de comandos para hablarle a LocalStack/DynamoDB Local (crear buckets, tablas, inspeccionar datos) — **no** se usa para tocar la cuenta real de AWS en este flujo. |
| **AWS SAM CLI** | Compila cada Lambda (`sam build`) y emula API Gateway + el runtime de Lambda (`sam local start-api` / `sam local invoke`), usando contenedores del Runtime Interface Emulator (RIE) por debajo. Es la misma herramienta que se usará para el deploy real. |

---

## 2. Instalación

Windows, vía `winget` (PowerShell):

```powershell
winget install Amazon.SAM-CLI --accept-package-agreements --accept-source-agreements
winget install Amazon.AWSCLI --accept-package-agreements --accept-source-agreements
```

Verificar (en una terminal **nueva** — el PATH no se actualiza en sesiones ya abiertas):

```powershell
sam --version
aws --version
```

También hace falta el **SDK de .NET 8** (no solo el 10 que ya tengas — Lambda no
soporta net10.0 nativamente; ver [`CLAUDE.md`](../CLAUDE.md)):

```powershell
winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
```

Los tres conviven sin conflicto con cualquier otra versión ya instalada (SDKs de
.NET side-by-side, AWS CLI/SAM CLI son binarios independientes).

---

## 3. AWS CLI — configuración y comandos usados acá

### Credenciales

LocalStack **no valida** las credenciales, pero el AWS CLI igual exige que existan
para armar la request. Cualquier valor sirve:

```powershell
$env:AWS_ACCESS_KEY_ID = "local"
$env:AWS_SECRET_ACCESS_KEY = "local"
$env:AWS_DEFAULT_REGION = "us-east-1"
```

Estas variables solo viven en la sesión de terminal actual — hay que volver a
setearlas en cada terminal nueva (o agregarlas a tu perfil de PowerShell si te
cansás de repetirlo).

### Comandos de S3 (contra LocalStack, puerto 4566)

Todos requieren `--endpoint-url=http://localhost:4566` para no pegarle a AWS real:

```powershell
# Crear un bucket
aws --endpoint-url=http://localhost:4566 s3 mb s3://docs

# Listar objetos (recursivo)
aws --endpoint-url=http://localhost:4566 s3 ls s3://docs/ --recursive

# Bajar un objeto puntual
aws --endpoint-url=http://localhost:4566 s3api get-object --bucket docs --key "contratos/<docId>/archivo.pdf" salida.pdf

# Vaciar el bucket completo (útil para resetear el entorno de pruebas)
aws --endpoint-url=http://localhost:4566 s3 rm s3://docs --recursive
```

### Comandos de DynamoDB (contra DynamoDB Local, puerto 8000)

```powershell
# Crear la tabla (la crea automáticamente el contenedor `setup` de docker-compose,
# esto es solo referencia si hay que recrearla a mano)
aws --endpoint-url=http://localhost:8000 dynamodb create-table `
  --table-name chunks `
  --attribute-definitions AttributeName=DocumentId,AttributeType=S AttributeName=ChunkId,AttributeType=S `
  --key-schema AttributeName=DocumentId,KeyType=HASH AttributeName=ChunkId,KeyType=RANGE `
  --billing-mode PAY_PER_REQUEST

# Ver todos los items
aws --endpoint-url=http://localhost:8000 dynamodb scan --table-name chunks --select ALL_ATTRIBUTES

# Contar items sin traer los datos
aws --endpoint-url=http://localhost:8000 dynamodb scan --table-name chunks --select COUNT

# Borrar un item puntual
aws --endpoint-url=http://localhost:8000 dynamodb delete-item --table-name chunks `
  --key '{"DocumentId":{"S":"<docId>"},"ChunkId":{"S":"chunk-000000"}}'

# Listar tablas (útil para confirmar que el contenedor ya está listo)
aws --endpoint-url=http://localhost:8000 dynamodb list-tables
```

> **Nota:** S3 usa el endpoint `4566` (LocalStack) y DynamoDB usa el `8000`
> (DynamoDB Local) — son dos contenedores/puertos distintos, no confundir.

---

## 4. AWS SAM CLI — configuración y comandos usados acá

### `template.local.yaml`

Vive en la raíz del repo. Define los Lambdas HTTP (`UploadFunction`, `QueryFunction`,
`DocumentsFunction`, `ReportsFunction`) con sus rutas, más `IndexerFunction` sin ruta
HTTP (se invoca a mano, ver más abajo). Las variables de entorno no-secretas
(región, endpoints de LocalStack/DynamoDB Local, nombres de bucket/tabla, modelos de
Gemini) están en `Globals.Function.Environment.Variables`.

`GEMINI_API_KEY` está declarada ahí con valor vacío (`""`) a propósito — es un
placeholder necesario (ver gotcha en la sección 7) para que `--env-vars` pueda
sobrescribirla con el valor real.

**Corrección (2026-09):** este archivo afirmaba que había un
`Globals.HttpApi.CorsConfiguration` en `template.local.yaml` — nunca existió (ver
`git log -S "CorsConfiguration"`, ni siquiera en el commit que agregó esta frase). No
hace falta: `sam local start-api` es un emulador liviano, no la API Gateway real, y
no aplica las reglas de CORS de HTTP API sobre el preflight `OPTIONS` como sí hace
AWS real — por eso `/documents`, `/query`, etc. nunca tuvieron problema de CORS desde
el frontend, con o sin esa config. El único CORS que sí es real y enforced en local
es el del **bucket S3** (sección 7, "Falta CORS en el bucket S3"), porque LocalStack
sí emula S3 fielmente.

### `env.local.json` (no se commitea)

```powershell
copy env.local.example.json env.local.json
notepad env.local.json
```

Formato: un objeto por Lambda (usa el **logical ID** de `template.local.yaml`, ej.
`IndexerFunction`), con las variables que querés sobrescribir — en este caso, solo
la API key real de Gemini:

```json
{
  "IndexerFunction": { "GEMINI_API_KEY": "tu-key-real" },
  "QueryFunction":   { "GEMINI_API_KEY": "tu-key-real" }
}
```

### Comandos

```powershell
# 1. Compilar las 4 Lambdas (publica cada proyecto .NET y arma el paquete de despliegue)
sam build --template template.local.yaml

# 2. Levantar el gateway HTTP local (equivalente a API Gateway + las 4 Lambdas)
sam local start-api --template .aws-sam/build/template.yaml --docker-network semantic-search-net --env-vars env.local.json
# queda escuchando en http://127.0.0.1:3000, en primer plano (Ctrl+C para cortar)

# 3. Invocar un Lambda puntual con un evento de ejemplo (para IndexerFunction, que no tiene ruta HTTP)
sam local invoke IndexerFunction --template .aws-sam/build/template.yaml --event events/s3-put-event.json --docker-network semantic-search-net --env-vars env.local.json
```

**Flags que importan y por qué:**

| Flag | Por qué hace falta |
|---|---|
| `--template .aws-sam/build/template.yaml` | Apunta al template **ya compilado**, no al fuente (`template.local.yaml`). Si le pasás el fuente, SAM monta el código sin publicar (falta `.deps.json`, falla con "runtime exited with error 105"). |
| `--docker-network semantic-search-net` | Sin esto, el contenedor de la Lambda queda aislado y no puede resolver `localstack`/`dynamodb-local` por nombre — tiene que estar en la misma red que levantó `docker-compose.yml`. |
| `--env-vars env.local.json` | Inyecta la API key de Gemini sin commitearla. Solo sobrescribe variables que **ya existen** como clave en el template — de ahí el placeholder `GEMINI_API_KEY: ""` en `template.local.yaml`. |

### Eventos de prueba (`events/`)

| Archivo | Para qué Lambda | Notas |
|---|---|---|
| `s3-put-event.json` | `IndexerFunction` | Hay que actualizar `key` y `size` a mano con el `docId`/tamaño real de cada archivo subido antes de invocar |
| `query-event.json` | `QueryFunction` | Body fijo, se puede editar la pregunta directamente en el JSON |
| `documents-list-event.json` | `DocumentsFunction` | `GET /documents` sin parámetros |
| `contrato-prueba.pdf` | — | PDF mínimo pero válido (con tabla xref correcta) para probar extracción de texto real con PdfPig |

---

## 5. Docker Compose — servicios

```powershell
docker compose up -d      # levanta LocalStack + DynamoDB Local + setup (bucket/tabla)
docker compose ps         # ver estado (setup debería quedar Exited(0) — corre una vez y termina, es normal)
docker compose logs -f localstack     # logs en vivo de S3
docker compose logs -f dynamodb-local # logs en vivo de DynamoDB
docker compose down -v    # bajar todo y borrar volúmenes (reset completo)
```

| Servicio | Imagen | Notas de configuración |
|---|---|---|
| `localstack` | `localstack/localstack:3.8` (pineada, **no** `latest`) | `LOCALSTACK_AUTH_TOKEN: ""` explícito, `SERVICES: s3` |
| `dynamodb-local` | `amazon/dynamodb-local:latest` | Corre con `-inMemory` (sin volumen persistente) |
| `setup` | `amazon/aws-cli:latest` | Corre una vez al levantar el stack: crea bucket `docs`/`reports` + tabla `chunks`; es idempotente (usa `|| true`) |

Los tres comparten la red `semantic-search-net`, la misma que se le pasa a SAM CLI
con `--docker-network` para que los Lambdas puedan resolver `localstack` y
`dynamodb-local` por nombre de contenedor.

---

## 6. Flujo completo, de punta a punta

```powershell
# Terminal 1 — stack de datos
docker compose up -d

# Terminal 1 — compilar y levantar el gateway
sam build --template template.local.yaml
sam local start-api --template .aws-sam/build/template.yaml --docker-network semantic-search-net --env-vars env.local.json
# (queda corriendo, dejar abierta)

# Terminal 2 — probar el flujo
$env:AWS_ACCESS_KEY_ID = "local"; $env:AWS_SECRET_ACCESS_KEY = "local"; $env:AWS_DEFAULT_REGION = "us-east-1"

# 1) pedir URL de subida
$upload = Invoke-RestMethod -Uri http://127.0.0.1:3000/upload -Method Post -ContentType "application/json" `
  -Body '{"filename":"contrato.pdf","category":"contratos","contentType":"application/pdf"}'

# 2) subir el archivo (ver gotcha de --resolve/-k en la sección 7)
curl.exe --resolve localstack:4566:127.0.0.1 -k -X PUT $upload.uploadUrl -H "Content-Type: application/pdf" --data-binary "@events/contrato-prueba.pdf"

# 3) indexar (actualizar events/s3-put-event.json con $upload.docId antes de esto)
sam local invoke IndexerFunction --template .aws-sam/build/template.yaml --event events/s3-put-event.json --docker-network semantic-search-net --env-vars env.local.json

# 4) preguntar
Invoke-RestMethod -Uri http://127.0.0.1:3000/query -Method Post -ContentType "application/json" -Body '{"query":"de que trata el contrato?","topK":3}'

# 5) listar documentos indexados
Invoke-RestMethod -Uri http://127.0.0.1:3000/documents
```

---

## 7. Problemas reales encontrados (y su solución)

Cada uno de estos pasó en esta sesión de trabajo — quedan documentados para no
volver a perder tiempo re-diagnosticándolos.

### LocalStack pide licencia Pro (`License activation failed`)
La imagen `localstack/localstack:latest` cambió de comportamiento en algún punto y
empieza a intentar activar una licencia Pro incluso para S3 (que es 100%
community/gratis). **Solución:** pinear la imagen a una versión conocida
(`localstack/localstack:3.8`) y setear `LOCALSTACK_AUTH_TOKEN: ""` explícito.

### DynamoDB Local: `unable to open database file`
La imagen corre como usuario no-root y no puede escribir en un volumen nombrado de
Docker Desktop en Windows — entra en un loop de reintentos cada 3 segundos.
**Solución:** correrla con `-inMemory` en vez de `-dbPath /data` con volumen. Se
pierden los datos al reiniciar el contenedor, aceptable para desarrollo.

### `sam local start-api`: falta `.deps.json`
Si `--template` apunta al `template.local.yaml` fuente (no al compilado), SAM monta
la carpeta `src/...` cruda como `/var/task`, sin publicar. **Solución:** usar
siempre `--template .aws-sam/build/template.yaml` después de un `sam build`.

### La URL prefirmada de S3 no es alcanzable desde el host
`upload-service` genera la URL con el hostname interno de Docker (`localstack`),
que solo resuelve dentro de la red de contenedores — no desde PowerShell en el
host. Además el cliente S3 firma en `https://` aunque LocalStack sirve HTTP plano.
**Solución:** `curl.exe --resolve localstack:4566:127.0.0.1 -k -X PUT ...` (fuerza
la resolución DNS al puerto publicado en localhost, e ignora el certificado
autofirmado).

### `env.local.json` no inyecta la API key (Gemini responde 403 "unregistered callers")
`--env-vars` de SAM CLI solo **sobrescribe** variables que ya existen como clave en
el template — no agrega nombres nuevos. Si `GEMINI_API_KEY` no está declarada en
`template.local.yaml`, el valor de `env.local.json` se pierde silenciosamente y la
key llega vacía. **Solución:** declarar `GEMINI_API_KEY: ""` como placeholder en
`Globals.Function.Environment.Variables` del template.

### Modelos de Gemini no disponibles (404 / 403 con mensajes crípticos)
`GeminiEmbeddingService`/`RagAnswerService` originalmente solo hacían
`response.EnsureSuccessStatusCode()`, perdiendo el cuerpo del error real de Gemini.
Con el body capturado se vieron los errores reales:
- `text-embedding-004` no disponible para la cuenta → se usa `gemini-embedding-001`.
- `gemini-2.0-flash` dado de baja por Google → se usa `gemini-2.5-flash`.

Si esto vuelve a pasar, correr para ver qué modelos están habilitados en tu cuenta:

```powershell
$key = (Get-Content env.local.json | ConvertFrom-Json).IndexerFunction.GEMINI_API_KEY
Invoke-RestMethod -Uri "https://generativelanguage.googleapis.com/v1beta/models?key=$key" -Method Get |
  Select-Object -ExpandProperty models |
  Where-Object { $_.supportedGenerationMethods -contains "embedContent" -or $_.supportedGenerationMethods -contains "generateContent" } |
  Select-Object name, supportedGenerationMethods
```

### Embeddings guardados como Number Set en vez de List en DynamoDB
El converter por defecto del SDK de .NET para `List<float>` mapea a **Number Set
(NS)**, que no garantiza orden y descarta valores duplicados — corrompe el vector
de embedding silenciosamente. **Solución:** `FloatListConverter : IPropertyConverter`
+ `[DynamoDBProperty(typeof(FloatListConverter))]` en `ChunkRecord.Embedding` para
forzar el tipo `List (L)`. Ver `src/SemanticSearch.Core/Models/FloatListConverter.cs`.

### DynamoDB Local / LocalStack pierden todo al reiniciar Docker Desktop
`dynamodb-local` corre con `-inMemory` (sin persistencia real, ver arriba). LocalStack
tiene `PERSISTENCE: 1` + volumen nombrado, pero en la práctica un `docker compose down`
sin `-v`, un reinicio de Docker Desktop o del daemon igual puede dejarlo vacío. Síntoma:
`GET /documents` devuelve 500 con `Amazon.DynamoDBv2.Model.ResourceNotFoundException:
Cannot do operations on a non-existent table`, o el bucket `docs` aparece vacío aunque
antes tenía objetos. **Solución:** correr `docker compose up setup` (es idempotente, no
hace daño de más) cada vez que se reinicia el stack de Docker, antes de asumir que hay
un bug de código.

### El frontend sube el archivo pero nunca llega a S3 (sin error visible en `sam local`)
Encontrado al integrar el frontend (Fase 7) con `upload-service`/`report-service`.
Son **tres problemas apilados**, cada uno enmascarando al siguiente — `curl` no los
sufre (por eso el flujo de la sección 6 con `--resolve`/`-k` siempre funcionó), pero
el navegador sí:

1. **Host `localstack` no resuelve fuera de la red Docker.** El navegador corre en el
   host, no dentro de `semantic-search-net`. **Solución:** variable de entorno nueva
   `S3_PUBLIC_SERVICE_URL: http://localhost:4566`, usada *solo* para el cliente S3 que
   genera URLs prefirmadas (`upload-service` completo, y el cliente de presign
   separado en `report-service`) — el resto de las llamadas S3 reales (Indexer,
   Documents, el `PutObject`/`GetObjectMetadata` de Reports) siguen usando
   `S3_SERVICE_URL: http://localstack:4566` porque esas sí corren dentro de Docker.
2. **El SDK firma `https://` aunque el endpoint sea `http://`.** `AmazonS3Config.UseHttp`
   **no tiene efecto** sobre el esquema de una URL prefirmada en este SDK (se probó
   explícitamente) — el navegador no puede saltarse ese mismatch de protocolo como sí
   hace `curl -k`. **Solución real:** setear `Protocol = Protocol.HTTP` directo en el
   `GetPreSignedUrlRequest` (namespace `Amazon`), condicionado a si se está corriendo
   contra un endpoint local. Ver `S3UploadService.cs` y `ReportStorageService.cs`.
3. **Falta CORS en el bucket S3.** Un `PUT`/`GET` desde el navegador a una URL
   prefirmada de S3 dispara preflight `OPTIONS` igual que cualquier otro request
   cross-origin — esto es CORS *del bucket*, independiente del CORS de la API
   (sección siguiente). Sin una política CORS en el bucket, el navegador bloquea el
   `PUT` **silenciosamente antes de mandarlo**: no aparece ningún log ni en
   `sam local start-api` ni en LocalStack, lo que lo hace parecer un problema de
   backend cuando no lo es. **Solución:** `aws s3api put-bucket-cors` sobre `docs` y
   `reports` en el contenedor `setup` de `docker-compose.yml`, permitiendo el origen
   `http://localhost:5173`.

**Cómo diagnosticar esto en el futuro:** si `POST /upload` devuelve 200 pero el
archivo no aparece en `/documents` tras indexarlo, primero confirmar si el objeto
llegó a S3 (`aws --endpoint-url=http://localhost:4566 s3api list-objects-v2 --bucket
docs`) antes de sospechar del código del indexer — si no está en S3, el problema es
la subida (esta sección), no la indexación.

### El frontend no puede llamar a ninguna ruta de la API (`/documents`, `/query`, ...) contra el backend local — `403 {"message":"Missing Authentication Token"}` en el preflight `OPTIONS`

Encontrado 2026-09-05 probando el frontend real (no `curl`) contra `sam local
start-api` por primera vez de punta a punta. Síntoma: **cualquier** llamada del
frontend al backend local falla en el navegador (Network tab muestra el `OPTIONS`
en rojo, nunca llega a hacerse el `GET`/`POST` real), aunque la misma URL responda
perfecto con `curl`.

**Causa raíz — límite conocido y sin fix de SAM CLI, no un bug de este proyecto:**
`client.ts` manda `Content-Type: application/json` en *todas* las llamadas
(incluso `GET` sin body), lo que el navegador considera un header "no simple" y
dispara un preflight `OPTIONS` antes de cualquier request cross-origin. Real API
Gateway HTTP API resuelve ese preflight **a nivel de gateway**, sin invocar ningún
Lambda — pero `sam local start-api`, cuando la API es la implícita (como acá:
`Type: HttpApi` en cada evento de `template.local.yaml`, sin declarar
`AWS::Serverless::HttpApi` a mano), **no puede sintetizar esa respuesta**: necesita
que la API tenga un `DefinitionBody` de OpenAPI inline, que la API implícita no
genera. Agregar `Globals.HttpApi.CorsConfiguration` al template (ver sección 4) no
lo arregla — es inofensivo y refleja la config real de `infra/apigateway.tf`, pero
el preflight sigue devolviendo 403 igual. Confirmado con los issues abiertos de AWS:
[aws/aws-sam-cli#3803](https://github.com/aws/aws-sam-cli/issues/3803) y
[aws/aws-sam-cli#3864](https://github.com/aws/aws-sam-cli/issues/3864) (mismo
mensaje de error exacto).

**Solución real:** evitar que el navegador dispare el preflight en primer lugar,
ya que los Lambdas nunca validan el `Content-Type` (`JsonSerializer.Deserialize(request.Body
?? "")` ignora el header por completo). En `frontend/src/api/client.ts`:
- No mandar `Content-Type` en absoluto si la request no tiene body (los `GET`/`DELETE`).
- Cuando sí hay body, usar `text/plain;charset=UTF-8` en vez de `application/json`
  — es uno de los 3 valores "CORS-safelisted" que el navegador nunca preflightea.

En Modo B (frontend local contra backend real en AWS, `npm run dev:cloud`) esto no
hace falta: ahí sí hay `Authorization: Bearer <jwt>` real, que igual dispara
preflight sin importar el `Content-Type` — pero como ese preflight lo resuelve la
API Gateway real (no `sam local`), nunca fue un problema en ese modo.

---

## 8. Frontend (React SPA) contra el entorno local

El frontend (`frontend/`, Fase 7 del `TODO.md`) es un Vite dev server aparte —
requiere su propia terminal, además de `docker compose up -d` y `sam local start-api`.

```powershell
cd frontend
npm run dev
# http://localhost:5173
```

Puntos a tener en cuenta:

- **Puerto fijo (`5173`).** `vite.config.ts` tiene `server.port = 5173` +
  `strictPort: true` a propósito: si Vite saltara a otro puerto (5174, 5175...) por
  tenerlo ocupado, dejaría de matchear el origen autorizado en la política CORS de
  los buckets `docs`/`reports` (`aws s3api put-bucket-cors` en el contenedor `setup`
  de `docker-compose.yml`, hardcodeada a `http://localhost:5173`/`http://127.0.0.1:5173`)
  y el navegador bloquearía el `PUT` a la URL prefirmada de S3 al subir un archivo. Si
  `npm run dev` falla con "Port 5173 is already in use", hay un proceso zombie — en
  Windows, matar el proceso `npm run dev`/`vite` a veces no mata
  el proceso `node` hijo real; buscar quién ocupa el puerto con
  `Get-NetTCPConnection -LocalPort 5173 | Select OwningProcess` y matarlo con
  `Stop-Process -Id <pid> -Force`.
- **`.env`** (gitignored, copiar de `.env.example`): solo necesita `VITE_API_URL`
  apuntando a `http://127.0.0.1:3000` (donde escucha `sam local start-api`).
- **El indexado sigue siendo manual.** Subir un archivo desde la UI (drag & drop en
  la vista "Subir documento") deja el objeto en S3 igual que con `curl`, pero
  `IndexerFunction` no se dispara solo — hay que invocarlo a mano con
  `sam local invoke IndexerFunction` (sección 4), usando el `docId`/key que muestra
  la respuesta del upload en la UI, antes de que el documento aparezca en la vista
  "Documentos" o sea buscable en "Buscar".
- Ver la sección 7 ("El frontend sube el archivo pero nunca llega a S3") si el upload
  parece exitoso en la UI pero el archivo no aparece en S3.

---

Ver también [`docs/architecture.md`](architecture.md) para el diagrama del entorno
local y la comparación con producción, y [`TODO.md`](../TODO.md#fase-12--entorno-local-con-docker-compose--sam-cli-sin-aws)
para el checklist de la Fase 12.
