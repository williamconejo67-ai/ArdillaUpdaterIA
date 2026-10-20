# Auditoría externa 2026-10 — validación y aplicación de los 3 PRs

*Documento interno de mantenimiento. Registra qué dijo la auditoría (DeepSeek, en
`3PRs.txt`), qué se verificó contra el código real, qué se aplicó exactamente y con
qué adaptaciones. Sirve para no rebuscar en conversaciones viejas.*

- **Fecha**: 2026-10-18
- **Origen**: `3PRs.txt` (raíz del repo; **nunca se commitea**)
- **Proyectos afectados**: `ArdillaUpdater` (IA) y `ArdillaUpdaterClon` (clon, sin módulo IA)
- **Publicado como**: IA **v1.1.6** (release 416850130) y clon **v1.1.5** (release 416850351)

> Regla seguida: los 3 PRs se aplicaron **solo después de validar cada afirmación
> contra el código real**. Las adaptaciones se documentan aquí y están anotadas
> también en los propios archivos (`// PR #3 auditoría 2026-10` etc.).

---

## Resumen de los 3 PRs

| PR | Problema real | Estado |
|----|---------------|--------|
| **#1** `fix(config)` | `ai_config.json` / `ai_conversation.json` se escribían junto al `.exe` (carpeta a menudo protegida) con `catch` vacío → cambios perdidos en silencio | ✅ Aplicado (IA: completo; clon: solo `AppPaths`) |
| **#2** `fix(lang)` | `LanguageManager.LanguageChanged` se suscribía con lambdas imposibles de anular → ventana cerrada notificada → `ObjectDisposedException` al cambiar de idioma | ✅ Aplicado en ambos proyectos |
| **#3** `fix(catalog)` | Catálogo DLC: `.7z` ignorados, `EP1000`→`EP10`, `__Installer.zip`→`__In`, tamaño de pack mal informado, paginación sin tope | ✅ Aplicado en ambos proyectos |

---

## PR #1 — configuración en `%LOCALAPPDATA%`

### Qué se validó (todo confirmado)
- `AiConfig.DefaultConfigPath` y `AiConversationStore.DefaultPath` apuntaban a
  `AppDomain.CurrentDomain.BaseDirectory` (junto al `.exe`).
