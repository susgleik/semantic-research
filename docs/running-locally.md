# Cómo correr el proyecto — guía rápida (2 modos)

Punto de entrada único para levantar el proyecto en tu máquina. El detalle
verificado de cada pieza (instalación, comandos exactos, troubleshooting) ya está
documentado, pero repartido en varios archivos — esta guía junta todo en el orden en
que realmente se usa, para los dos modos que importan en el día a día:

| Modo | Frontend | Backend | Cuándo usarlo |
|---|---|---|---|
| **A — Todo local** | `npm run dev` (Vite, `localhost:5173`) | Docker Compose + SAM CLI local (`127.0.0.1:3000`) | Desarrollo normal: probar un cambio de código sin tocar la cuenta de AWS ni gastar créditos de Gemini de más en llamadas repetidas de prueba (igual pega a Gemini real, no hay forma de evitarlo) |
| **B — Frontend local, backend real** | `npm run dev:cloud` (Vite, `localhost:5173`) | AWS real (API Gateway + Lambda + Cognito ya desplegados) | Probar el frontend contra datos/infra reales, con login de Cognito de verdad, sin tener que buildear y desplegar el frontend en cada cambio |

No existe combinación "frontend real + backend local" — el frontend en CloudFront
sirve un build estático apuntando siempre a la URL de API Gateway real (Fase 7).

> **Nota sobre el modo A y el cambio de multi-tenancy (Fase 15):** el backend local
> todavía no valida JWT (no hay mock de Cognito, ver la sección de gotchas más abajo),
> así que `CallerIdentity.GetOwnerId` siempre devuelve `""` — todo lo que subís en
> modo A queda marcado como legacy/compartido (`OwnerId` vacío), visible para
> cualquiera. Para probar el aislamiento real entre usuarios hace falta el modo B
> (o Cognito real) con dos usuarios distintos.

---

## Prerrequisitos (una sola vez)

```powershell
winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
winget install Amazon.SAM-CLI --accept-package-agreements --accept-source-agreements
winget install Amazon.AWSCLI --accept-package-agreements --accept-source-agreements
```

Abrí una terminal **nueva** después de instalar (el PATH no se actualiza en
sesiones ya abiertas) y confirmá:

```powershell
dotnet --list-sdks   # debe listar una 8.x además de la que ya tenías
sam --version
aws --version
```

También hace falta Docker Desktop corriendo, Node.js para el frontend, y una API
key de Google Gemini (tier de pago — ver [`CLAUDE.md`](../CLAUDE.md) para el porqué).

### Setup único por máquina (no se repite en cada sesión de trabajo)

Estos archivos no vienen en el `git clone` a propósito — están en `.gitignore`
porque contienen (o van a contener) tu API key real. Se crean **una sola vez** por
máquina, igual que correr `npm install` una vez:

```powershell
# Config del backend local: copia el archivo de ejemplo a uno nuevo en la raíz del
# repo. `copy` (alias de Copy-Item en PowerShell) solo duplica el archivo en tu
# disco local — no sube nada a ningún lado.
copy env.local.example.json env.local.json
notepad env.local.json   # pegar tu API key real de Gemini y guardar
```

```powershell
cd frontend
copy .env.example .env.development   # (si no existe ya — ver nota abajo)
npm install
```

> **Dónde queda guardada la API key:** en texto plano dentro de `env.local.json`,
> en tu disco, en la raíz del repo. Nunca se commitea (gitignored). La única vez
> que "viaja" a algún lado es cuando `sam local start-api --env-vars env.local.json`
> la lee al arrancar y la inyecta como variable de entorno dentro de los
> contenedores Docker que emulan los Lambdas — no sale de tu máquina.
>
> **Sobre `.env.development`:** en este repo ya existe (fue creado en una sesión de
> trabajo anterior) — no hace falta volver a copiarlo. Se documenta el paso acá
> solo para el caso de un clone nuevo en otra máquina.

