# Documentación de Estructura - Ardilla Updater IA

*Documento interno para referencia y desarrollo*

## 1. Información General

**Nombre:** Ardilla Updater IA  
**Tipo:** Aplicación Windows Forms (.NET) para gestión y actualización de DLCs de The Sims 4  
**Repositorio:** https://github.com/williamconejo67-ai/ArdillaUpdaterIA  
**Versión actual:** 1.1.3 (según CHANGELOG.md)  
**Lenguaje:** C#  
**Framework:** Basado en .NET (ver global.json y ArdillaUpdater.csproj)

## 2. Estructura de Directorios

```
ArdillaUpdater/
├── AI/                        # Módulo de Inteligencia Artificial (Ollama)
├── ArdillaUpdater.Tests/      # Pruebas unitarias e integración
├── bin/                       # Archivos compilados (generado)
├── docs/                      # Documentación
│   ├── DOCUMENTACION_ESTRUCTURA_PROYECTO.md  # Referencia interna de estructura
│   └── maintenance/           # Notas de mantenimiento y releases
├── obj/                       # Archivos intermedios (generado)
├── Properties/                # Propiedades del proyecto
├── .gitignore
├── 1851305.largethumb.ico     # Icono de la aplicación
├── AboutForm.*                # Formulario "Acerca de"
├── app.ico
├── app.manifest
├── AppConfig.cs               # Configuración central (URLs, rutas, constantes)
├── ArdillaUpdater.csproj      # Archivo de proyecto
├── ArdillaUpdater.sln         # Solución Visual Studio
├── ArdillaUpdater.slnx        # Solución alternativa
├── CHANGELOG.md               # Historial de cambios
├── DLCInstaller.cs            # Instalador principal de DLCs
├── DLCIntegrityChecker.cs     # Verificación de integridad
├── Form1.*                    # Formulario principal de la aplicación
├── GamePathDetector.cs        # Detección automática de ruta del Sims 4
├── global.json                # Configuración global de SDK .NET
├── Guia_Ardilla_AI_Ollama.pdf # Guía original
├── GUIA_ARDILLA_AI_OLLAMA_PRINCIPIANTES.md # Guía para principiantes
├── HttpClientManager.cs       # Gestión centralizada de HttpClient
├── ILogger.cs / SimpleLogger.cs # Sistema de logging
├── LanguageManager.cs         # Gestión de idiomas
├── Models.txt                 # Lista de modelos IA disponibles
├── README.md                  # Guía principal (GitHub)
├── LEEME_GUIA_RAPIDA.txt      # Resumen
├── PackItem.cs                # Modelo de pack DLC
├── ParallelManager.cs         # Gestión de descargas/paralelismo
├── PermissionHelper.cs        # Ayudas para permisos
├── Program.cs                 # Punto de entrada
├── ProgressInfo.cs            # Info de progreso
├── SafeCopy.cs                # Copia segura de archivos
├── SeenPacksStore.cs          # Persistencia de packs vistos
├── SelectiveDownloadForm.*    # Formulario de descarga selectiva
├── UpdaterDownloadService.cs  # Servicio de autoactualización
```

## 3. Módulo AI (Ollama Integration)

Ubicado en `AI/`. Gestiona todo el asistente inteligente Ardilla AI.

| Archivo | Propósito |
|---|---|
| **AiAssistantForm.cs** | Interfaz gráfica del chat de Ardilla AI. Contiene UI, chips rápidos, streaming, callbacks. |
| **AiConfig.cs** | Configuración de IA (endpoint, modelo, temperatura, historial). Carga/guarda `ai_config.json`. Lee `Models.txt`. |
| **AiSettingsForm.cs** | Ventana de ajustes del asistente IA. Permite cambiar endpoint/modelo/temperatura. |
| **ArdillaAiAgent.cs** | Agente con herramientas (tools). Define funciones: `consultar_estado_juego`, `listar_dlcs_disponibles`, `diagnosticar_errores_juego`, `verificar_espacio_disco`, `recomendar_packs`, `seleccionar_packs_para_instalar`, `verificar_integridad_juego`. Ejecuta lógica para cada tool call. |
| **ArdillaAiService.cs** | Cliente HTTP para Ollama `/api/chat`. Gestiona historial, normalización de tool_calls, streaming, poda de historial, fallback ante errores 400/500. |

