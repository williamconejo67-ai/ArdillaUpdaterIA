# Changelog

Todos los cambios importantes del proyecto se pueden registrar aquí.

El formato recomendado es:

- `Added` para nuevas funciones
- `Changed` para cambios de comportamiento
- `Fixed` para correcciones
- `Removed` para cosas eliminadas
- `Security` para mejoras de seguridad o integridad

## [Unreleased]

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

### Notes

- Verificación de esta versión: compilación `0 advertencias, 0 errores`; suite completa `72/72` pruebas superadas.

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
