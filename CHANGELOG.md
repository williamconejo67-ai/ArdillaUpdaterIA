# Changelog

Todos los cambios importantes del proyecto se pueden registrar aquí.

El formato recomendado es:

- `Added` para nuevas funciones
- `Changed` para cambios de comportamiento
- `Fixed` para correcciones
- `Removed` para cosas eliminadas
- `Security` para mejoras de seguridad o integridad

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