### El "modo configurable" que buscás ya existe — no hay que copiar nada para cambiar de modo

Una vez hecho el setup de arriba, **cambiar entre modo A y modo B no implica tocar
ningún `.env` a mano** — eso ya lo resuelve Vite por convención según qué script
corras:

| Comando | Modo de Vite | Archivo que carga automáticamente |
|---|---|---|
| `npm run dev` | `development` (default) | `.env.development` → backend local |
| `npm run dev:cloud` | `production` (`vite --mode production`) | `.env.production` → backend real en AWS |

O sea: los dos `.env.*` **conviven en disco al mismo tiempo**, uno con la URL de
`127.0.0.1:3000` y otro con la URL real de API Gateway, y elegís cuál se usa con
el script de `npm` que corras — nunca copiando ni renombrando archivos.

---

## Modo A — Todo local (frontend + backend en tu máquina)

Con el setup de arriba ya hecho, esto es lo que corrés cada vez que querés trabajar.
Necesitás **3 terminales abiertas en simultáneo**. El orden importa.

### Terminal 1 — stack de datos (Docker)

```powershell
docker compose up -d
docker compose ps   # "setup" debe quedar en Exited(0) — es normal, corre una vez y termina
```

Levanta LocalStack (emula S3, puerto `4566`) y DynamoDB Local (puerto `8000`).
`setup` crea el bucket `docs`/`reports` y la tabla `chunks` automáticamente.

### Terminal 1 (misma) — compilar y levantar el backend

```powershell
sam build --template template.local.yaml
sam local start-api --template .aws-sam/build/template.yaml --docker-network semantic-search-net --env-vars env.local.json
```

`sam build` no sube ni descarga nada — corre `dotnet publish` sobre cada uno de los
5 proyectos de Lambda y deja el resultado en una carpeta nueva `.aws-sam/` en la
raíz del repo (gitignored, se regenera en cada build):
- `.aws-sam/build/template.yaml` — el template con las rutas de código ya resueltas
  (por eso `sam local start-api` apunta acá y no a `template.local.yaml`: ese es el
  *fuente*, este es el *compilado*)
- `.aws-sam/build/<NombreDeLaFunción>/` — una carpeta por Lambda con los binarios
  publicados, es lo que realmente corre dentro del contenedor Docker de cada Lambda

`sam local start-api` queda escuchando en `http://127.0.0.1:3000`, en primer
plano — **dejar esta terminal abierta**. Cada vez que cambiás código de un Lambda
hay que repetir `sam build` y relanzar `sam local start-api` (no hay hot-reload).

### Terminal 2 — frontend

```powershell
cd frontend
npm run dev
```

Abre en `http://localhost:5173` (puerto fijo, no cambiarlo — ver gotchas). Como el
backend local no valida JWT, la app arranca **sin pantalla de login**.

### Terminal 3 — para invocar cosas a mano

El indexado **no es automático en local** (LocalStack no emite el evento
`s3:ObjectCreated`): después de subir un archivo desde la UI hay que indexar a mano.

```powershell
$env:AWS_ACCESS_KEY_ID = "local"; $env:AWS_SECRET_ACCESS_KEY = "local"; $env:AWS_DEFAULT_REGION = "us-east-1"

# 1. Confirmar que el archivo llegó a S3 después de subirlo desde la UI
aws --endpoint-url=http://localhost:4566 s3 ls s3://docs/ --recursive

# 2. Editar events/s3-put-event.json: poner el docId y el tamaño real del archivo subido
notepad events/s3-put-event.json

# 3. Indexar
sam local invoke IndexerFunction --template .aws-sam/build/template.yaml --event events/s3-put-event.json --docker-network semantic-search-net --env-vars env.local.json
```

Recién después de este paso el documento aparece en la vista "Documentos" y es
buscable en "Buscar". Repetir el paso 2-3 por cada archivo que subas.

### Resetear el entorno

