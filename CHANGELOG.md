# Changelog

Todos los cambios importantes del proyecto se pueden registrar aquí.

El formato recomendado es:

- `Added` para nuevas funciones
- `Changed` para cambios de comportamiento
- `Fixed` para correcciones
- `Removed` para cosas eliminadas
- `Security` para mejoras de seguridad o integridad

## [1.1.8] - 2026-10-20

### Added

- **Botón "🩺 Diagnosticar juego"**: analiza los errores del juego (`lastException.txt`) directamente desde la ventana principal, sin tener que pasar por el chat de la IA. Muestra un informe con la carpeta del juego, el tipo de excepción, los mods sospechosos y un extracto, en una ventana con opción de **copiar** el informe.
- **Botón "🛡️ Reiniciar como admin"**: relanza la aplicación pidiendo permisos de administrador (UAC), útil para instalar en rutas protegidas como `Program Files`. Si ya se está ejecutando como administrador, lo avisa en lugar de reabrir la ventana.
- **Diagnóstico robusto del juego (Lote D)**: la búsqueda de la carpeta de usuario ahora contempla **OneDrive**, "Documentos" redirigido y el nombre del juego en **otros idiomas**; además avisa cuando el error es **antiguo** y busca `lastException.txt` también dentro de `Mods`.

## [1.1.7] - 2026-10-20

### Security

- **Clave de API de la IA cifrada en disco**: `ai_config.json` ya no guarda la clave en texto plano; se cifra con DPAPI (solo la puede descifrar el usuario de Windows que la guardó). Una configuración antigua en texto plano se **migra** automáticamente al volver a guardar, y si el archivo viene de otro usuario/equipo (DPAPI no puede descifrarlo) se deja la clave vacía en vez de romper la app.
- **Auto-actualización verificada**: se valida el **SHA-256 real** del ejecutable descargado (campo `digest` de la API de GitHub, con respaldo en el `SHA256:` del cuerpo del release) y se rechazan las URLs que no sean **HTTPS**.
- **Validación estricta de IDs de pack**: un ID que no sea `EPnn`/`GPnn`/`SPnn`/`FPnn`/`__Installer` se descarta durante el parseo del listado, de modo que un nombre inesperado nunca pueda escribir fuera de la carpeta del juego.
- **Descargas incompletas ya no se cuelan**: el sondeo de partes multipart distingue "no hay más partes" (404/410) de un error real (5xx/red), que se reintenta y, si persiste, aborta con un error claro. Antes un fallo de red se interpretaba como "fin de las partes" y podía instalarse un pack incompleto en silencio.

### Fixed

- **Descargas que se quedaban colgadas**: nuevo guardián de estancamiento; si una descarga deja de recibir datos durante 60 s falla con un error reintentable en lugar de dejar la app congelada. Se aplica tanto a la descarga e instalación de DLC como a la auto-actualización (el antiguo `DownloadTimeout` no lo usaba nadie).
- **`7z.exe` ya no queda vivo al cancelar**: si el usuario cancela durante la extracción, el proceso de `7z` se termina por completo (antes podía seguir trabajando en segundo plano).
- **Cambio de idioma tras cerrar ventanas**: `AboutForm` desuscribe `LanguageChanged` al cerrarse (mismo patrón que la corrección de la versión anterior); antes una ventana ya cerrada seguía recibiendo el evento.
- **Dos instancias a la vez**: cada variante (IA y clon) usa un mutex propio, así que ya no se excluyen entre sí al ejecutarse a la vez.

### Changed

- **Interfaz totalmente traducible**: los estados de progreso de la instalación (`Descargando`, `Extrayendo`, `Completado`, `Cancelado`, `Procesando`) y los mensajes del flujo de actualización ya no están escritos en español fijo; pasan por el mismo traductor que el resto de la ventana (ES/EN).
- **Limpieza de archivos temporales en segundo plano**: al arrancar ya no bloquea la ventana, y un guardián de antigüedad (30 min) impide borrar carpetas de operaciones en curso.
- **Historial de la IA con tope en memoria**: se mantiene un máximo de mensajes durante la sesión (el recorte al enviar al modelo ya existía); al recortar se respeta el `system` inicial y no se dejan mensajes `tool` sin su `assistant`.
- **Respuestas de la IA más fluidas en pantalla**: el texto que llega por fragmentos del modelo se dibuja por bloques (cada 120 ms o al cerrar párrafo) en lugar de repintar el chat por cada carácter.
- **Código interno del coordinador sin duplicar**: la decisión de "reparar / descargar / informar" y el reparto de packs afectados tras reparar `__Installer` se unifican en dos ayudantes (mismo comportamiento, cubierto por las pruebas).
- **Comprobación de espacio libre antes de descargar** (tamaño del pack + 1 GB de margen; no bloquea si el disco no se puede consultar).
- Constantes de reintento de extracción nombradas (`5` intentos, `400 ms` de espera).

