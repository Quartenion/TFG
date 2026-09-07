# INF-HARTO

**Juego rítmico 3D en primera persona** desarrollado en Unity 6 como Trabajo de Fin de Grado.

El jugador avanza automáticamente por el nivel al compás de la música y **mantiene su corazón con vida haciendo clic al ritmo**. No hay combate ni disparos: cada acierto rítmico devuelve sangre al corazón (recupera vida), mientras que moverse, fallar y el simple paso del tiempo la consumen. El recorrido combina tramos de *parkour* a pie con **secciones de kayak sobre el agua**, y todo —obstáculos, cámara, HUD— late sincronizado con el beat.

> La vida se representa como sangre en una jeringuilla con una estética clínica/médica (monitor de electrocardiograma y jeringuillas de sangre). El documento de diseño completo (narrativa, roadmap, sistemas planificados) está en [`GAME_DESIGN_MASTER.md`](GAME_DESIGN_MASTER.md).

---

## Características principales

- 🎵 **Motor rítmico sincronizado por DSP** con ventanas de precisión (Perfect / Good / Miss) y compensación de latencia de audio.
- 🏃 **Auto-runner de parkour**: doble salto, dash, slide, slam, wallrun/walljump y tirolina; cada acción cuesta energía y se premia al ritmo.
- 🩸 **Sistema de vida "jeringuilla"**: un modelo de sangre roja/azul con riesgo-recompensa, en vez de una barra de vida convencional.
- 🛶 **Secciones de kayak**: física de flotación, remo al ritmo, combo de remo y saltar dentro/fuera para esquivar obstáculos.
- 🧱 **Obstáculos rítmicos**: con caras letales configurables, caída desde el techo, caída por escaleras y generación alineada al beat.
- 🖥️ **HUD clínico**: monitor ECG, jeringuillas 3D renderizadas al canvas, mira con feedback de precisión y notas rítmicas convergentes.
- 🛠️ **Herramientas de diseño de niveles** en el editor para colocar todo sobre la rejilla de beats.

---

## Mecánicas de juego

### 1. Sistema rítmico (el núcleo)

El **`BeatManager`** es el reloj maestro del juego. Arranca la música con precisión de DSP (`PlayScheduled`) para que el ritmo no dependa de los FPS, y expone:

- **BPM configurable** y duración de beat/medio-beat.
- **Ventanas de timing**: `Perfect` (±70 ms) y `Good` (±140 ms); fuera de ahí es `Miss`.
- **Compensación de latencia** de audio, calibrable con `LatencyCalibrator` y guardada en `PlayerPrefs`.
- **Eventos** `OnBeat` / `OnHalfBeat` a los que se enganchan cámara, pasos, obstáculos y UI.
- **Modo test** para arrancar el nivel y la canción en cualquier posición/beat (útil para depurar).

El **`RhythmInput`** evalúa cada clic contra el beat y solo cuenta **un acierto válido por beat** (anti-spam), disparando el feedback visual y los cambios de vida.

### 2. Vida: el modelo de la jeringuilla (`EnergyManager`)

En lugar de una barra de vida clásica, hay **una jeringuilla que acumula sangre**:

- **Energía / vida = capacidad − sangre en la jeringuilla.** Cuanta menos sangre acumulada, más vida.
- **Pasivamente** la jeringuilla se va llenando (pierdes vida con el tiempo).
- **Las acciones** (saltar, dash, slide…) añaden sangre: cuestan energía.
- **Acertar el clic al ritmo** saca sangre de la jeringuilla (devuelve vida): `Perfect` = 1.5×, `Good` = 1×.
- **Fallar** añade sangre extra, con un **multiplicador anti-spam** (machacar el clic penaliza cada vez más).
- Si **la jeringuilla se llena del todo → game over**.
- **Modo azul (bonus)**: al pasarte de aciertos entras en un modo de riesgo-recompensa donde acumulas "sangre azul"; al fallar se inyecta toda de golpe y da un *boost*.

### 3. Movimiento a pie (`PlayerMovement`)

Auto-runner de *parkour* inspirado en juegos como Ghostrunner/Titanfall. El jugador **avanza solo** al ritmo y controla la dirección y las acciones:

- **Doble salto**, **dash**, **slide**, **slam** (caída rápida en el aire).
- **Wallrun** y **walljump** con detección de pared por raycast.
- **Tirolina** (zipline) para tramos guiados.
- Manejo específico de **escaleras** y corrección de posición.
- **Head-bob de cámara al ritmo** y efectos de cámara (dash, shake).
- Cada acción se paga con energía, atándose así al sistema de la jeringuilla.

### 4. Secciones de kayak (`KayakController`)

Tramos sobre el río con física propia:

- **Flotación** tipo muelle-amortiguador (spring-damper) sobre el agua.
- **Remo al ritmo**: clic izquierdo/derecho para remar a cada lado; el kayak gira en consecuencia.
- **Combo de remo**: remar repetidamente hacia el mismo lado incrementa progresivamente la velocidad lateral hacia esa dirección.
- **Saltar dentro/fuera del kayak**: puedes saltar (incluido doble salto) para esquivar obstáculos por debajo de los cuales pasa el kayak; el kayak es una **plataforma sólida** a la que vuelves de forma suave.
- **Obstáculos con caras letales**: embestir de **frente** rompe el kayak (game over si vas dentro); los **laterales** solo bloquean, sin matar.
- **Corrientes de agua** (`WaterCurrentZone`) y oleaje del líquido.

### 5. Obstáculos rítmicos