- `AiConfig.Save` y `AiConversationStore.Save` tenían `catch { }` vacío → fallo de
  escritura invisible para el usuario (bug crítico si la app vive en `C:\Program Files\`).
- `SeenPacksStore` ya usaba `%LOCALAPPDATA%` → inconsistencia real en el proyecto.
- Los tests existentes usan rutas temporales explícitas (`AiConfigTests.ConfigPath`,
  `AiConversationStoreTests._path`) → no se rompen con el cambio de ruta por defecto.

### Adaptaciones aplicadas (diferencias con el PR original)
1. **Migración solo en la ruta por defecto**: `Load(configPath == null)` invoca
   `AppPaths.TryMigrateLegacyFile(...)`; con ruta explícita (los tests) **no migra**.
   El PR original migraba siempre dentro de `Load`, lo que habría tocado el
   `%LOCALAPPDATA%` real o ficheros sueltos en `BaseDirectory` desde los tests.
2. **JSON corrupto → `.bak` + regenerar** (no solo "no sobrescribir"): se copia a
   `path + .bak` con `AppPaths.TryBackupCorruptFile`, se registra en el log y se
   generan los valores por defecto. Se conserva el test existente
   `Load_WithCorruptedJson_FallsBackToDefaults`.
3. **`Action<string>? log = null` opcional** en `AiConfig.Load/Save` y
   `AiConversationStore.Save/Load` → comportamiento por defecto intacto; la app pasa
   su `Log` real: `Form1` construye `new AI.AiAssistantForm(this, dlcCatalog, Log)` y
   `AiAssistantForm` propaga `_log` a cada llamada.
4. `AppPaths.SaveJsonAtomic` escribe a `.tmp` y luego `File.Replace`/`File.Move`
   (escritura atómica: un fallo a mitad no corrompe el archivo anterior).
5. `AppPaths` se añadió a **ambos** proyectos (`AppConfig.cs`); en el clon queda
   aún sin consumidores (preparado para traer el módulo IA).

### Tests añadidos (`AiConfigTests`, `AiConversationStoreTests` — solo IA)
- `DefaultConfigPath_IsInLocalAppData_NotNextToExe`
- `Load_MigratesLegacyConfigNextToExe_OnlyOnce`
- `Load_WithCorruptedJson_KeepsBackupBeforeRegenerating`
- `Save_UsesAtomicWrite_AndLeavesNoTmpFile`
- `Save_CreatesMissingDirectory`
- `DefaultPath_IsInLocalAppData_NotNextToExe` (conversación)
- `Save_UsesAtomicWrite_AndLeavesNoTmpFile` (conversación)
- `MigratesLegacyConversationNextToExe_OnlyOnce`

---

## PR #2 — fuga de `LanguageChanged`

### Qué se validó (todo confirmado)
- `SelectiveDownloadForm.cs` (línea 31) y `Form1.cs` (línea 33) se suscribían con
  **lambdas** `(_, __) => ApplyLanguage()` a un evento **estático**: la suscripción
  sobrevive al cierre de la ventana (imposible de anular) → al cambiar de idioma,
  `ApplyLanguage` tocaba controles destruidos → `ObjectDisposedException`.
- `SelectiveDownloadForm` **no tenía** `Dispose` propio; `Form1` ya tenía un
  `FormClosed += (_, __) => updaterDownload.Dispose();`.
- `LanguageManager.Set` solo dispara el evento cuando el idioma **cambia**
  (`if (_lang == lang) return;`), y el `Invoke` **no** tiene try/catch.

### Adaptaciones aplicadas
1. **`AiAssistantForm` queda FUERA del PR**: el parche de DeepSeek le añadía un
   `protected override void Dispose(bool disposing)`, pero ese formulario **ya tiene**
   `Dispose` en la línea 61 → el parche tal cual era **error de compilación**
   (override duplicado), y la ventana ni siquiera estaba suscrita al evento → código
   muerto. No se tocó.
2. **`Form1`**: en lugar de añadir `OnFormClosed` (que no existe), la anulación de la
   suscripción se puso dentro del `FormClosed` existente.
3. Se descartó el test propuesto `SubscriberThatThrows_DoesNotBreakOtherSubscribers`:
   exigiría try/catch en `Set`, que el propio PR declara fuera de alcance → habría
   fallado contra el comportamiento actual.
4. Guardia `if (IsDisposed || Disposing) return;` al inicio de
   `SelectiveDownloadForm.ApplyLanguage` (ventana de vida corta, es donde ocurría el crash).

### Tests añadidos (`LanguageManagerTests.cs` — ambos proyectos)
- `Set_NotifiesSubscribers_WhenLanguageChanges`
- `Set_DoesNotNotify_WhenLanguageIsSame`
- `UnsubscribedHandler_IsNotInvoked`
- Ojo: `LanguageManager` es estado global; los tests guardan/.restauran el idioma
  original y siempre se desuscriben en `finally`.

---

## PR #3 — catálogo de DLC

### Qué se validó (todo confirmado)
- `DlcCatalogService.TryParseAsset` solo aceptaba `.zip`/`.zip.001` → assets `.7z`
  descartados (**aunque `DlcArchive.IsSevenZip` ya los extrae**, incluido `.7z.NNN`:
  `DLCInstaller.cs:451`).
- Regex `^(EP|GP|SP|FP)\d{2}` con `match.Value` truncado: `EP1000` → `EP10`.
- `DlcUrlListParser.NormalizeId`: `dot >= 4 → id.Substring(0, 4)` constante:
  `__Installer.zip` → **`__In`** (bug real en producción) y `EP5.001` quedaba crudo.
- `SizeBytes` se quedaba con la primera parte detectada → "Tamaño total" en la UI
  informado con un valor incompleto.
- `while (true)` de paginación sin tope: una fuente que nunca devuelve página vacía
  = bucle infinito.

### Inconsistencias encontradas en el PR de DeepSeek (nunca ejecutado)
1. Su parche aceptaba solo `.zip`, `.zip.001`, `.7z`, `.7z.001` (**rechazaba
   `.zip.002`**), pero sus tests esperaban `SizeBytes = 80 + 20` → el propio PR no
   pasaba sus propios tests. *Resolución*: se aceptan **todas** las partes
   (`\.(zip|7z)\.\d{3}$`), única interpretación coherente con su intención.
2. Su `Normalize` devolvía `raw` para `__Installer.foo`, pero su test esperaba
   `__Installer` → código y test discrepaban. *Resolución*: se implementa **según el
   test** (se recorta en el primer punto para IDs que empiezan por `__`).
3. Dejó sin decidir `TryParse("EP1000")`. *Decisión*: **`false`** con regex
   `^(EP|GP|SP|FP)(\d{2,3})(?!\d)` (tope de 3 dígitos documentado en el test;
   `Normalize("EP1000")` devuelve el crudo sin truncar).

### Verificación con datos reales
Antes de fijar el regex se descargó `DLCsList.txt` del repo público
`williamconejo67-ai/Dlcs`: los IDs existentes son **solo** `__Installer`,
`EP01–EP21`, `FP01`, `GP01–GP12`, `SP01–SP84` → todos de 2 dígitos; el tope de 3
queda como futuro seguro (cuando `SP` pase de 99). Ningún ID real queda fuera.

### Adaptaciones aplicadas
1. **`IsZip001` → `IsMultipart`** en el record `DlcCatalogAsset` (solo lo usaban
   `DlcCatalogService` y los tests por posición; sin efectos colaterales). El campo
   significa "es la **primera** parte (.001)", el punto de entrada de un multipart.
2. **Agregación de `SizeBytes`** implementada con dos diccionarios por fuente
   (`plainSizeById`, `multipartSizeById`) en lugar del helper `GetAssetPrefix` del PR
   (más simple y mismo resultado). Regla: **manda el archivo completo `.zip`/`.7z` si
   existe; si solo hay partes, suma de todas; si no, tamaño parseado**. La URL
   candidata se decide por prioridad: primera parte (.001) > archivo completo > resto
   de partes (`AssetRank`), a igualdad gana la primera que llegó.
3. **`MaxPages = 100` por fuente (10.000 releases)** con `log(...)`, **sin** marcar
   `sourceCompleted` → la fuente que golpea el tope queda incompleta y sus datos **no
   se usan** (conservador: si es la única fuente, se lanza el error de la UI en lugar
   de confiar a medias).
4. `DlcUrlListParser` usa `DlcIds.Normalize` (se elimina el helper privado) y, ante un
   ID repetido, **prefiere la URL `.001`** si la que había no lo era (solo así se
   puede bajar un multipart completo). El filtro de `.zip.002` se mantuvo sin cambios
   (higiene, fuera del alcance del PR).

### Tests añadidos (ambos proyectos)
- `DlcIdsTests.cs` (nuevo): teoría con `EP05`, `ep05`→`EP05`, `EP100`, `EP1000`→false,
  `EP05foo`, `__Installer*`, `garbage`.
- `DlcCatalogServiceTests`: `Assert.Equal(80, …)` → **100** (manda el `.zip`
  completo) + 5 tests nuevos: IDs de 3 dígitos, assets `.7z`, suma de partes `.7z`,
  preferencia del completo sobre la suma, tope de 100 páginas.
- `DlcUrlListParserTests`: 3 tests nuevos (IDs de 3 dígitos + `__Installer` con
  sufijo, `.003` antes que `.001`, partes `.7z`).

---

## Hallazgo adicional: pruebas en paralelo

`DlcInstallerIntegrationTests` **falla de forma intermitente si se ejecutan las dos
suites de tests a la vez** (IA y clon): los dos procesos comparten rutas en
`%TEMP%` y las pruebas de instalación con ficheros reales se pisan. En ejecución
secuencial pasan siempre.

> **Regla**: nunca lanzar dos `dotnet test` de proyectos distintos en paralelo;
> encadenarlos en un mismo comando.

---

## Resultados de la aplicación

### Verificación
| Proyecto | Compilación | Tests antes | Tests después |
|---|---|---|---|
| IA | 0 advertencias / 0 errores (Debug y Release) | 96/96 | **130/130** (+34) |
| Clon | 0 advertencias / 0 errores (Debug y Release) | 51/51 | **77/77** (+26) |

### Commits (repo IA, rama `main`)
| Commit | Contenido |
|---|---|
| `d12fb9b` | `fix(ai)`: config y conversación en `%LOCALAPPDATA%` (PR #1) |
| `aa2f669` | `chore(release)`: bump 1.1.6 + CHANGELOG |
| `e1575a7` | `docs(maintenance)`: historial de releases en la rutina |

Los archivos fuente core (`Form1.cs`, `DlcCatalogService.cs`, `DlcIds.cs`, tests de
core…) siguen **sin trackear** por convención de "release hub"; solo se commitean los
archivos revisados a propósito (módulo IA, tests de IA, csproj, CHANGELOG, docs).

### Releases publicados
| | IA | Clon |
|---|---|---|
| Versión | v1.1.6 | v1.1.5 |
| Release ID | 416850130 | 416850351 |
| SHA256 (local = remoto) | `9ad7b21c…8f8ee` | `42aeb7d0…db20881` |
| API de auto-update | `v1.1.6` + `ArdillaUpdaterIA.exe` ✅ | `v1.1.5` + `ArdillaUpdater.exe` ✅ |

### Backups previos
`E:\Respaldo 7.10.2026\Ardilla ia\_backup_auditoria_2026-10\` — 19 archivos
originales (subcarpetas `IA\` y `Clon\`), copiados **antes** de cualquier edición.

---

## Pendiente / recomendaciones

- **Prueba manual de auto-update**: abrir la app anterior y comprobar que detecta la
  nueva versión (único paso del paso 10 de `RUTINA_RELEASE.md` que es de interfaz).
- **Tope de paginación** (`MaxPagesPerSource = 100`): suficiente hoy (10.000 releases
  por fuente); si el repo de DLCs lo supera, subir el tope o paginar con cursor.
- **Migración real de configuraciones de usuarios**: se probó con rutas explícitas en
  tests; la ruta por defecto se ejecuta en la primera arranque real tras actualizar.
- **Idea futura (no aplicada)**: `SeenPacksStore` podría migrar a `AppPaths` para
  que toda escritura de usuario pase por la misma central.

---

## Aplicación posterior: Lotes A y B (auditoría interna 2026-10)

Tras este informe se aplicaron dos lotes de mejoras validados con build 0/0 y suites
completas en ambos proyectos:

### Lote A
- Validación estricta de IDs de pack (`DlcIds.IsKnownPackId`) en el parser de listados:
  un ID que no sea EPnn/GPnn/SPnn/FPnn/__Installer se descarta (evita escrituras fuera
  de la carpeta del juego).
- `7z.exe` terminado (`Kill(entireProcessTree)`) al cancelar una extracción.
- `AboutForm` desuscribe `LanguageChanged` al cerrar (patrón PR #2 que se había olvidado).
- Mutex por variante (`AppConfig.SingleInstanceMutexName`): IA y clon ya no se excluyen
  entre sí.
- SHA-256 real en la auto-actualización (campo `digest` de la API de GitHub, con
  fallback al `SHA256:` del cuerpo del release) y rechazo de URLs no-HTTPS.
- Progreso del updater con throttle (solo al cambiar el porcentaje) y buffer de 256 KB.
- Clave `log.installerRepairMissing` (ES/EN) en el coordinador + test de paridad ES/EN.

### Lote B
- Guardián de estancamiento (`ReadWithStallGuardAsync`): si una descarga no recibe
  datos durante 60 s falla con error reintentable en vez de congelarse (el antiguo
  `DownloadTimeout` no lo usaba nadie; ahora es `AppConfig.DownloadStallTimeout`).
- Sondeo multipart honesto: 404/410 = fin legítimo de partes; los errores de red/5xx
  se reintentan y, si persisten, se aborta con error claro (antes un fallo de red se
  trataba como "fin de partes" y se instalaba un pack incompleto en silencio). La
  cancelación durante el sondeo se propaga como Cancelado.
- Limpieza de temporales en segundo plano con guardián de 30 min
  (`DlcTempStorage.MinAgeToDelete`) para no borrar carpetas de operaciones activas.
- Constantes nombradas de reintento de extracción
  (`AppConfig.ExtractionMaxRetries` / `ExtractionRetryDelayMs`).
- Comprobación de espacio libre antes de descargar (`EnsureEnoughFreeSpace`, margen de
  1 GB, best-effort si el disco no se puede consultar).

Verificación: builds 0 advertencias/0 errores; pruebas IA 142/142 y clon 89/89.
Backups pre-edición: `_backup_loteA_2026-10\` y `_backup_loteB_2026-10\`.
