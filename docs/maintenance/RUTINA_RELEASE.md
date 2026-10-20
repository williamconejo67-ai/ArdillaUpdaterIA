# Rutina de release - ArdillaUpdater (copia de trabajo)

*Documento interno para mantenimiento. Describe EXACTAMENTE cómo se publicó la
versión 1.1.5 (2026-10-17), paso a paso, para poder repetirlo sin rebuscar
en conversaciones viejas.*

> Regla de oro: **este repo es un "release hub"**. Solo se commitean los archivos
> revisados a propósito (módulo AI, pruebas de AI, csproj, CHANGELOG, docs).
> NUNCA usar `git add .` ni `git add -A` (hay decenas de archivos fuente sin trackear a propósito).

---

## Datos fijos

| Dato | Valor |
|---|---|
| Repo IA | `https://github.com/williamconejo67-ai/ArdillaUpdaterIA.git` (carpeta actual) |
| Repo clon (sin IA) | `E:\Respaldo 7.10.2026\Ardilla ia\ArdillaUpdaterClon` |
| Asset esperado por la app IA | `ArdillaUpdaterIA.exe` (constante `AppConfig.UpdaterAssetName`) |
| Asset esperado por el clon | `ArdillaUpdater.exe` |
| Token GitHub | en `..\ardilla-updater-uploader\.env` (carpeta hermana; el uploader quedó fuera del proyecto en 2026-10) (`GITHUB_TOKEN=...`) — **nunca imprimirlo ni subirlo a git** |
| SDK | .NET `10.0.402` (fijado en `global.json`) |
| `gh` CLI | **NO instalado** → usar la API de GitHub con `Invoke-RestMethod` |
| PowerShell | sin `&&`, sin `head/tail/grep/sed` → usar `;`, `Select-Object`, `Select-String` |
| Git en PowerShell | git escribe por stderr → PowerShell lo muestra como "error" rojo; **confiar en los resultados, no en el color** |
| Pruebas | `dotnet test ArdillaUpdater.Tests\ArdillaUpdater.Tests.csproj` |
| Backups de cambios | `E:\Respaldo 7.10.2026\Ardilla ia\_backup_ia_mejoras_<fecha>\` (copiar ANTES de editar) |

---

## Pasos de la rutina (en orden)

### 1. Backup de los archivos que se van a tocar
```powershell
New-Item -ItemType Directory -Force "E:\Respaldo 7.10.2026\Ardilla ia\_backup_ia_mejoras_<fecha>" | Out-Null
Copy-Item "<archivo>" "<backup>\<archivo>" -Force
```

### 2. Bump de versión en `ArdillaUpdater.csproj`
Cambiar los TRES valores: `Version`, `AssemblyVersion`, `FileVersion`
(ej. `1.1.5` / `1.1.5.0` / `1.1.5.0`). Se dejan como defecto porque al publicar
se sobreescriben con `/p:Version=...`.

### 3. Entrada nueva arriba en `CHANGELOG.md`
Formato `## [x.y.z] - AAAA-MM-DD` con secciones `Added / Changed / Fixed / Notes`.
En `Notes` poner el resultado real: `compilación 0 advertencias, 0 errores;
suite N/N pruebas superadas`.

### 4. Verificar build + pruebas
```powershell
dotnet build ArdillaUpdater.csproj -c Release
dotnet test ArdillaUpdater.Tests\ArdillaUpdater.Tests.csproj
```
Si no está en verde, NO continuar.

### 5. Stage SELECTIVO (nunca `git add .`)
```powershell
git add AI/<archivos revisados> ArdillaUpdater.Tests/<archivos revisados> ArdillaUpdater.csproj CHANGELOG.md
git status --short   # revisar que SOLO están staged los esperados
```
Exclusiones conocidas:
- `docs/IA_MODULO_MAPA.md` → se queda SIN commitear (decisión del usuario).
- Fuentes sin trackear (`Form1.cs`, `AppConfig.cs`, etc.) → jamás subirlos.
- Capturas `.PNG`, `.gitignore`, íconos → no subir.

### 6. Commit con mensaje convencional
```
feat(ai): <resumen> (x.y.z)
```
o `chore(release): bump version to x.y.z` si solo cambia la versión.
Cuerpo con viñetas cortas en español describiendo lo importante.

### 7. Tag + push
```powershell
git tag -a vx.y.z -m "Release x.y.z - <título>"
git push origin main
git push origin vx.y.z
git ls-remote --tags origin   # confirmar que el tag llegó
```

