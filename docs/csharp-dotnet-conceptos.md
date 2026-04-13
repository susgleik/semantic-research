# C# / .NET — Conceptos clave
> Referencia personal para alguien que viene del mundo Python.

---

## 1. El ecosistema: qué es qué

```
.NET                → el runtime/plataforma base       (= CPython)
ASP.NET Core        → framework web encima de .NET     (= FastAPI / Flask)
C#                  → el lenguaje                      (= Python)
NuGet               → el gestor de paquetes            (= pip / uv)
```

**ASP.NET Core** no es un producto separado, es parte de .NET. Cuando un proyecto usa
`Sdk="Microsoft.NET.Sdk.Web"` en el `.csproj`, automáticamente tiene ASP.NET Core disponible.

---

## 2. Cómo identificar el tipo de proyecto — el `.csproj`

El `.csproj` es la declaración del proyecto. La primera línea dice todo:

| `Sdk` en el `.csproj` | Tipo de proyecto | Analogía Python |
|---|---|---|
| `Microsoft.NET.Sdk.Web` | ASP.NET Core (API / web) | FastAPI / Flask |
| `Microsoft.NET.Sdk` | Librería o consola pura | módulo Python |
| `Microsoft.NET.Sdk` + `AzureFunctionsVersion` | Azure Functions | AWS Lambda |

```xml
<!-- Esto es ASP.NET Core -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

El `TargetFramework` indica la versión de .NET: `net8.0`, `net10.0`, etc.

---

## 3. El `.csproj` es el `pyproject.toml`

```xml
<!-- C# — SemanticSearch.Api.csproj -->
<PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
<ProjectReference Include="../SemanticSearch.Core/SemanticSearch.Core.csproj" />
```

```toml
# Python — pyproject.toml
[tool.poetry.dependencies]
openai = "^2.0"
semantic-search-core = {path = "../SemanticSearch.Core"}
```

- `PackageReference` → dependencia externa de NuGet (= pip package)
- `ProjectReference` → dependencia a otro proyecto del mismo repo (= editable install)
- `dotnet restore` → instala todas las dependencias (= `pip install -r requirements.txt`)

---

## 4. Nombres de carpetas y namespaces

En .NET el **nombre de la carpeta = nombre del proyecto = namespace**. El punto (`.`)
indica jerarquía, igual que los paquetes en Python:

```
SemanticSearch.Api/          → namespace raíz: SemanticSearch.Api
SemanticSearch.Core/         → namespace raíz: SemanticSearch.Core
SemanticSearch.Functions/    → namespace raíz: SemanticSearch.Functions
```

```python
# Python
from semantic_search.core.models import DocumentChunk
```

```csharp
// C# equivalente
using SemanticSearch.Core.Models; // y luego usás DocumentChunk directamente
```

---

## 5. Estructura interna de un proyecto ASP.NET Core

```
SemanticSearch.Api/
│
├── Endpoints/       ← rutas HTTP            (= routers de FastAPI)
├── Services/        ← lógica de negocio     (= services / use_cases)
├── Models/          ← schemas de datos      (= Pydantic models)
├── Middleware/      ← interceptores HTTP    (= middleware de Starlette)
│
├── Program.cs       ← entrypoint + DI       (= main.py / app.py)
├── appsettings.json ← configuración         (= config.py / .env)
└── *.csproj         ← declaración proyecto  (= pyproject.toml)
```

---

## 6. `Program.cs` — el `main.py`

**FastAPI (Python):**
```python
app = FastAPI()
app.include_router(query_router)
app.include_router(upload_router)
uvicorn.run(app)
```

**ASP.NET Core (C#):**
```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Registrar servicios (Dependency Injection)
builder.Services.AddScoped<IRagService, RagService>();

// 2. Construir la app
var app = builder.Build();

// 3. Registrar endpoints
app.MapQueryEndpoints();
app.MapUploadEndpoints();

// 4. Arrancar
app.Run();
```

La diferencia clave: en .NET existe el `builder` que configura la **inyección de dependencias**
*antes* de crear la app. En Python esto se hace de forma más manual.

---

## 7. Inyección de Dependencias (DI)

En Python normalmente instanciás las clases a mano o usás `Depends()` de FastAPI.
En .NET el sistema de DI es nativo y central.

```python
# FastAPI — DI con Depends
def get_rag_service():
    return RagService(EmbeddingService(), SearchService())

@app.post("/query")
async def query(service = Depends(get_rag_service)):
    ...
```

```csharp
// ASP.NET Core — DI nativo
// En Program.cs registrás una vez:
builder.Services.AddScoped<IRagService, RagService>();

// En el endpoint, .NET lo inyecta automáticamente:
app.MapPost("/query", async (IRagService ragService) => {
    ...
});
```

**Lifetimes de DI:**
| .NET | Significado | Analogía |
|---|---|---|
| `AddSingleton` | Una sola instancia para toda la app | módulo cargado una vez |
| `AddScoped` | Una instancia por request HTTP | instancia por request |
| `AddTransient` | Nueva instancia cada vez que se pide | `new X()` siempre |

---

## 8. `record` — los Pydantic models de C#

```python
# Python / Pydantic
class QueryRequest(BaseModel):
    query: str
    top_k: int = 5
    filter: str | None = None
    language: str = "es"