### Removed

- Código sin usar: relanzado como administrador (`RestartAsAdmin`), copia de archivos por fecha (`CopyFileIfNewer`), detección de varias instalaciones del juego (`DetectAllGamePaths`), propiedad de tamaño de pack sin uso (`SizeText`) y la interfaz de logging `ILogger`/`SimpleLogger` (nadie la consumía; el registro real es `Program.AppendLog`).

### Notes

- Verificación de esta versión: compilación `0 advertencias, 0 errores` (Debug y Release); suite completa `148/148` pruebas superadas.
- Claves nuevas de traducción añadidas en ES y EN con paridad exacta (110/110).
- Detalle técnico completo en `docs/maintenance/AUDITORIA-2026-10.md` (Lotes A, B y C).

## [1.1.6] - 2026-10-18

### Fixed

- **Configuración y conversación ya no se pierden en silencio**: `ai_config.json` y `ai_conversation.json` se guardan ahora en `%LOCALAPPDATA%\ArdillaUpdater` (carpeta con escritura garantizada) en lugar de junto al `.exe`; en el primer arranque **migran** automáticamente los archivos de instalaciones antiguas y, si un JSON está corrupto, se guarda una copia `.bak` antes de regenerarlo. Antes un `catch` vacío ocultaba cualquier fallo de escritura.
- **Cambio de idioma tras cerrar ventanas**: `LanguageManager.LanguageChanged` se suscribe con métodos con nombre y se anula al cerrar (`Dispose`/`FormClosed`). Antes las lambdas quedaban clavadas en el evento estático y un cambio de idioma posterior tocaba controles destruidos y terminaba la app (`ObjectDisposedException`); `ApplyLanguage` ahora comprueba además `IsDisposed/Disposing`.
- **Catálogo de DLC**: se aceptan los assets `.7z` (que el extractor ya soportaba) y **todas** las partes de un multipart (`.zip.002`, `.7z.003`…); la detección de IDs pasa a `DlcIds` con tope de 3 dígitos y lookahead, así `EP1000` ya no se convierte en `EP10` ni `__Installer.zip` en `__In`.
- **Tamaño del pack informado con verdad**: si un release publica el archivo completo `.zip`/`.7z` manda su tamaño; si solo hay partes multipart, se suma el total de todas ellas (antes solo se quedaba el tamaño de la primera parte).
- **Paginación de GitHub acotada**: tope de 100 páginas por fuente (10.000 releases); antes una fuente que nunca devolvía página vacía dejaba la app en bucle infinito. La fuente que golpea el tope **no** se marca como completada (conservador: sus datos no se usan).
- `DlcUrlListParser` prefiere la URL de la **primera parte** (`.001`) cuando un mismo ID aparece repetido fuera de orden (solo así se puede descargar un pack multipart completo).

### Added

- `AppPaths`: central de rutas de usuario con escritura atómica (`.tmp` + reemplazo), migración única desde la carpeta del `.exe` y backup de corruptos; `DlcIds`: normalizador único de IDs de DLC.
- Pruebas nuevas: `LanguageManagerTests`, `DlcIdsTests` y extensiones de `AiConfigTests`, `AiConversationStoreTests`, `DlcCatalogServiceTests` y `DlcUrlListParserTests` (34 casos nuevos).

### Notes

- Auditoría externa 2026-10: los PR #1–#3 se validaron contra el código real y contra los datos públicos del repo de DLCs antes de aplicarlos (adaptaciones documentadas en los comentarios de cada archivo).
- Verificación de esta versión: compilación `0 advertencias, 0 errores`; suite completa `130/130` pruebas superadas.

## [1.1.5] - 2026-10-17

### Added