```powershell
docker compose down -v   # borra todo (S3 y DynamoDB local no tienen persistencia real igual)
docker compose up -d
docker compose up setup  # por si el bucket/tabla no se recrearon solos
```

---

## Modo B — Frontend local, backend real en AWS

Una sola terminal.

```powershell
cd frontend
npm run dev:cloud
```

Esto corre Vite en modo `production`, que carga `.env.production` (URL real de API
Gateway + datos de Cognito, ya generados por Terraform — ver
[`docs/terraform-setup.md`](terraform-setup.md)). Sigue sirviendo en
`http://localhost:5173`, pero:

- Pide login real contra el Hosted UI de Cognito.
- Cada acción (subir, preguntar, listar) pega directo a la API real en AWS — es el
  mismo backend que usa el sitio en CloudFront, **cuenta contra costos reales** de
  API Gateway/Lambda/DynamoDB/Gemini (ver [`CLAUDE.md`](../CLAUDE.md#por-qué-esta-arquitectura-decisiones-no-obvias)).
- No hace falta Docker, SAM CLI, ni `env.local.json` para este modo — el backend ya
  está desplegado.

Útil para: probar un cambio de frontend contra datos reales sin tener que hacer
`npm run build` + subir a S3 + invalidar CloudFront (Fase 7) en cada iteración.

---

## Gotchas rápidos (el detalle completo está en `docs/local-development.md`)

Estos son los que más tiempo hacen perder si no se conocen de antemano:

| Síntoma | Causa | Solución |
|---|---|---|
| `sam local start-api` tira "missing .deps.json" | `--template` apunta al `template.local.yaml` fuente, no al compilado | Usar siempre `.aws-sam/build/template.yaml` (después de `sam build`) |
| El upload desde la UI no llega a S3, sin error visible | Host `localstack` no resuelve fuera de Docker / protocolo HTTPS firmado sobre un endpoint HTTP / falta CORS en el bucket | Ya resuelto en el código (`S3_PUBLIC_SERVICE_URL`, `Protocol.HTTP`, CORS en `setup`) — si vuelve a pasar, confirmar primero si el objeto llegó a S3 (`aws s3 ls`) antes de sospechar del indexer |
| Gemini responde 403 "unregistered callers" | `env.local.json` no inyectó la key porque `GEMINI_API_KEY` no estaba declarada como placeholder en el template | Ya resuelto (placeholder `""` en `template.local.yaml`) — si aparece de nuevo, revisar que `env.local.json` tenga la key bajo el logical ID correcto de cada función |
| `GET /documents` tira 500 después de reiniciar Docker | LocalStack/DynamoDB Local perdieron los datos (sin persistencia real) | `docker compose up setup` (idempotente) |
| El frontend no puede llamar a ninguna ruta en Modo A — `403 "Missing Authentication Token"` en el `OPTIONS` (Network tab del navegador) | Límite conocido de `sam local start-api`: no puede responder el preflight CORS cuando la API es la implícita (nuestro caso). `curl` no lo sufre porque no hace preflight | Ya resuelto en el código (`client.ts` evita mandar headers que disparen preflight) — si reaparece, no tocar el template, revisar que ninguna request nueva agregue un header "no simple" sin necesidad |
| Vite falla con "Port 5173 is already in use" | Proceso zombie de una sesión anterior | `Get-NetTCPConnection -LocalPort 5173 \| Select OwningProcess` → `Stop-Process -Id <pid> -Force` |
| No hay pantalla de login en modo A | Es intencional — el backend local no valida JWT todavía (mock de Cognito pendiente, Fase 12) | No es un bug; usar modo B para probar el flujo de auth real |

Para cualquier otro problema, **[`docs/local-development.md`](local-development.md)**
tiene la referencia completa: instalación paso a paso, todos los comandos de AWS
CLI/SAM CLI usados en este proyecto, y cada bug real encontrado con su fix explicado
en detalle.
