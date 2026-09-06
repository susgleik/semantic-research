# Comandos frecuentes — git + gh

Cheat sheet de referencia rápida. Para la explicación completa del flujo de PR y qué
dispara cada workflow, ver [`contributing.md`](contributing.md).

## Día a día en `develop`

```bash
git status
git add <archivo>              # evitar `git add -A`/`.` a ciegas — revisar qué se agrega
git commit -m "tipo: mensaje corto"
git push origin develop
```

Antes de un `checkout`/`restore`/`reset`/`clean` que pueda descartar trabajo:

```bash
git status
git stash -u                   # -u incluye archivos untracked
```

## Ramas

```bash
git branch                     # listar locales
git checkout -b fix/nombre-corto
git checkout develop
git branch -d fix/nombre-corto # borrar rama local ya mergeada
```

## Traer cambios de `main` a tu rama

```bash
git checkout develop
git pull origin main           # o: git merge main
```

`git merge` es correcto para esto (integrar ramas entre sí localmente). **No** es
correcto para llevar cambios a `main` — ver más abajo por qué.

## Flujo real de PR (`develop` → `main`)

`main` está protegida: push directo rechazado (`GH006: Protected branch update
failed`). El merge final a `main` siempre pasa por PR, nunca por `git merge` local +
`git push` — eso saltearía `ci.yml` y el gate de aprobación de `deploy.yml`.

```bash
git push origin develop

gh pr create --base main --head develop \
  --title "titulo corto del cambio" \
  --body "que cambia y por que"

gh pr checks --watch            # espera a que ci.yml pase en verde, bloquea la terminal

gh pr merge --merge
```

Después del merge: pestaña **Actions** → run de `Deploy` → **"Review deployments"**
para aprobar el job `apply` (Environment `production`, queda en `waiting`).

## `gh` — otros comandos útiles

```bash
gh auth status                  # verificar sesión
gh pr status                    # PRs propios: abiertos, review requerido, etc.
gh pr view                      # detalle del PR de la rama actual
gh pr diff                      # diff del PR de la rama actual
gh run list --workflow=ci.yml   # últimos runs de un workflow
gh run watch                    # seguir el run en curso
```

## Referencia: `git commit` vs PR (`gh`)

- `git commit` guarda un snapshot **local** (o en tu rama remota tras `push`) — no
  depende de GitHub, es control de versiones puro.
- Un **PR** no es un comando de `git`, es una feature de GitHub: propone mergear una
  rama a `main`, dispara `ci.yml`, y solo después de aprobado el merge dispara
  `deploy.yml`. Por eso son dos pasos separados y con herramientas distintas
  (`git` para el historial de cambios, `gh` para el proceso de review/deploy).

## Troubleshooting rápido

- **`gh pr merge` dice "not mergeable"** y sos el único trabajando en el repo: revisar
  `required_approving_review_count` en la protección de `main` (GitHub no te deja
  aprobar tu propio PR). En este repo está en `0` — el check de `ci.yml` en verde es
  el gate real.
- **`terraform plan` muestra cambios en los 5 Lambdas aunque no tocaste su código**:
  normal, `dotnet publish` no genera bytes idénticos entre builds → `source_code_hash`
  cambia siempre. No es un bug.