- **`ObstacleInfo`** — componente de datos que define **qué caras son letales** (Front, Bottom, Top, Left, Right, Back), con *gizmos* que las pintan en el editor.
- **`FallingObstacle`** — cae desde el techo en caída libre y **aterriza justo cuando el jugador llega**, calculado por DSP.
- **`StairFallingObstacle`** — **baja unas escaleras a saltos, un escalón por beat**, aterrizando cada escalón sobre el beat. Aparece unos escalones más arriba y termina en la posición donde lo colocas.
- **`BeatObstacleSpawner`** — genera obstáculos en posiciones **alineadas a la rejilla de beats** (`cellSize = walkSpeed × duración de beat`), con patrones cíclicos.
- **Habilidad de bloqueo (`BlockAbility`)** — al pulsar **Q** se abre una ventana que **rompe los bloques rompibles** cercanos justo antes de tocarlos.

### 6. HUD y *game feel*

- **Monitor cardíaco (ECG)**: una línea de electrocardiograma que pulsa con cada acierto (verde = Perfect, blanco = Good, rojo = Miss).
- **Jeringuillas 3D** de sangre roja/azul renderizadas al canvas como indicador de vida.
- **Mira (crosshair)** con "pinchos" que reaccionan al clic.
- **Notas rítmicas**: anillos con pinchos que convergen hacia el centro sincronizados con la mira y se colorean de verde al acertar.
- **Feedback de timing** (PERFECT / GOOD / MISS) y **efectos de vida baja** (viñeta roja, latido en pantalla).

---

## Controles

| Acción | Control |
|--------|---------|
| Avanzar | Automático (al ritmo) |
| Moverse / carril | `W` `A` `S` `D` |
| Mirar | Ratón |
| Saltar / Doble salto | `Espacio` |
| Dash | `E` |
| Deslizarse (slide) | `Shift` izquierdo |
| Slam (en el aire) | `S` |
| Wallrun | Automático (saltar junto a una pared) |
| Walljump | `Espacio` (durante el wallrun) |
| Tirolina | `F` |
| Bloquear / romper bloques | `Q` |
| **Golpe rítmico** (rellenar vida) | Clic izquierdo |
| Kayak: remar izquierda / derecha | Clic izquierdo / Clic derecho |

> A pie, el clic izquierdo es el golpe rítmico que rellena la vida; dentro del kayak, los clics reman a cada lado.

---

## Tecnologías

- **Motor**: Unity 6 (`6000.4.4f1`)
- **Render**: Universal Render Pipeline (URP)
- **Input**: New Input System
- **Geometría de niveles**: ProBuilder
- **Lenguaje**: C#
- **Control de versiones**: Git + Git LFS (para *assets* y binarios)

---

## Estructura del proyecto

```
TFG/
├── Assets/
│   ├── Scripts/
│   │   ├── BeatManager.cs           ← reloj rítmico maestro (DSP, ventanas, latencia)
│   │   ├── RhythmInput.cs           ← evalúa los clics contra el beat
│   │   ├── EnergyManager.cs         ← sistema de vida "jeringuilla" (rojo/azul)
│   │   ├── PlayerMovement.cs        ← auto-runner + parkour (salto, dash, slide, slam, wallrun…)
│   │   ├── KayakController.cs       ← secciones de kayak (flotación, remo, combo, saltos)
│   │   ├── ObstacleInfo.cs          ← caras letales de los obstáculos (+ gizmos)
│   │   ├── ObstacleCollision.cs     ← detección de colisión a pie
│   │   ├── FallingObstacle.cs       ← caída desde el techo sincronizada
│   │   ├── StairFallingObstacle.cs  ← caída por escaleras al ritmo
│   │   ├── BeatObstacleSpawner.cs   ← generación alineada al beat
│   │   ├── BlockAbility.cs / BreakableBlock.cs
│   │   ├── Zipline.cs, WaterCurrentZone.cs, LiquidLevel.cs, LiquidWobble.cs
│   │   ├── LatencyCalibrator.cs, StartSequenceManager.cs, GameOverManager.cs …
│   │   ├── HUD/                     ← ECG, jeringuillas, notas rítmicas, feedback
│   │   ├── Editor/                  ← inspectores y herramientas personalizadas
│   │   └── LevelDesign/             ← utilidades de diseño de niveles
│   ├── Prefabs/  ├── Scenes/  ├── Audio/  └── …
├── GAME_DESIGN_MASTER.md            ← documento maestro de diseño (visión completa)
└── README.md
```

---

## Cómo abrir y ejecutar el proyecto

1. Instala **Unity 6 (`6000.4.4f1`)** desde Unity Hub (con el módulo de URP) y asegúrate de tener **Git LFS** (`git lfs install`).
2. Clona el repositorio (Git LFS descargará los *assets* grandes automáticamente):
   ```bash
   git clone <url-del-repositorio>
   ```
3. Abre la carpeta del proyecto desde **Unity Hub**.
4. Abre la escena del nivel (por ejemplo `Assets/Scenes/…`) o el menú principal.
5. Pulsa **Play**.

> Consejo: en la primera ejecución conviene calibrar la latencia de audio para que el timing case en tu equipo.

---

## Estado del proyecto

Proyecto de TFG **en desarrollo**. Este README recoge las mecánicas **ya implementadas**. La visión completa (sistema de puntuación "la balanza", tienda y economía, contratos, enemigos rítmicos, varios niveles temáticos y la narrativa) está descrita como diseño y trabajo futuro en [`GAME_DESIGN_MASTER.md`](GAME_DESIGN_MASTER.md).

---

## Créditos y licencia

- **Autor**: _[tu nombre]_ — Trabajo de Fin de Grado, _[Universidad / Grado]_.
- **Assets de terceros**: *Simple Water Shader* (IgniteCoders).
- **Licencia**: _[por definir]_.

> Rellena el autor, la universidad y la licencia según corresponda antes de publicar.
