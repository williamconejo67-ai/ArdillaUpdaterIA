# Release Guide

Este archivo resume el flujo recomendado para publicar una nueva version de `ArdillaUpdater` usando solamente GitHub.

## Objetivo

En cada release debes dejar consistentes dos cosas:

1. La version del proyecto
2. El tag y los assets de GitHub Releases

La aplicacion usa GitHub para:

- Buscar la ultima version publicada
- Descargar `ArdillaUpdater.exe`
- Leer `DlcsUrls.txt` y `DLCsList.txt` desde el repositorio `Dlcs`
- Descargar los DLCs desde las URLs publicadas en `DlcsUrls.txt`

## Antes de empezar

Confirma esto:

- La version del proyecto es la correcta en `ArdillaUpdater.csproj`
- Tienes el `DlcsUrls.txt` actualizado en GitHub
- Tienes el `DLCsList.txt` actualizado en GitHub
- Vas a usar el mismo tag en GitHub Releases y en las URLs de DLC, por ejemplo `v1.1.1`

## Flujo recomendado

### 1. Publicar el updater

Genera el ejecutable con la version actual:

```powershell
dotnet publish .\ArdillaUpdater.csproj -c Release -r win-x64 --self-contained true -o .\publish\updater-publish-1.1.1 /p:Version=1.1.1
```

### 2. Publicar en GitHub Releases

Sube o verifica en GitHub:

- `ArdillaUpdater.exe`
- Los assets DLC de esa version
- El tag correcto, por ejemplo `v1.1.1`

### 3. Validar antes de cerrar la release

Revisa que:

- La app detecte el ultimo release desde GitHub
- El release tenga un asset llamado `ArdillaUpdater.exe`
- `DlcsUrls.txt` apunte a URLs disponibles
- `DLCsList.txt` cargue correctamente

## Checklist corta

- Version correcta en el proyecto
- Tag correcto en GitHub
- `ArdillaUpdater.exe` subido a GitHub Releases
- Assets DLC subidos o enlazados en GitHub
- `DlcsUrls.txt` actualizado
- `DLCsList.txt` actualizado
- Prueba de actualizacion completada
