# Cómo se hacen los PRs en este proyecto

Flujo real usado desde Fase 13 (CI/CD) en adelante — `develop` es la rama de trabajo,
`main` está protegida y **exige pasar por Pull Request** (push directo rechazado por
GitHub: `GH006: Protected branch update failed`).

## Herramienta

Se usa `gh` (GitHub CLI), instalado vía `winget install --id GitHub.cli`. Login una
sola vez (interactivo, requiere navegador o token — no se puede automatizar):

```powershell
& "C:\Program Files\GitHub CLI\gh.exe" auth login
```

Verificar sesión: `gh auth status`.

## Flujo estándar

Trabajás siempre sobre `develop`. Cuando el trabajo está listo para `main`:

```powershell
git push origin develop

gh pr create --base main --head develop `
  --title "titulo corto del cambio" `
  --body "que cambia y por que"

gh pr checks --watch   # espera a que ci.yml (dotnet build + test) pase en verde

gh pr merge --merge
```

- `gh pr checks --watch` bloquea la terminal hasta que el check termine — no hace
  falta sondear a mano.
- Si `gh pr merge` sale con **"not mergeable"** por política de revisores y sos el
  único que trabaja en el repo: revisá `required_approving_review_count` en la
  protección de `main` (GitHub no te deja aprobar tu propio PR). Se bajó a `0` en este
  repo — el check de `ci.yml` en verde es el gate real, no un segundo humano.

## Qué dispara cada cosa

| Evento | Workflow | Qué corre |
|---|---|---|
| Push o PR a cualquier rama | `.github/workflows/ci.yml` | `dotnet build` + `dotnet test` de `SemanticSearch.sln`, sin credenciales AWS |
| Push a `main` (o sea, el merge del PR) | `.github/workflows/deploy.yml` | Job `build-and-plan` automático (build, test, empaqueta Lambdas, `terraform plan`) → job `apply` **pausado** esperando aprobación manual en el Environment `production` |

## Aprobar un deploy

Después de mergear a `main`, entrar a la pestaña **Actions** del repo, abrir el run de
`Deploy`, y click en **"Review deployments"** cuando el job `apply` quede en estado
`waiting`. Ver `docs/terraform-setup.md#6` para el setup one-time de la variable
`AWS_DEPLOY_ROLE_ARN` y el Environment `production` con reviewer.

## Si un PR toca solo docs/config (no `infra/` ni código)

El job `build-and-plan` igual corre `terraform plan` completo — es normal que salga
"changes" en los 5 Lambdas aunque no hayas tocado su código: `dotnet publish` no
genera bytes idénticos entre builds (timestamps, GUIDs del compilador), así que
`source_code_hash` cambia siempre. No es un bug, no hace falta investigarlo.