### 8. Publicar el ejecutable
```powershell
dotnet publish .\ArdillaUpdater.csproj -c Release -r win-x64 --self-contained true `
  -o .\publish\updater-publish-x.y.z /p:Version=x.y.z
```
Verificar la versión del resultado:
```powershell
(Get-Item '.\publish\updater-publish-x.y.z\ArdillaUpdaterIA.exe').VersionInfo |
  Select-Object FileVersion, ProductVersion
```

### 9. Crear release en GitHub + subir asset
Usar el script **`docs/maintenance/publish-release.ps1`** (ver abajo).
No hace falta `gh` ni el uploader gráfico.

### 10. Verificación end-to-end (obligatoria)
- Descargar el asset desde la URL pública y comparar SHA256 contra el local.
- Abrir la app anterior y comprobar que el auto-update detecta `vx.y.z`.

---

## Script de publicación

`docs/maintenance/publish-release.ps1` crea el release y sube el `.exe`
usando la API de GitHub con el token del `.env`.

```powershell
# Release completo para el proyecto IA (asset ArdillaUpdaterIA.exe)
powershell -NoProfile -ExecutionPolicy Bypass -File "docs\maintenance\publish-release.ps1" `
  -Version 1.1.5 -Repo "williamconejo67-ai/ArdillaUpdaterIA" `
  -ExePath "E:\Respaldo 7.10.2026\Ardilla ia\ArdillaUpdater\publish\updater-publish-1.1.5\ArdillaUpdaterIA.exe"

# Proyecto clon (asset ArdillaUpdater.exe)
powershell -NoProfile -ExecutionPolicy Bypass -File "docs\maintenance\publish-release.ps1" `
  -Version 1.1.5 -Repo "williamconejo67-ai/ArdillaUpdater" `
  -ExePath "<ruta>\publish\...\ArdillaUpdater.exe"
```

El script:
1. Lee `GITHUB_TOKEN` del `.env` del uploader (no lo imprime).
2. Crea el release `vx.y.z` con `name = x.y.z` (si ya existe, avisa y sale).
3. Sube el `.exe` con el nombre de archivo que tenga el archivo local
   (**el asset debe llamarse como espera la app**: `ArdillaUpdaterIA.exe` / `ArdillaUpdater.exe`).
4. Imprime `RELEASE_URL`, `ASSET_URL` y el SHA256 local para la verificación.

Verificación manual de integridad (SHA256 local vs remoto):
```powershell
(Get-FileHash "<exe>" -Algorithm SHA256).Hash.ToLower()
# descargar la URL pública del asset y comparar con:
# Invoke-WebRequest -Uri "<asset_url>" -OutFile "$env:TEMP\v.exe"
# (Get-FileHash "$env:TEMP\v.exe" -Algorithm SHA256).Hash.ToLower()
```

---

## Cómo detecta la app las actualizaciones

- Consulta los releases de GitHub vía API (repositorio según `AppConfig`).
- Busca el release más reciente **no prerelease** con tag `vx.y.z`.
- Exige un asset con el nombre exacto de `AppConfig.UpdaterAssetName`.
- Descarga a `<carpeta de versiones>\<assetBase>_NEW.exe.part`, valida,
  y reemplaza dejando backup `*_OLD_*.exe`.
- Mecanismo de mutex: `Global\ARDILLA-UPDATER-UNIQUE-IDENTIFIER`
  → cerrar la app antes de reemplazar el exe (se ejecuta en secuencia).

---

## Historial de releases publicados con esta rutina

| Versión | Fecha | Repo | Release ID | Notas |
|---|---|---|---|---|
| 1.1.6 | 2026-10-18 | ArdillaUpdaterIA | 416850130 | Auditoría 2026-10 (PR #1–#3); 130/130 pruebas |
| 1.1.5 | 2026-10-18 | ArdillaUpdater | 416850351 | Auditoría 2026-10 (PR #1–#3, sin módulo IA); 77/77 pruebas |
| 1.1.5 | 2026-10-17 | ArdillaUpdaterIA | 416059074 | Mejoras IA (#1–#10) + fix DPI; 96/96 pruebas |
| 1.1.4 | 2026-10-17 | ArdillaUpdaterIA | 415722875 | Validación de extracciones |
| 1.1.4 | 2026-10-17 | ArdillaUpdater | 415722887 | Igual que IA, sin módulo IA |