```

```csharp
// C# — record (inmutable, con validación)
public record QueryRequest(
    [Required] string Query,
    int TopK = 5,
    string? Filter = null,
    string Language = "es"
);
```

- `record` es inmutable por defecto (como un `frozen=True` en Pydantic)
- `[Required]` es un atributo de validación (= `Field(..., min_length=1)`)
- `string?` significa nullable — el `?` indica que puede ser `null` (= `Optional[str]`)

---

## 9. Interfaces — contratos explícitos

Python usa duck typing. C# usa interfaces explícitas:

```python
# Python — duck typing, no hay contrato formal
class RagService:
    async def query(self, request): ...
```

```csharp
// C# — interfaz define el contrato
public interface IRagService
{
    Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct);
}

// La clase implementa el contrato explícitamente
public class RagService : IRagService
{
    public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct) { ... }
}
```

La razón: con interfaces podés registrar `IRagService` en DI y swapear implementaciones
(real vs mock en tests) sin cambiar el código que lo usa.

---

## 10. `async/await` y `Task` — equivalente a `async/await` de Python

```python
# Python
async def embed(text: str) -> list[float]:
    result = await client.embeddings.create(input=text)
    return result.data[0].embedding
```

```csharp
// C# — Task<T> es el equivalente de Coroutine/Awaitable
public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct)
{
    var result = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
    return result.Value.ToFloats();
}
```

- `Task<T>` = `Awaitable[T]` en Python
- `CancellationToken` = mecanismo para cancelar operaciones async (no existe en Python stdlib, similar a `asyncio.Event`)
- `async Task` sin tipo de retorno = `async def` que no retorna nada (equivale a `Task` = `None`)

---

## 11. `dotnet` CLI — comandos frecuentes

| Comando | Equivalente Python | Qué hace |
|---|---|---|
| `dotnet restore` | `pip install -r requirements.txt` | Instala paquetes NuGet |
| `dotnet build` | — | Compila el proyecto |
| `dotnet run` | `python main.py` | Compila y ejecuta |
| `dotnet test` | `pytest` | Corre los tests |
| `dotnet publish` | — | Genera build de producción |

Se puede correr desde:
- La **root** del repo (donde está el `.sln`) → aplica a **todos** los proyectos
- Una **carpeta de proyecto** (donde está el `.csproj`) → aplica **solo** a ese proyecto

---

## 12. Solution (`.sln`) — el workspace

El `.sln` agrupa todos los proyectos. Es solo un índice, no contiene código.

```
SemanticSearch.sln          ← agrupa todo (= workspace de VS Code / monorepo)
├── src/
│   ├── SemanticSearch.Api/         ← proyecto web
│   ├── SemanticSearch.Core/        ← librería compartida
│   ├── SemanticSearch.Functions/   ← Azure Functions
│   └── SemanticSearch.McpServer/   ← servidor MCP
└── tests/
    ├── SemanticSearch.Api.Tests/
    └── SemanticSearch.Functions.Tests/
```

`dotnet restore` en la root lee el `.sln` y restaura **todos** los proyectos de una vez.

---

---

## 13. Primary Constructors — constructor en la firma de la clase

Feature de **C# 12** (.NET 8+). Permite declarar los parámetros del constructor directamente en la línea de la clase, eliminando el boilerplate de declarar campos privados y asignarlos.

### Antes (C# clásico)

```csharp
public class BlobService : IBlobService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IOptions<BlobOptions> _opts;
    private readonly ILogger<BlobService> _logger;

    public BlobService(
        BlobServiceClient blobServiceClient,
        IOptions<BlobOptions> opts,
        ILogger<BlobService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _opts = opts;
        _logger = logger;
    }
}
```

### Con Primary Constructor (C# 12+)

```csharp
public class BlobService(
    BlobServiceClient blobServiceClient,
    IOptions<BlobOptions> opts,
    ILogger<BlobService> logger) : IBlobService
{
    // blobServiceClient, opts y logger disponibles en todo el cuerpo
    // sin declarar campos ni asignar — el compilador lo genera
}
```

El compilador genera los campos y la asignación internamente. Es **azúcar sintáctico** puro.

### Cómo interactúa con DI

El sistema de DI no distingue entre constructor clásico y primary constructor.
Al registrar `builder.Services.AddScoped<IBlobService, BlobService>()`, .NET inspecciona
los parámetros y los inyecta automáticamente porque ya están registrados en el contenedor.

### Origen: los `record` siempre lo tuvieron

```csharp
// record — primary constructor de siempre
public record QueryRequest(string Query, int TopK = 5);
```

C# 12 extendió esa misma idea a las `class` normales. Es la misma sintaxis.

---

_Stack de este proyecto: ASP.NET Core 10 · Azure Functions v4 · Azure AI Search · Azure OpenAI · .NET 10_