- **Streaming en la interfaz del asistente**: la respuesta se escribe en el chat en tiempo real durante **todas las vueltas del bucle agéntico**, con detección para no duplicar la respuesta final.
- **Persistencia de la conversación**: la charla se guarda en `ai_conversation.json` junto al ejecutable y se restaura al volver a abrir la ventana; **instancia única** del asistente (no se abren varias ventanas a la vez).
- **Catálogo real como referencia**: `recomendar_packs` añade la disponibilidad real de cada pack y `seleccionar_packs_para_instalar` **valida los IDs contra el catálogo** (descarta los que el modelo "invente").
- **Diagnóstico mejorado de errores de The Sims 4**: `Sims4ErrorAnalyzer` extrae el tipo de excepción y el **mod sospechoso** de `lastException.txt`, con extracto y detección de `lastCrash.txt`.
- **Ajustes avanzados**: temperatura y `num_ctx` editables en la ventana de Ajustes, con validación del modelo contra `/api/tags` al probar la conexión.
- **Botones de utilidad**: 📋 *Copiar última respuesta* y 💾 *Exportar chat* (diálogo "Guardar como" en `.txt` o `.md`).
- **Notas técnicas internas**: los errores de conexión/timeout ya no entran al historial como si fueran respuestas de la IA; se guardan como nota `system` que no se muestra ni se restaura en el chat.
- Pruebas nuevas: `AiConversationStoreTests` y cobertura adicional de poda de historial.

### Changed

- La poda del historial pasa a **presupuesto de caracteres** (`MaxContextChars` = 24000, salidas de herramienta recortadas a `MaxToolMessageChars` = 4000) sin romper el emparejamiento `assistant` ↔ `tool`.
- `num_ctx` por defecto a **8192** (antes fijo en 8192 sin ser configurable) y configurable desde Ajustes.
- Interfaz con **escalado DPI correcto** (`AutoScaleMode.Dpi`): cabecera con los 4 botones (Ajustes, Copiar, Exportar, Limpiar) en una fila reposicionada dinámicamente para no quedar recortada a 100%/150% de escala, y panel inferior con Enviar y Detener **sin solaparse**.

### Fixed

- Los mensajes técnicos de error (`⚠️`, `❌`, etc.) se dejaban de registrar como respuesta del asistente, contaminando el historial y la conversación persistida.
- Botones de la cabecera desaparecidos a escala 150% (panel con `Dock` + `AutoSize` + `WrapContents` recortaba la segunda fila) y botones Enviar/Detener solapados (área de 84 px para 96 px de botones).
- Restauración de conversación: los mensajes `system` internos ya no aparecen en el chat al reabrir la ventana.

### Notes

- Verificación de esta versión: compilación `0 advertencias, 0 errores`; suite completa `96/96` pruebas superadas. Prueba manual con Ollama a escala 100% y 150%: cabecera y botones Enviar/Detener correctos.

## [1.1.4] - 2026-10-17

### Fixed

- Validación de extracciones: ahora se exige **al menos un archivo con tamaño mayor que 0** en todo el árbol extraído antes de dar una instalación por válida. Antes bastaba con encontrar una subcarpeta (aunque estuviera vacía) o un archivo de 0 bytes, así que un ZIP que solo traía carpetas vacías —o un `Delta.package` vacío— se aceptaba como instalación correcta.
- `DLCIntegrityChecker.FolderHasRealContent` deja de considerar "contenido" una subcarpeta vacía o un archivo de 0 bytes, y ya no filtra por extensión (evita marcar como incompletos packs con extensiones no listadas).
- `__Installer`: antes de copiar se valida que la extracción haya producido archivos reales (no solo carpetas vacías).
- DLC normal: la validación de contenido se hace **antes** de copiar, de modo que ya no queda una carpeta vacía en la carpeta del juego cuando la extracción no aporta nada.

### Added

- Pruebas de regresión (casos que antes se colaban): ZIP de `__Installer` solo con carpetas vacías, ZIP de DLC solo con carpetas vacías, ZIP de DLC solo con un `Delta.package` de 0 bytes, y una prueba de reparación real que crea `__Installer\DLC\<id>` y comprueba que el pack queda completo.

### Changed

- `DLCInstaller` reutiliza `DLCIntegrityChecker.FolderHasRealContent` y se elimina el helper `IsDirectoryEmpty`.

### Notes

- Verificación de esta versión: compilación `0 advertencias, 0 errores`; suite completa `88/88` pruebas superadas.

