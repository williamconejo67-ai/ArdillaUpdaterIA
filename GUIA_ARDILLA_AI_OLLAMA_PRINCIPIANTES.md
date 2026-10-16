# Guía de Ardilla Updater IA + Ollama (Para Principiantes)

Esta guía está pensada para usuarios que nunca han utilizado Ollama o IA local. Te llevará paso a paso desde la instalación hasta resolver los problemas más comunes.

---

## 1. ¿Qué es Ardilla Updater IA?

**Ardilla Updater IA** es una herramienta para gestionar, actualizar e instalar DLCs (contenidos descargables) de *The Sims 4*. Cuenta con un asistente inteligente llamado **Ardilla AI** que funciona con [Ollama](https://ollama.com/es) (IA local).

### ¿Para qué te sirve?

- **Gestionar DLCs**: Comprueba qué packs tienes instalados y cuáles te faltan.
- **Descarga selectiva**: Te permite elegir exactamente qué DLCs descargar e instalar.
- **Diagnóstico inteligente**: Analiza archivos `lastException.txt` y `lastCrash.txt` para detectar posibles problemas con mods o el juego.
- **Recomendaciones**: Te sugiere los mejores packs según el tema que busques (magia, familia, construcción, etc.)
- **Verificaciones útiles**: Comprueba espacio en disco e integridad de los archivos del juego.
- **Todo en español**: El asistente responde de forma clara, amable y fácil de entender.

> **Importante:** Ardilla AI funciona **100% en local**. Las consultas a IA solo van a tu servidor de Ollama (`http://localhost:11434`). Las descargas de DLCs provienen de los repositorios oficiales de GitHub configurados en la aplicación.

---

## 2. Requisitos

Antes de empezar, necesitas:

- **Sistema operativo**: Windows 10 o Windows 11 (recomendado).
- **RAM**: Mínimo 8 GB. Si usarás modelos grandes, se recomienda 16 GB o más.
- **Espacio en disco**: Al menos 5-10 GB libres para descargar Ollama y el modelo de IA. Ten en cuenta que los DLCs del Sims 4 pueden ocupar 60-80 GB.
- **Conexión a Internet**: Necesaria para descargar Ollama, el modelo de IA y los DLCs desde GitHub.
- **The Sims 4 instalado**: Puede estar en EA, Steam, Epic Games u Origin.

---

## 3. Instalación de Ollama

Ollama es el programa que ejecutará el modelo de IA en tu PC.

### Paso 1: Descargar Ollama

1. Ve a la página oficial: [https://ollama.com/download](https://ollama.com/download).
2. Descarga el instalador para Windows (`OllamaSetup.exe`).
3. Ejecuta el instalador y sigue los pasos (instalación estándar).

### Paso 2: Comprobar que Ollama esté funcionando

1. Después de instalarlo, Ollama se ejecuta automáticamente en segundo plano.
2. Busca el icono de **Ollama** en la bandeja del sistema (esquina inferior derecha de la barra de tareas). Si lo ves, está corriendo.
3. Puedes comprobarlo también abriendo tu navegador y yendo a: [http://localhost:11434](http://localhost:11434). Si aparece una página en blanco o no da error, significa que está funcionando correctamente.

> **Truco:** Ollama debe estar **abierto y corriendo** siempre que quieras usar Ardilla AI. Si no lo está, el asistente te avisará con un error de conexión.

---

## 4. Descargar un modelo de IA

Un "modelo" es el cerebro con el que responderá Ardilla AI. Para empezar, te recomendamos usar un modelo **ligero y rápido**, ideal para principiantes.

### Opción recomendada (Inicio rápido)

Abre una terminal de comandos (`cmd`, `PowerShell` o `Terminal`) y ejecuta uno de estos comandos:

| Comando | Tamaño aproximado | Recomendado para |
|---|---|---|
| `ollama pull qwen3.5:0.8b` | ~0.5 GB | **Recomendado para empezar.** Muy rápido, funciona bien en PCs con 8 GB de RAM. |
| `ollama pull gemma3:4b` | ~2.5 GB | **Mejor equilibrio.** Más inteligente, recomendable si tienes 8-16 GB de RAM. |
| `ollama pull dolphin-mistral:latest` | ~4.1 GB | Buen razonamiento, requiere un poco más de recursos. |

> **Recomendación para principiantes:** Empieza con `qwen3.5:0.8b`. Es el más seguro para evitar lentitudes.

### Comprobar modelos descargados

Para ver qué modelos tienes instalados, ejecuta:

```bash
ollama list
```

Verás una lista con el nombre de cada modelo, su tamaño y cuándo fue descargado.

---

## 5. Primera ejecución de Ardilla Updater IA

1. Abre **Ardilla Updater IA** (`ArdillaUpdaterIA.exe`).
2. En la ventana principal, la aplicación intentará **detectar automáticamente la ruta de The Sims 4**. Si aparece correctamente, ¡perfecto!
3. Si no la detecta, pulsa el botón **"Detectar"** o selecciona la ruta manualmente (suele ser: `C:\Program Files\EA Games\The Sims 4` en EA App, o la correspondiente a Steam/Epic).
4. Pulsa en la pestaña o botón para abrir **"Ardilla AI"** (Asistente Inteligente).
5. ¡Listo! Ya puedes hacer tu primera pregunta.

### Primera prueba

Escribe esto en el chat de Ardilla AI:

```text
¿Qué DLCs me faltan?
```

El asistente usará sus herramientas automáticamente y te mostrará la ruta detectada, qué packs tienes instalados y cuáles te faltan (con sus nombres en español).

---

## 6. Configurar Ardilla AI (Ventana Ajustes)

Para personalizar el asistente, pulsa el botón **"⚙️ Ajustes"** (arriba a la derecha en la ventana de Ardilla AI).

| Opción | Explicación | Valor recomendado |
|---|---|---|
| **Endpoint (Servidor IA)** | Dirección de tu servidor Ollama. Nunca lo cambies a no ser que uses Ollama en otro PC. | `http://localhost:11434` |
| **Modelo** | Modelo de IA a usar. Se llena automáticamente desde `Models.txt`. También aparecen los que tengas descargados en Ollama. | `qwen3.5:0.8b` (principiantes) o `gemma3:4b` (mejor calidad) |
| **Temperatura** | Controla la creatividad de las respuestas. 0 = muy preciso, 1.0 = más creativo. | `0.5` (equilibrio perfecto) |
| **Máx. mensajes historial** | Cuántos mensajes recuerda la conversación. Más valor = mejor contexto, pero consume más recursos. | `20` (recomendado para principiantes) |

Pulsa **"Guardar"** para aplicar los cambios. El asistente actualizará la etiqueta superior mostrando el modelo y endpoint nuevos.

> **Nota:** El archivo `Models.txt` que viene en el proyecto sirve para rellenar el desplegable de modelos. Si descargas un modelo nuevo con `ollama pull`, aparecerá automáticamente en la lista si su nombre coincide o simplemente puedes seleccionarlo desde Ajustes.

---

## 7. Usando Ardilla AI

Ardilla AI viene con **botones rápidos (chips)** para hacer las consultas más útiles con un solo clic. Te ahorran tener que escribir.

| Botón rápido | ¿Para qué sirve? |
|---|---|
| **🔍 ¿Qué DLCs me faltan?** | Compara los packs instalados en tu juego con el catálogo de GitHub y te dice exactamente qué te falta. |
| **⚠️ Analizar errores (lastException)** | Revisa `Documentos\Electronic Arts\The Sims 4\lastException.txt` y `lastCrash.txt`. Te explica si el error puede deberse a un mod desactualizado, script roto o archivo corrupto. |
| **💡 Recomendar packs** | Pregunta por temática (magia, sobrenatural, familia, construcción, naturaleza, etc.) y te da recomendaciones con explicación. |
| **💾 Espacio en disco** | Comprueba el espacio libre en la unidad donde está el Sims 4 y te avisa si te falta espacio para nuevas descargas. |
| **🛡️ Verificar archivos** | Comprueba la integridad del juego (ejecutables `TS4_x64.exe`, carpeta `Data\Client`, etc.). |
| **⚙️ Ajustes IA** | Abre directamente la configuración del asistente. |

### Seleccionar packs para instalar

Cuando preguntes algo como *"Quiero descargar Vampiros y Reino de la Magia"*, Ardilla AI puede **preparar esos packs automáticamente** y preguntarte si deseas abrir la **Descarga Selectiva** para revisarlos antes de instalarlos.

Esto es muy útil: tú los ves listados, decides cuáles marcar y luego los descarga e instala la aplicación.

### Limpiar conversación

Pulsa **"🧹 Limpiar"** si el asistente empieza a confundirse después de muchas preguntas. Esto borra el historial de esa conversación y empieza desde cero.

---

## 8. Descargar e instalar DLCs

Ardilla Updater IA se divide entre **actualizaciones generales** y **descarga selectiva**.

1. **Actualizar** (botón principal): Busca actualizaciones del propio programa o contenido marcado por defecto.
2. **Descarga selectiva**: Ideal para instalar solo los packs que quieras. Puedes abrirla manualmente desde la ventana principal, o **directamente desde la sugerencia de Ardilla AI** cuando te proponga packs para instalar.

Cuando el asistente marca packs para ti, aparecerá un mensaje: *"¿Deseas abrir la descarga selectiva ahora para revisarlos e instalarlos?"* → Pulsa **Sí** para revisarlos antes de descargar.

---

## 9. Diagnóstico de errores (lastException.txt)

Una de las funciones más útiles para principiantes.

### ¿Dónde están estos archivos?

Los archivos de logs del Sims 4 se encuentran en:

```text
Documentos\Electronic Arts\The Sims 4\
```

- `lastException.txt` → Aparece cuando hay un error de script/mod (lo más común).
- `lastCrash.txt` → Aparece cuando el juego se cierra de forma inesperada.

### ¿Cómo usarlo con Ardilla AI?

Pulsa **"⚠️ Analizar errores (lastException)"** o escribe:

```text
Analiza mis errores del Sims 4
```

Ardilla AI leerá esos archivos (solo lo necesario), te mostrará un fragmento del error y te dará una **explicación clara**: si lo más probable es un mod desactualizado (MC Command Center, UI Cheats, etc.), un conflicto entre mods o un archivo corrupto.

> **Consejo:** Haz esto **justo después** de que te salga un error. Así el `lastException.txt` será el más reciente.

---

## 10. Solución de problemas

Aquí tienes las soluciones a los problemas más comunes que pueden aparecerle a un usuario principiante.

| Problema | Causa | Solución |
|---|---|---|
| **"No se pudo conectar al servidor de IA en 'http://localhost:11434'"** | Ollama no está corriendo. | Abre Ollama (comprueba el icono en la bandeja del sistema). Si no aparece, ejecútalo desde el Menú Inicio o reinicia el PC. Prueba [http://localhost:11434](http://localhost:11434) en el navegador. |
| **"Modelo no encontrado" o error 404 con el modelo** | El modelo seleccionado no está descargado en Ollama. | Ejecuta `ollama pull <nombre_del_modelo>` en terminal con el nombre exacto que aparece en Ajustes. Luego compruébalo con `ollama list`. |
| **El asistente responde muy lento / se queda pensando** | Modelo demasiado pesado para tu PC (RAM/CPU limitada). | Cambia a un modelo más ligero en **Ajustes IA** → `qwen3.5:0.8b` o `gemma3:4b`. También puedes probar a limpiar la conversación. |
| **Responde sin usar herramientas (adivina en lugar de comprobar)** | Modelo muy pequeño o con limitaciones. | Usa `gemma3:4b` en lugar de `0.8b`. Es el mejor equilibrio entre velocidad y capacidad para usar herramientas correctamente. |
| **No detecta la ruta del juego** | No está en las rutas predeterminadas (instalación personalizada). | Pulsa **"Detectar"** en la ventana principal. Si sigue sin encontrarla, escríbela manualmente (ej.: `D:\Juegos\The Sims 4`). |
| **"No se pudo cargar el catálogo de GitHub"** | Sin conexión a Internet o GitHub no accesible temporalmente. | Comprueba tu conexión a Internet. Inténtalo de nuevo pasados unos segundos. El asistente seguirá funcionando para preguntas generales, pero las consultas de DLCs necesitan catálogo. |
| **El juego va con lag después de analizar muchos mods** | Es normal, no es culpa de Ardilla AI. | Usa el análisis de `lastException.txt` para detectar mods problemáticos y actualízalos o elimínalos uno a uno. |
| **Errores aleatorios con modelos grandes** | Contexto muy grande. | El programa baja automáticamente `num_ctx` a 4096 si hay errores del runner. Para evitarlo, usa modelos ligeros (`qwen3.5:0.8b`). |
| **Ollama no se inicia al arrancar Windows** | Configuración del sistema. | Por defecto Ollama se instala para que arranque en segundo plano. Si no lo hace, ábrelo manualmente una vez o actívalo desde Inicio. |

---

## 11. Preguntas Frecuentes (FAQ)

| Pregunta | Respuesta |
|---|---|
| **¿Necesito internet para usar Ardilla AI?** | Solo para descargar Ollama, el modelo, el catálogo de DLCs o los propios DLCs. Las conversaciones con IA **sí funcionan sin internet** una vez descargado el modelo (todo es local). |
| **¿Qué modelo es mejor para mi PC?** | Con **8 GB RAM**: `qwen3.5:0.8b`. Con **16 GB RAM**: `gemma3:4b`. Con **32 GB+**: puedes probar modelos más grandes, aunque no es necesario. |
| **¿Puedo cambiar de modelo cuando quiera?** | Sí. Ve a **Ajustes → Modelo** y selecciona otro. No necesitas reiniciar la aplicación. |
| **¿Se envía información a terceros?** | No. Todo queda en tu equipo: Ollama local (`localhost:11434`) + descargas desde GitHub. Ardilla AI no envía tus partidas, saves o datos personales fuera de tu PC. |
| **¿Qué significan EP, GP, SP, FP?** | Son los tipos de packs de The Sims 4: **EP** = Expansiones, **GP** = Game Packs, **SP** = Stuff Packs, **FP** = Free Packs. |
| **¿El asistente recuerda lo que hablamos?** | Sí, durante la misma conversación (hasta el límite de historial configurado). Pulsa **Limpiar** para empezar de cero. |
| **¿Puedo añadir un modelo nuevo a Ollama y usarlo?** | Sí. Descárgalo con `ollama pull nombre:modelo`. Aparecerá en el desplegable de Ajustes (la app lee `Models.txt` pero también detecta modelos disponibles). |
| **¿Cuándo usar "Limpiar chat"?** | Cuando el asistente da respuestas confusas, repite cosas o se sale del tema tras muchas preguntas seguidas. Es lo más efectivo para resetear el contexto. |

---

## 12. Glosario

| Término | Explicación |
|---|---|
| **Ollama** | Aplicación que permite ejecutar modelos de IA (LLMs) de forma local en tu PC, sin necesidad de servicios en la nube. |
| **Modelo (LLM)** | El "cerebro" de IA que responde a tus preguntas (ej.: `qwen3.5:0.8b`, `gemma3:4b`). |
| **Endpoint** | Dirección URL donde se conecta la app con Ollama. Siempre `http://localhost:11434` para uso local. |
| **localhost** | Significa "tu propio ordenador". No sale a internet. |
| **Catálogo** | Lista de DLCs disponibles que carga Ardilla Updater desde GitHub para saber qué packs existen. |
| **lastException.txt** | Archivo de registro de errores de scripts/mods del Sims 4. Muy útil para diagnosticar problemas. |
| **lastCrash.txt** | Registro cuando el juego se cierra inesperadamente (crash). |
| **DLC / Pack** | Contenido descargable extra para The Sims 4 (expansiones, game packs, stuff packs...). |
| **Descarga selectiva** | Función para elegir exactamente qué packs descargar/instalar (no todo de golpe). |
| **Herramientas (Tools)** | Acciones que Ardilla AI puede ejecutar (comprobar DLCs, espacio, errores...) para dar respuestas precisas en lugar de adivinar. |

---

## 13. Checklist de Arranque Rápido

Usa esta lista para asegurarte de que todo está listo:

- [ ] **Ollama instalado** y corriendo (icono visible en bandeja del sistema).
- [ ] **Modelo descargado** (`ollama list` lo confirma). Recomendado: `qwen3.5:0.8b`.
- [ ] **Ardilla Updater IA abierto**.
- [ ] **Ruta de The Sims 4 detectada/correcta** en la ventana principal.
- [ ] **Ardilla AI abierto** y conectado (muestra modelo + endpoint arriba).
- [ ] **Primera prueba exitosa**: *"¿Qué DLCs me faltan?"* responde sin error de conexión.
- [ ] **Conexión a Internet activa** (para descargar catálogo/DLCs cuando los instales).

**¡Todo listo!** Ya puedes empezar a usar Ardilla AI con total tranquilidad.