**Flujo típico:** Usuario -> AiAssistantForm -> ArdillaAiAgent -> ArdillaAiService (Ollama) -> Agent ejecuta tools -> Respuesta final.

## 4. Módulo DLCs

Archivos principales para gestión de DLCs.

| Archivo | Propósito |
|---|---|
| **AppConfig.cs** | URLs centrales: `DlcsListSources`, `DlcsUrlsListSources`, `DlcReleaseApiSources`, `UpdaterLatestReleaseApi`. Rutas por defecto de The Sims 4. Timeouts, reintentos, límites. |
| **DlcCatalogService.cs** | Carga catálogo desde GitHub (`DLCsList.txt`). Parsea nombres ES/EN. Cache y validación. |
| **DlcUrlListParser.cs** | Parsea `DlcsUrls.txt` con URLs de descarga de cada pack. |
| **DlcArchive.cs** | Manejo de archivos comprimidos (ZIP, multipart ZIP, 7z). Selección de extractor según tipo. |
| **DLCInstaller.cs** | Instalación física de DLCs. Extracción, verificación, copia a ruta del juego. Prevención de sobrescritura indebida. |
| **DlcInstallationCoordinator.cs** | Coordina instalación/actualización de múltiples packs. Orquesta validaciones, temporales, progreso. |
| **DlcTempStorage.cs** | Gestión aislada de directorios temporales para DLCs. Limpieza segura (solo artefactos propios). |
| **DLCIntegrityChecker.cs** | Verificación de integridad post-instalación. |

## 5. Núcleo de la Aplicación

| Archivo | Propósito |
|---|---|
| **Form1.cs / Form1.Designer.cs / Form1.resx** | Ventana principal. UI, detección juego, listado DLCs, botones actualizar/descarga selectiva, integración con Ardilla AI. |
| **SelectiveDownloadForm.cs** | Interfaz de selección múltiple de packs para descargar/instalar. Filtrado, marcado masivo. |
| **AboutForm.cs** | Acerca del programa, versión, créditos. |
| **Program.cs** | Entry point. Single instance (Mutex), arranque de aplicación. |
| **GamePathDetector.cs** | Detecta ruta TS4 en ubicaciones comunes (EA, Steam, Epic, Origin) + rutas personalizadas/Steam libraries. |
| **UpdaterDownloadService.cs** | Autoactualizador: descarga release, valida SHA-256 si disponible, reemplazo seguro con backup. |

## 6. Utilidades y Soporte

| Archivo | Propósito |
|---|---|
| **HttpClientManager.cs** | Centraliza instancias HttpClient con timeouts/configuración. Evita socket exhaustion. |
| **ParallelManager.cs** | Control de paralelismo para descargas/operaciones concurrentes. |
| **SafeCopy.cs** | Operaciones de copia con verificación. |
| **PermissionHelper.cs** | Solicitud/verificación de permisos si aplica. |
| **ProgressInfo.cs** | DTO para reportar progreso entre componentes. |
| **PackItem.cs** | Modelo de datos para packs (ID, nombre ES/EN, tamaño, estado, URL...). |
| **SeenPacksStore.cs** | Persistencia local de estado. |
| **ILogger.cs / SimpleLogger.cs** | Interfaz + implementación simple de logging. |

## 7. Configuración y Dependencias