## [1.1.3] - 2026-10-16

### Added

- Pruebas de IA: `AiConfigTests`, `ArdillaAiServiceTests` y `ArdillaAiAgentTests` (valores por defecto, migración de modelo, JSON corrupto, HTTP simulado, streaming, fallback 400/500, poda de historial, despacho de herramientas y tope de iteraciones).

### Changed

- `ArdillaAiService` inyecta el `HttpClient`, implementa `IDisposable` y distingue el cliente propio del cliente cedido por el llamador.
- `AiConfig.Load`, `AiConfig.Save` y `AiConfig.GetAvailableModels` aceptan rutas opcionales para poder probarse con archivos temporales.
- Se exponen `ArdillaAiAgent.RecommendPacks` y `ArdillaAiAgent.TryRecoverToolCallsFromPlainText` como `internal static` para poder probarse.
- `AiAssistantForm` cancela y libera el `CancellationTokenSource` en un único punto (intercambio atómico) y deja de actualizar la interfaz durante el cierre, evitando dobles liberaciones y accesos a controles ya destruidos.
- `global.json` fija el SDK `10.0.402` con `rollForward: latestFeature`.

### Removed

- Se elimina `ArdillaUpdater.Tests/AiComponentsTests.cs` (pruebas redundantes; su cobertura vive ahora en las suites de IA).

### Fixed

- Instalación en un sistema limpio: `IsMissingInstallerDlcFolder` ya no exige que exista la carpeta principal del pack. Así, cuando el juego trae su propia carpeta `__Installer` pero los packs aún no están instalados, se repara `__Installer` **antes** de descargarlos. Antes se descargaban los packs sin su subcarpeta `__Installer\DLC\<id>`, fallaba la validación final y había que reiniciar la aplicación para que funcionara.
- Se evita el "éxito falso" cuando un archivo se extrae vacío o corrupto: ahora se comprueba que la extracción haya producido contenido antes de dar la instalación por completada (incluido `__Installer`).
- Nuevas pruebas de regresión: `DLCIntegrityCheckerTests`, caso de sistema limpio en `DlcInstallationCoordinatorTests` y archivo vacío en `DlcInstallerIntegrationTests`.

### Notes

- Verificación de esta versión: compilación `0 advertencias, 0 errores`; suite completa `79/79` pruebas superadas.

## [1.1.2] - 2026-10-05

### Changed

- Se centraliza la selección del extractor: ZIP y ZIP multipart usan .NET; 7-Zip queda opcional para archivos 7z.
- Se aíslan los temporales de DLC y se limita su limpieza a artefactos propios de Ardilla Updater.
- Se extrae la carga y combinación del catálogo DLC y la coordinación de instalación/actualización a servicios testeables.
- Las consultas fallidas de GitHub ya no se muestran como actualizaciones exitosas; las consultas válidas sin paquetes se informan explícitamente.
- Se retira la fuente de releases `l1ntol/lunia-dlc`, que respondía HTTP 404.
- Se extrae la descarga del autoactualizador, se valida SHA-256 cuando está disponible y el reemplazo del ejecutable conserva la versión anterior ante errores.
- La detección del juego verifica `Game\Bin\TS4_x64.exe` y contempla bibliotecas Steam personalizadas.
- Se agregan pruebas automatizadas para catálogo, ZIP multipart, instalación, temporales, coordinación DLC, autoactualización y detección de rutas.

### Removed

- Se elimina la prueba temporal de detección de la ruta del juego al iniciar.

### Fixed

- Se mejora el mensaje cuando no está disponible 7-Zip para un archivo 7z.
- Se evita borrar temporales ajenos durante el inicio o la cancelación.

## [1.1.1] - 2026-10-04

### Fixed

- Se corrige la instalacion de DLCs cuyos archivos estan directamente en la raiz del ZIP.
- Se verifica que la copia de los archivos haya finalizado antes de reportar la instalacion como exitosa.

## [1.1.0] - 2026-05-25

### Removed

- Soporte de respaldo externo para updater y DLCs.
- Scripts y documentos de publicacion del respaldo externo.

### Changed

- La aplicacion queda configurada para usar solamente GitHub.
- La version base del proyecto sube a `1.1.0`.

## [1.0.7]

### Added

- Base de version anterior del proyecto.

### Notes

- Usa esta seccion como referencia historica si luego reconstruyes cambios anteriores.