| Archivo | Propósito |
|---|---|
| **ArdillaUpdater.csproj** | Referencias, configuración build, recursos, icono. |
| **global.json** | Versión SDK .NET usada. |
| **packages.lock.json** | Lockfile de paquetes NuGet. |
| **app.manifest** | Manifesto Windows (UAC, DPI awareness...). |
| **.gitignore** | Exclusiones Git (bin/obj/packages, configs locales...). |

## 8. Pruebas (ArdillaUpdater.Tests)

Suite de tests para validar lógica crítica.

| Archivo | Alcance |
|---|---|
| **AiComponentsTests.cs** | Tests relacionados con componentes IA. |
| **DlcArchiveTests.cs** | Tests extracción ZIP/multipart/7z. |
| **DlcCatalogServiceTests.cs** | Carga/parsing catálogo. |
| **DlcInstallationCoordinatorTests.cs** | Coordinación instalación. |
| **DlcInstallerIntegrationTests.cs** | Integración instalador. |
| **DlcTempStorageTests.cs** | Limpieza temporales aislados. |
| **DlcUrlListParserTests.cs** | Parsing URLs. |
| **GamePathDetectorTests.cs** | Detección rutas juego. |
| **UpdaterDownloadServiceTests.cs** | Autoactualizador + SHA-256. |

## 9. Documentación

Toda la documentación interna se agrupa en `docs/`. Los archivos de la raíz (`README.md`, `CHANGELOG.md` y guías) se mantienen ahí por convención de GitHub.

| Archivo | Ubicación | Propósito |
|---|---|---|
| **README.md** | Raíz | Guía principal para usuarios (GitHub). Orientada a principiantes con Ollama. |
| **GUIA_ARDILLA_AI_OLLAMA_PRINCIPIANTES.md** | Raíz | Misma guía (copia local de respaldo). |
| **Guia_Ardilla_AI_Ollama.pdf** | Raíz | Guía original en PDF. |
| **LEEME_GUIA_RAPIDA.txt** | Raíz | Resumen rápido de la guía. |
| **CHANGELOG.md** | Raíz | Historial de versiones con formato Keep a Changelog. |
| **DOCUMENTACION_ESTRUCTURA_PROYECTO.md** | `docs/` | Este documento. Referencia interna de la estructura del proyecto. |
| **README-release.md** | `docs/maintenance/` | Checklist/proceso para releases. |
| **release-notes-template.md** | `docs/maintenance/` | Plantilla para notas de release. |

### Estructura de `docs/`

```
docs/
├── DOCUMENTACION_ESTRUCTURA_PROYECTO.md   # Referencia interna de estructura
└── maintenance/
    ├── README-release.md                  # Proceso de release
    └── release-notes-template.md          # Plantilla de notas de release
```

## 10. Puntos Clave para Desarrollo

- **Arquitectura modular**: Separación clara AI / DLC / Core / Utils. Facilita testing.
- **Testeable**: Servicios extraídos (DlcCatalogService, DlcInstallationCoordinator, UpdaterDownloadService) con dependencias inyectables.
- **Seguridad de temporales**: `DlcTempStorage` limita la limpieza a artefactos propios de Ardilla Updater.
- **IA local-first**: Usa Ollama API `/api/chat` con tool calling. Soporta streaming y fallback ante modelos con problemas.
- **Integración UI**: `AiAssistantForm` se comunica con `Form1` mediante callbacks (`OnPacksSelectedByAi` -> `OpenSelectiveDownloadWithFilter`).
- **Robusto con la red**: Reintentos, timeouts configurables, manejo de fallos de GitHub API.
- **Multi-formato DLC**: Soporta ZIP normal/multipart + 7-Zip opcional para `.7z`.

## 11. Archivos Generados (ignorar en Git)

- `bin/`, `obj/` - salida de compilación
- `*.user` (ArdillaUpdater.csproj.user) - configuraciones locales de Visual Studio
- `ai_config.json` - configuración IA local (se genera en runtime)

## Notas

Este documento está pensado para consulta rápida interna. Para guiar a usuarios finales, usar **README.md**.
