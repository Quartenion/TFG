# INF-HARTO — Documento Maestro de Diseño y Arquitectura
### TFG: Juego Rítmico 3D en Primera Persona
*Última actualización: Septiembre 2026*

---

## ÍNDICE

1. [Estado de Implementación — Sistemas Construidos](#1-estado-de-implementación--sistemas-construidos)
2. [Roadmap y Milestones para TFG](#2-roadmap-y-milestones-para-tfg)
3. [Arquitectura General del Proyecto](#3-arquitectura-general-del-proyecto)
4. [BeatManager — Rediseño Completo](#4-beatmanager--rediseño-completo)
5. [Sistema de Movimiento del Jugador](#5-sistema-de-movimiento-del-jugador)
6. [Sistema de Energía / Jeringuilla](#6-sistema-de-energía--jeringuilla)
7. [Sistema de Puntuación — La Balanza](#7-sistema-de-puntuación--la-balanza)
8. [Sistema de Tienda y Economía](#8-sistema-de-tienda-y-economía)
9. [Contratos Demoníacos](#9-contratos-demoníacos)
10. [Sistema de Muerte y Respawn](#10-sistema-de-muerte-y-respawn)
11. [Diseño de Niveles](#11-diseño-de-niveles)
12. [Juice, Game Feel y Polish](#12-juice-game-feel-y-polish)
13. [Diseño Narrativo](#13-diseño-narrativo)
14. [Investigación Académica — Papers y Referencias](#14-investigación-académica--papers-y-referencias)
15. [Riesgos Técnicos Identificados](#15-riesgos-técnicos-identificados)
16. [Código C# de Referencia](#16-código-c-de-referencia)
17. [Registro de limpieza y cambios — auditoría sep. 2026](#17-registro-de-limpieza-y-cambios--auditoría-sep-2026)

---

## 1. ESTADO DE IMPLEMENTACIÓN — SISTEMAS CONSTRUIDOS

Esta sección resume lo que **ya está construido y funcionando** en el proyecto (estado tras la auditoría y limpieza de septiembre de 2026, ver [sección 17](#17-registro-de-limpieza-y-cambios--auditoría-sep-2026)). El resto del documento describe el diseño completo (incluidos sistemas planificados y trabajo futuro).

**Niveles jugables:** MainMenu + **Level1** (mina), **Level2** (prisión / río con kayaks) y **Level3** (ascenso + **pelea contra el jefe** en 3 fases). Cada nivel tiene una zona de *freeroam* inicial (lobby) con tienda y puesto de contratos.

### Motor rítmico
- **BeatManager** — reloj maestro por DSP (`PlayScheduled`, `ScheduledStartDSP`), BPM configurable, ventanas **Perfect / Good / Miss**, compensación de latencia (lee el valor guardado en `PlayerPrefs` si existe), eventos `OnBeat` / `OnHalfBeat` e *Intervals* (eventos cada N pasos, usados por `PulseToTheBeat`). Arranque desde un segundo concreto de la canción (`StartMusicFromAt`) para checkpoints y modo test; `SetRewindClock` para el Rebobinado; soporte de medio beat con la mejora «Doble tempo» (`ClickStep`, `NearestClickSlot`).
- **RhythmInput** — evalúa cada clic (`EvaluateClickTiming`). **Anti-spam por beat**: solo el primer clic de cada beat (o medio beat) puede acertar; cada clic de más es un fallo que cuesta el doble que el anterior (×1, ×2, ×4… hasta ×8) y, si varios beats seguidos tienen clics de más, sube un «calor» (hasta 3) que multiplica todos los fallos. Un jugador que no spamea nunca paga más que un fallo normal.
- **MetronomeClicks** — tics generados por código y programados en el reloj DSP (sin latencia). Los usan el 3-2-1 y la mejora «Metrónomo».
- **TimingResult** — enum Perfect/Good/Miss compartido.

### Vida / energía — la jeringuilla (EnergyManager)
**Energía = capacidad − sangre acumulada.** Llenado pasivo, coste por acción, aciertos que devuelven sangre (Perfect 1.5× / Good 1×, con modificadores de la tienda), fallos que suman sangre (escalados por el anti-spam de `RhythmInput`), **modo azul** de riesgo-recompensa, drenaje por BloodSuckers, `TakeHitFraction` (golpes del jefe = fracción de la sangre total), gasto «a crédito» si quedan vidas extra, pausa durante el Rebobinado y el fin de nivel, y **God Mode** para diseño. Los niveles empiezan con la jeringuilla vacía (salvo «Buen desayuno»). HUD: `EnergyBarUI`, `LiquidLevel`, `SyringeSpinner`, `LowHealthEffects`.

### Movimiento a pie (PlayerMovement)
Auto-runner de *parkour*: **doble salto, dash, slide, slam, wallrun, walljump, tirolina** y escaleras, con *beat gating*, coste de energía por acción (sin cooldowns) y corrección de velocidad para ir sincronizado con la rejilla de beats. Efectos: `CameraDashEffect` (FOV, punch del bloqueo), `CameraShaker`, `ViewmodelPhysics`, `HandAnimationController`, `FootstepAudio`.
- **BlockAbility** (Q) + **BreakableBlock** — bloqueo que rompe bloques (se desmoronan con `CrumbleFX` y el Rebobinado los recompone).

### Kayak (KayakController)
Flotación *spring-damper*, remo al ritmo (clic izq./der.), combo de remo, saltar dentro/fuera, embestida frontal letal y roce lateral seguro, corrientes (`WaterCurrentZone`), activación por volumen o horizontal, reinicio con el Rebobinado. Caer al agua fuera del kayak = game over inmediato (`WaterDiveController`, detección por raycast contra el agua sólida).

### Obstáculos
`ObstacleInfo` (caras letales), `ObstacleCollision`, `FallingObstacle`, `StairFallingObstacle`, `FireballObstacle`, `WaterSpike`, `BeatOscillator`, `PivotOscillator`, `FloatingObject`, `CrushBottom` (cara inferior letal de bloques móviles), `BloodSucker` + `GazeSwitch` (mirar para activar). Todos implementan **`IRewindable`** para el Rebobinado.

### Jefe (Level3)
`BossController` (+ `BossControllerPhase3`), `BossTridentSweep` (tridente), `BossFistSlam`, `BossProjectile` (barriles), `BossHomingFireball`, `BossPillar`, `BossShockwave`, `BossPlatformSpawner`, `BossMusicController`, `BossLifeOrb`. Los golpes **rompen el escudo azul** (se pierde toda la sangre azul, suena cristal y no se puede rellenar en 3 s) o, sin azul, llenan **media jeringuilla roja** (1 s de invulnerabilidad); **una vida extra por fase** (barril de vida → bola de fuego de vida → orbe de vida por mirada). Las mejoras de la tienda funcionan en el jefe salvo el Rebobinado.

### Tienda, economía y progreso
- **PlayerProgress** — guardado JSON (monedas, mejoras, consumibles, muescas, contratos, iris, cosméticos). `LevelCoins` (monedas de la partida; se conservan en checkpoint y al morir), `RunStats`, `CheckpointSystem`.
- **Monedas** (`Coin`) — suben del suelo 4 beats antes, giran 45° por beat, imán (mejora), sonido con escala ascendente en combos; el Rebobinado las devuelve. **Iris** (`GazeCoin`) — moneda de mirada para cosméticos.
- **Tienda** (`ShopUI`, un solo archivo construido por código con `UIKit`; diseño simple sin colores ni bordes redondeados; tamaños en constantes al principio) — solo dos pestañas encima de la rejilla, **Mejoras** (consumibles + mejoras) y **Cosméticos** (sin categorías), **rejilla de casillas cuadradas a la izquierda** y **ficha a la derecha** (descripción, equipar y botón de compra). 30 objetos: 3 consumibles (siempre disponibles) + 27 mejoras que se **desbloquean por nivel, 9 en cada uno** (`ShopUnlocks`); la tienda solo muestra lo que ya se puede comprar. Básicas en el nivel 1, intermedios en el 2 y avanzados (golpes del jefe, BloodSuckers, Auto-latido, Doble tempo…) en el 3. El nivel alcanzado se guarda (`PlayerProgress.LevelReached`).
- **Muescas** (`Notches`, estilo Hollow Knight) — las mejoras solo funcionan **equipadas**; 3 muescas base, cada mejora cuesta 1–3, se compran hasta 5 más (**máximo 8**, precios 250/450/750/1200/1800).
- **Cosméticos** (`Cosmetics`, `CosmeticApplier`) — corazón de la mira, mano, monedas y kayak; se pagan con iris.
- Sistemas auto-creados: `TimeRewinder`, `Metronome`, `AutoBeater`, `BarFlash`, `ContractRunner`, `CosmeticApplier`, `CursorRestorer`.

### Contratos (Contracts/)
`ContractCatalog` (18 contratos en 3 dificultades, **cada uno con una versión por nivel** (54 en total) cuyo objetivo y pago suben de un nivel al siguiente, p. ej. «Buscador de oro»: 25/50/80 monedas por 50/90/140; solo cuentan jugando su nivel, también en el nivel 3 con el jefe; reglas como «Contrarreloj» = 100/90/80 pulsaciones, «Al compás» o «A pelo»), `ContractRunner` (reglas y progreso en el nivel; los de mitad de nivel se marcan cumplidos al momento), `ContractUI` (mismo estilo simple que la tienda vía `SimpleUI`: pestañas Contratos / Cobrados, rejilla de casillas cuadradas y ficha a la derecha con la hoja de firma) + `SignaturePad` (firmar con el ratón), `ContractStand` (puesto: firmar y **cobrar** los cumplidos).

### Flujo de juego
- **StartSequenceManager** — intro (una vez por nivel y arranque del juego), espera a que el jugador llegue al punto de salida, y **3-2-1-¡YA!** con metrónomo generado: tic agudo = puedes empezar (suena en cada beat mientras no has empezado), grave = vas en racha; cada acierto tiene que caer en el beat siguiente exacto; al fallar, el siguiente beat vuelve a ser agudo; solo late el círculo que toca pulsar. Modo test y reaparición en checkpoint.
- `MainMenuManager`, `StartWall` (muro que se desmorona), `LobbyMusic` (música del lobby con pasos al ritmo), `PauseMenuManager`, `GameOverManager` (música de game over, reinicio asíncrono, respawn en checkpoint), `LevelEndManager` (resultados + contratos «por cobrar»), `DoorInteraction`.

### Tutorial y modo prueba (estudio con jugadores)
- **Tutorial:** `TutorialHint` = **carteles en el mundo** junto a cada obstáculo con la tecla/acción (p. ej. «ESPACIO · saltar») que miran a la cámara y **laten al ritmo** de la música (menú *Tools → Tutorial → Crear avisos de ejemplo*). `TutorialHUD`: **F1** = panel de controles, F2 = activar/desactivar consejos.
- **Modo prueba** (`Study/`): botón «Participar en la prueba» en el menú → consentimiento → código anónimo → 5 partes: práctica con tutorial; Nivel 1 con configuración **Permisiva** y **Exigente** (orden AB/BA sorteado; `StudyConfig` cambia el gasto de energía de las acciones y el castigo por fallo); Nivel 2 y Nivel 3 opcionales, **con tienda y contratos** (se gastan las monedas ganadas; se registran compras, equipamiento, tiempo en la tienda, contratos firmados/cumplidos/cobrados y monedas recogidas frente a las disponibles). Hay un cuestionario corto (1–7) tras cada parte y un Google Forms final.
- **Telemetría** (`StudyTelemetry`): una fila por intento con precisión, Perfect/Good/fallos, desfase del clic (ms), energía gastada/perdida/recuperada, tiempo en azul, acciones, resultado. Se guarda en CSV local y se envía a un Google Form oculto. Instrucciones en `ESTUDIO_FORMULARIOS.md`.

### HUD
`RhythmHUDController` + `RhythmRingVisual` (notas que convergen con la mira, también de medio beat), `CrosshairController`, `RhythmHeartMonitor`, `TimingFeedbackUI`, `HeartBeat`, `GazeChargeUI`, `CoinHUD`, `ReviveFX`, `PlayerShield`.

### Herramientas de editor / diseño de niveles
- `BeatGridVisualizer` (rejilla de beats), `BeatSnapHelper`, `PulseToTheBeat`.
- `PlayerPathRecorder` / `RecordedPath` / `PlayerPathVisualizer` (grabar la ruta del jugador), `BeatCoinPlacer` (colocar monedas cada N beats sobre la ruta), `CoinSnapEditor` (ajustar monedas a beat / medio beat / X impar).
- **`LevelHierarchyOrganizer`** (Tools → Nivel) — ordena el Timeline de cada nivel en «Segmento NNN · beats a-b» (4 beats) con subgrupos Escenario / Obstáculos / Interactivos / Monedas, conservando posiciones.
- Menús: tienda (catálogo, dar monedas, borrar guardado), contratos, iris, lobby; inspectores personalizados en `Assets/Scripts/Editor/`.

---

## 2. ROADMAP Y MILESTONES PARA TFG

> Actualizado sep. 2026: marcado lo que ya está en el juego. Pendiente sobre todo: puntuación con la balanza, música propia y la parte académica (Fase 5).

### Fase 0 — Fundamentos (2-3 semanas)
**Objetivo:** que el juego no tenga bugs bloqueantes y el core loop sea jugable.

- [x] Arreglar Game Over Screen (bugs actuales)
- [x] Arreglar Pause Menu
- [x] Implementar auto-run (el jugador siempre avanza solo)
- [x] Rediseñar BeatManager con ventanas Perfect/Good/Miss
- [x] Integrar feedback visual de timing (Perfect = flash dorado, Miss = flash rojo)
- [x] Slide básico con Shift

### Fase 1 — Movimiento Completo (3-4 semanas)
**Objetivo:** que el movimiento se sienta como Ghostrunner/Titanfall.

- [x] Wallrun (detectar pared, aplicar movimiento horizontal + vertical)
- [x] Walljump (salto desde pared con impulso)
- [x] ~~Rehacer Grapple~~ — **descartado** (el gancho se eliminó del juego)
- [x] Ajustar costes de energía para cada acción
- [x] Ajuste de feel: camera tilt en wallrun, FOV en dash, screen shake leve

### Fase 2 — Nivel 1 Completo (4-5 semanas)
**Objetivo:** un nivel jugable de principio a fin, pulido, con música sincronizada.

- [x] Diseñar y construir Nivel 1 completo en Unity
- [ ] Componer/conseguir música para Nivel 1 (rabia, duelo, ira)
- [x] Sincronizar eventos de música con el BeatManager
- [ ] Sistema de puntuación básico al final del nivel
- [x] Sistema de muerte + respawn con efecto de desfibrilador

### Fase 3 — Sistemas de Juego (3-4 semanas)
**Objetivo:** economía, tienda y contratos funcionando.

- [x] Sistema de monedas (spawn en nivel + contador)
- [x] Tienda con 4-6 upgrades funcionales
- [x] Al menos 1 contrato demoníaco
- [ ] 1 tipo de enemigo básico con comportamiento rítmico

### Fase 4 — Niveles 2 y 3 + Polish (3-4 semanas)
**Objetivo:** variedad de contenido y pulido general.

- [x] Nivel 2 (prisión, sufrimiento) — puede reusar assets con paleta diferente
- [x] Nivel 3 (pasado, traición) — cambio de atmósfera
- [ ] Sistema de puntuación con la balanza visual
- [ ] Efectos de partículas, screen effects, audio polish

### Fase 5 — TFG Académico (3-4 semanas paralelas)
**Objetivo:** la memoria del TFG y la investigación.

- [ ] Escribir capítulo de investigación sobre beat tracking
- [ ] Escribir capítulo sobre flow state y diseño rítmico
- [ ] Análisis de métricas: recoger datos de timing, errores, flujo
- [ ] Comparar distintas configuraciones de ventana de timing
- [ ] Conclusiones sobre cómo la energía afecta la experiencia

**Total estimado: 18-20 semanas (~5 meses)**

---

## 3. ARQUITECTURA GENERAL DEL PROYECTO

### Estructura de carpetas recomendada en Assets/

```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── GameManager.cs          ← singleton principal, estado global
│   │   ├── LevelManager.cs         ← carga/descarga de niveles
│   │   ├── SceneTransition.cs      ← transiciones con fade
│   │   └── GameState.cs            ← enum de estados del juego
│   ├── Rhythm/
│   │   ├── BeatManager.cs          ← REDISEÑADO (ver sección 4)
│   │   ├── RhythmInput.cs          ← ya hecho, pequeños ajustes
│   │   ├── TimingEvaluator.cs      ← Perfect / Good / Miss / Early / Late
│   │   └── BeatEventChannel.cs     ← ScriptableObject canal de eventos
│   ├── Player/
│   │   ├── PlayerController.cs     ← orquestador principal
│   │   ├── PlayerMovement.cs       ← ya hecho, refactorizar
│   │   ├── PlayerSlide.cs          ← nuevo
│   │   ├── PlayerWallrun.cs        ← nuevo
│   │   └── PlayerStateManager.cs   ← state machine del jugador
│   ├── Energy/
│   │   ├── EnergyManager.cs        ← ya hecho
│   │   └── EnergyBarUI.cs          ← ya hecho
│   ├── Scoring/
│   │   ├── ScoreManager.cs         ← acumula puntos por run
│   │   ├── ComboTracker.cs         ← multiplier de combo
│   │   └── BalanceScreen.cs        ← pantalla de la balanza al final
│   ├── Economy/
│   │   ├── CoinManager.cs
│   │   ├── ShopManager.cs
│   │   ├── UpgradeDefinition.cs    ← ScriptableObject
│   │   └── PlayerInventory.cs
│   ├── Enemies/
│   │   ├── EnemyBase.cs
│   │   ├── RhythmEnemy.cs          ← enemigo que ataca en beat
│   │   └── EnemySpawner.cs
│   ├── Contracts/
│   │   ├── ContractDefinition.cs   ← ScriptableObject
│   │   ├── ContractManager.cs
│   │   └── ContractModifier.cs
│   ├── UI/
│   │   ├── HUD.cs
│   │   ├── PauseMenu.cs
│   │   ├── GameOverScreen.cs
│   │   └── BeatFeedbackUI.cs      ← "PERFECT", "GOOD", "MISS"
│   └── Audio/
│       ├── MusicLayerController.cs ← capas de música adaptativa
│       └── FootstepSync.cs        ← pasos sincronizados al beat
├── ScriptableObjects/
│   ├── Upgrades/
│   ├── Contracts/
│   └── LevelData/
├── Audio/
│   ├── Music/
│   └── SFX/
├── Prefabs/
└── Scenes/
    ├── MainMenu
    ├── Level_01_Wrath
    ├── Level_02_Prison
    ├── Level_03_Past
    ├── Hub_01             ← zona entre niveles 1-2
    └── GameOver
```

### Patrón de comunicación entre sistemas

Usa **ScriptableObject Event Channels** en vez de referencias directas entre scripts. Esto desacopla los sistemas y facilita el testing:

```csharp
// BeatEventChannel.cs — ScriptableObject
[CreateAssetMenu(menuName = "Rhythm/Beat Event Channel")]
public class BeatEventChannel : ScriptableObject
{
    public event System.Action<BeatData> OnBeat;
    public void RaiseBeat(BeatData data) => OnBeat?.Invoke(data);
}

public struct BeatData
{
    public float songTime;
    public int beatIndex;
    public float bpm;
}
```

El `BeatManager` llama `channel.RaiseBeat(data)` y cualquier sistema (FootstepSync, EnemyRhythm, MusicLayers, UI) se subscribe sin conocer al BeatManager directamente.

### GameState — State Machine Global

```csharp
public enum GameState
{
    MainMenu,
    LevelRunning,      // jugador corriendo, beat activo
    LevelComplete,     // mostrando balanza
    HubExploring,      // free roam entre niveles
    Shopping,          // en tienda
    Paused,
    GameOver,
    CutScene
}
```

El `GameManager` es el único que cambia el estado. El `BeatManager` solo activa el beat cuando el estado es `LevelRunning`.

---

## 4. BEATMANAGER — REDISEÑO COMPLETO

> ✅ **Estado: implementado.** Este rediseño ya está en el juego y va más allá de lo descrito aquí: el `BeatManager` real usa **sincronización por DSP** (`PlayScheduled` / `StartMusicAt`) para precisión sub-frame, permite **arrancar la canción desde un offset** (`StartMusicFromAt`, base del modo test y de los checkpoints) y expone `ScheduledStartDSP`, que usan `FallingObstacle` y `StairFallingObstacle` para caer al ritmo. El código de abajo es la versión de referencia; la implementación final incluye esas extensiones y **ya no tiene** `BeatProgress`, `SetBpm` ni `SetLatencyOffset` (se quitaron por no usarse).

### Problemas del BeatManager de partida

1. **No tiene ventanas de timing diferenciadas** — solo sabe si estás "cerca" del beat, no cuán cerca.
2. **No tiene offset de latencia de audio** — en casi todos los dispositivos el audio llega con 20-80ms de delay. Si no compensas esto, los clicks siempre parecerán incorrectos.
3. **No expone el progreso dentro del beat** — necesitas saber si estás en el primer cuarto del beat, la mitad, etc., para efectos visuales.
4. **No tiene sistema de eventos tipado** — los UnityEvents de los Intervals son difíciles de debuggear.

### Nuevo diseño

```csharp
// ============================================================
// BeatManager.cs — VERSIÓN REDISEÑADA
// ============================================================
using UnityEngine;
using UnityEngine.Events;

public class BeatManager : MonoBehaviour
{
    public static BeatManager Instance { get; private set; }

    [Header("Música")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private float _bpm = 120f;

    [Header("Compensación de latencia")]
    [Tooltip("Offset en segundos para compensar la latencia de audio del dispositivo. " +
             "Valor positivo = el juego considera que el audio va adelantado. " +
             "Empieza con 0 y ajusta según pruebas.")]
    [SerializeField] private float _audioLatencyOffset = 0.0f;

    [Header("Ventanas de timing")]
    [SerializeField] private float _perfectWindow = 0.07f;   // ±70ms
    [SerializeField] private float _goodWindow    = 0.14f;   // ±140ms
    // Fuera de goodWindow = Miss

    [Header("Eventos de beat")]
    public UnityEvent OnBeat;                        // cada beat exacto
    public UnityEvent OnHalfBeat;                    // cada medio beat (subdivisión)

    // Datos públicos
    public float Bpm         => _bpm;
    public float BeatDuration => _bpm > 0f ? 60f / _bpm : 1f;
    public float HalfBeatDuration => BeatDuration * 0.5f;

    /// <summary>Tiempo de la canción corregido por latencia</summary>
    public float CorrectedTime => (_audioSource != null ? _audioSource.time : 0f) - _audioLatencyOffset;

    /// <summary>Progreso dentro del beat actual [0..1]</summary>
    public float BeatProgress
    {
        get
        {
            float t = CorrectedTime;
            return (t % BeatDuration) / BeatDuration;
        }
    }

    /// <summary>Índice del beat actual desde el inicio de la canción</summary>
    public int CurrentBeatIndex => Mathf.FloorToInt(CorrectedTime / BeatDuration);

    private int _lastBeatIndex = -1;
    private int _lastHalfBeatIndex = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (_audioSource == null || !_audioSource.isPlaying) return;

        float t = CorrectedTime;

        // Detectar beat entero
        int beatIdx = Mathf.FloorToInt(t / BeatDuration);
        if (beatIdx != _lastBeatIndex)
        {
            _lastBeatIndex = beatIdx;
            OnBeat?.Invoke();
        }

        // Detectar medio beat
        int halfIdx = Mathf.FloorToInt(t / HalfBeatDuration);
        if (halfIdx != _lastHalfBeatIndex)
        {
            _lastHalfBeatIndex = halfIdx;
            OnHalfBeat?.Invoke();
        }
    }

    /// <summary>
    /// Evalúa si un input ahora mismo cae en ventana Perfect, Good o Miss.
    /// </summary>
    public TimingResult EvaluateTiming()
    {
        float distToNearest = GetSecondsToNearestBeat();

        if (distToNearest <= _perfectWindow) return TimingResult.Perfect;
        if (distToNearest <= _goodWindow)    return TimingResult.Good;
        return TimingResult.Miss;
    }

    /// <summary>Distancia en segundos al beat más cercano (anterior o siguiente).</summary>
    public float GetSecondsToNearestBeat()
    {
        float t = CorrectedTime;
        float mod = t % BeatDuration;
        return Mathf.Min(mod, BeatDuration - mod);
    }

    /// <summary>True si el input cae dentro de la ventana Good o mejor.</summary>
    public bool IsOnBeat() => GetSecondsToNearestBeat() <= _goodWindow;

    public void SetBpm(float newBpm)
    {
        _bpm = newBpm;
        _lastBeatIndex = -1;
        _lastHalfBeatIndex = -1;
    }

    public void SetLatencyOffset(float offsetSeconds)
    {
        _audioLatencyOffset = offsetSeconds;
    }

    /// <summary>Inicia la música sincronizada desde el principio del beat 0.</summary>
    public void StartMusic()
    {
        if (_audioSource == null) return;
        _audioSource.time = 0f;
        _audioSource.Play();
        _lastBeatIndex = -1;
        _lastHalfBeatIndex = -1;
    }

    /// <summary>Para la música y resetea el estado interno.</summary>
    public void StopMusic()
    {
        if (_audioSource != null) _audioSource.Stop();
        _lastBeatIndex = -1;
        _lastHalfBeatIndex = -1;
    }
}

public enum TimingResult
{
    Perfect,   // dentro de _perfectWindow
    Good,      // dentro de _goodWindow
    Miss       // fuera de cualquier ventana
}
```

### Sistema de timing visual (feedback al jugador)

```csharp
// TimingFeedbackUI.cs
public class TimingFeedbackUI : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI _label;
    [SerializeField] private Color _perfectColor = new Color(1f, 0.85f, 0f);  // dorado
    [SerializeField] private Color _goodColor    = Color.white;
    [SerializeField] private Color _missColor    = new Color(1f, 0.2f, 0.2f);
    [SerializeField] private float _displayTime  = 0.6f;
    [SerializeField] private AnimationCurve _scaleCurve;  // punch scale

    private Coroutine _current;

    public void Show(TimingResult result)
    {
        if (_current != null) StopCoroutine(_current);
        _current = StartCoroutine(ShowRoutine(result));
    }

    private IEnumerator ShowRoutine(TimingResult result)
    {
        string text = result switch
        {
            TimingResult.Perfect => "PERFECT",
            TimingResult.Good    => "GOOD",
            TimingResult.Miss    => "MISS",
            _                    => ""
        };
        Color col = result switch
        {
            TimingResult.Perfect => _perfectColor,
            TimingResult.Good    => _goodColor,
            TimingResult.Miss    => _missColor,
            _                    => Color.white
        };

        _label.text = text;
        _label.color = col;

        float t = 0f;
        while (t < _displayTime)
        {
            t += Time.deltaTime;
            float s = _scaleCurve.Evaluate(t / _displayTime);
            _label.transform.localScale = Vector3.one * s;
            _label.color = col.WithAlpha(1f - (t / _displayTime));
            yield return null;
        }
        _label.text = "";
    }
}
```

### Calibración de latencia en tiempo real

> ❌ **Descartado.** Existió una pantalla `LatencyCalibrator`, pero nunca se conectó a ninguna escena y se eliminó en la limpieza de sep. 2026. El `BeatManager` sigue leyendo un offset guardado en `PlayerPrefs` (`AudioLatencyOffset`) si existe, y el offset se puede ajustar en el Inspector. Si en el futuro hace falta calibrar en otros PCs, habría que rehacer la pantalla.

---

## 5. SISTEMA DE MOVIMIENTO DEL JUGADOR

> ✅ **Estado: implementado** (dentro de `PlayerMovement.cs`). Funcionan **doble salto, dash, slide, slam, wallrun, walljump, tirolina** y escaleras, con *beat gating* y coste de energía por acción. La implementación real concentra la lógica en `PlayerMovement` en lugar de repartirla en scripts por estado como se propone abajo. El **gancho (grapple) se descartó y se eliminó**. El resto de la sección se conserva como referencia de diseño.

### Arquitectura: PlayerStateManager

El movimiento se controla con una **State Machine** explícita. Esto elimina los bugs de estados solapados (como el wallrun que interrumpe el dash, o el slide que interactúa mal con el salto):

```
Estados del jugador:
├── Grounded        ← en el suelo, puede saltar, slide
├── Airborne        ← en el aire, puede doble salto
├── Dashing         ← dash activo (override total de movimiento)
├── WallRunning     ← pegado a pared corriendo horizontalmente
├── WallJumping     ← impulso desde pared (breve)
├── Sliding         ← deslizándose por el suelo
└── Dead            ← muerto, sin input
```

Transiciones permitidas:
- `Grounded` → puede iniciar `Dashing`, `Airborne` (salto), `Sliding`
- `Airborne` → puede iniciar `Dashing`, `WallRunning`
- `WallRunning` → puede iniciar `WallJumping`, `Airborne` (caída)
- `Sliding` → puede cancelar a `Grounded` o `Airborne` (salto desde slide = boost)
- `Dashing` → siempre vuelve al estado previo al terminar

### Auto-run — implementación

```csharp
// En PlayerController.Update()
// El jugador siempre avanza en la dirección que mira (sin input)
Vector3 autoRunDir = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
Vector3 autoRunVelocity = autoRunDir * baseRunSpeed;

// El jugador SOLO controla horizontal (A/D para strafe leve o girar cámara)
// Si mira hacia atrás (dot product < -0.5), se invierten controles horizontales
float forwardDot = Vector3.Dot(Camera.main.transform.forward, transform.forward);
float strafeMultiplier = forwardDot < -0.5f ? -1f : 1f;
```

### Slide — implementación

```csharp
// PlayerSlide.cs
public class PlayerSlide : MonoBehaviour
{
    [SerializeField] private float slideDuration = 0.8f;
    [SerializeField] private float slideSpeedBoost = 1.5f;    // multiplicador sobre runSpeed
    [SerializeField] private float slideEnergyCost = 10f;
    [SerializeField] private float crouchHeight = 0.7f;

    private CharacterController _cc;
    private float _slideTimer;
    private bool _isSliding;
    private float _defaultHeight;

    // Llama a esto desde PlayerController cuando se detecta Shift presionado + grounded
    public bool TryStartSlide(float currentSpeed)
    {
        if (_isSliding) return false;
        if (!EnergyManager.Instance.TrySpend(slideEnergyCost)) return false;

        _isSliding = true;
        _slideTimer = slideDuration;
        _cc.height = crouchHeight;
        // Impulso inicial en la dirección actual
        _slideVelocity = transform.forward * currentSpeed * slideSpeedBoost;
        return true;
    }

    public void UpdateSlide(ref Vector3 moveDir)
    {
        if (!_isSliding) return;

        _slideTimer -= Time.deltaTime;
        // Decelerar progresivamente
        _slideVelocity = Vector3.Lerp(_slideVelocity, Vector3.zero, Time.deltaTime * 3f);
        moveDir = _slideVelocity;

        if (_slideTimer <= 0f) EndSlide();
    }

    public void EndSlide()
    {
        _isSliding = false;
        _cc.height = _defaultHeight;
    }
}
```

### Wallrun — implementación

```csharp
// PlayerWallrun.cs
public class PlayerWallrun : MonoBehaviour
{
    [SerializeField] private LayerMask _wallMask;
    [SerializeField] private float _wallCheckDistance = 0.7f;
    [SerializeField] private float _wallRunGravityScale = 0.2f;   // casi sin gravedad
    [SerializeField] private float _wallRunSpeed = 10f;
    [SerializeField] private float _wallRunDuration = 1.5f;
    [SerializeField] private float _wallJumpForce = 8f;
    [SerializeField] private float _wallJumpLateralForce = 5f;
    [SerializeField] private float _energyPerSecond = 6f;

    private bool _isWallRunning;
    private Vector3 _wallNormal;
    private float _wallRunTimer;

    public bool TryStartWallrun(Vector3 velocity)
    {
        // Solo se puede iniciar en el aire
        if (_cc.isGrounded) return false;

        // Raycast a ambos lados
        bool hitLeft  = Physics.Raycast(transform.position, -transform.right, out var hitL, _wallCheckDistance, _wallMask);
        bool hitRight = Physics.Raycast(transform.position,  transform.right, out var hitR, _wallCheckDistance, _wallMask);

        if (!hitLeft && !hitRight) return false;

        _wallNormal = hitLeft ? hitL.normal : hitR.normal;
        _isWallRunning = true;
        _wallRunTimer = _wallRunDuration;
        return true;
    }

    public void UpdateWallrun(ref Vector3 moveDir, ref float verticalVelocity)
    {
        if (!_isWallRunning) return;

        // Consumir energía por segundo
        if (!EnergyManager.Instance.TrySpend(_energyPerSecond * Time.deltaTime))
        {
            EndWallrun();
            return;
        }

        // Comprobar que sigue habiendo pared
        bool stillOnWall = Physics.Raycast(transform.position, -_wallNormal, _wallCheckDistance + 0.1f, _wallMask);
        if (!stillOnWall) { EndWallrun(); return; }

        _wallRunTimer -= Time.deltaTime;
        if (_wallRunTimer <= 0f) { EndWallrun(); return; }

        // Movimiento paralelo a la pared + gravedad reducida
        Vector3 wallForward = Vector3.Cross(_wallNormal, Vector3.up).normalized;
        // Aseguramos que va en la dirección que avanza el jugador
        if (Vector3.Dot(wallForward, transform.forward) < 0f) wallForward = -wallForward;

        moveDir = wallForward * _wallRunSpeed;
        verticalVelocity = Mathf.Max(verticalVelocity, -2f); // frena la caída

        // Camera tilt: inclinar la cámara hacia la pared (hacer en CameraController)
    }

    public Vector3 WallJump()
    {
        if (!_isWallRunning) return Vector3.zero;
        EndWallrun();
        // Impulso alejándose de la pared + hacia arriba
        return _wallNormal * _wallJumpLateralForce + Vector3.up * _wallJumpForce;
    }

    public void EndWallrun()
    {
        _isWallRunning = false;
        _wallRunTimer = 0f;
    }

    public bool IsWallRunning => _isWallRunning;
}
```

### Camera Effects (importantísimo para el game feel)

```csharp
// CameraEffects.cs — añade esto al script de cámara existente
public class CameraEffects : MonoBehaviour
{
    [Header("Camera Tilt")]
    [SerializeField] private float wallrunTilt = 15f;   // grados
    [SerializeField] private float dashTilt = 5f;
    [SerializeField] private float tiltSpeed = 10f;

    [Header("FOV")]
    [SerializeField] private float normalFov = 75f;
    [SerializeField] private float dashFov = 95f;
    [SerializeField] private float wallrunFov = 85f;
    [SerializeField] private float fovSpeed = 8f;

    private Camera _cam;
    private float _targetTilt;
    private float _targetFov;

    void Update()
    {
        // Suavizar tilt y FOV
        float currentZ = _cam.transform.localEulerAngles.z;
        float newZ = Mathf.LerpAngle(currentZ, _targetTilt, Time.deltaTime * tiltSpeed);
        _cam.transform.localEulerAngles = new Vector3(_cam.transform.localEulerAngles.x, 0, newZ);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _targetFov, Time.deltaTime * fovSpeed);
    }

    public void SetWallrunLeft()  { _targetTilt = -wallrunTilt; _targetFov = wallrunFov; }
    public void SetWallrunRight() { _targetTilt =  wallrunTilt; _targetFov = wallrunFov; }
    public void SetDash()         { _targetTilt = 0f;           _targetFov = dashFov;    }
    public void ResetEffects()    { _targetTilt = 0f;           _targetFov = normalFov;  }
}
```

---

## 6. SISTEMA DE ENERGÍA / JERINGUILLA

> ✅ **Estado: implementado.** El `EnergyManager` real ya incluye los **multiplicadores de timing** (Perfect 1.5× / Good 1×) descritos abajo, además de **modo azul (bonus)** de riesgo-recompensa, **penalización anti-spam por beat** (en `RhythmInput`: cada clic de más en el mismo beat cuesta el doble, con «calor» si se repite) y **God Mode** para diseño. La muerte ocurre cuando la jeringuilla se llena por completo en modo rojo.

### El sistema actual es bueno — ajustes recomendados

El diseño de la jeringuilla (modo rojo/azul) es muy original. Los ajustes son menores:

**1. Añadir multiplicadores de timing al hit**

En vez de que cada hit quite siempre la misma cantidad de sangre, escala con el timing:

```csharp
public float HitRed(TimingResult timing)
{
    float multiplier = timing switch
    {
        TimingResult.Perfect => 1.5f,   // 150% de energía
        TimingResult.Good    => 1.0f,   // energía normal
        TimingResult.Miss    => 0f,     // no da energía + penalización
        _                    => 1.0f
    };
    // Aplicar multiplier al _hitInjectAmount antes de calcular
    float originalAmount = _hitInjectAmount;
    _hitInjectAmount = originalAmount * multiplier;
    float result = HitRedInternal();
    _hitInjectAmount = originalAmount;
    return result;
}
```

**2. Degradación de velocidad según energía**

```csharp
// En PlayerMovement.Update()
float energyRatio = EnergyManager.Instance.EnergyPercent; // 0..1
float speedMultiplier = Mathf.Lerp(0.4f, 1.0f, energyRatio);
// Con 0% energía, vas al 40% de velocidad. Con 100%, velocidad normal.
float currentRunSpeed = baseRunSpeed * speedMultiplier;
```

**3. Efecto visual de baja energía**

Cuando `EnergyPercent < 0.25f`: vignette roja + latido en pantalla + postprocesado de desaturación. Ya tienes `LowHealthEffects.cs` — conectarlo al `EnergyManager.OnEnergyChanged`.

---

## 7. SISTEMA DE PUNTUACIÓN — LA BALANZA

### El concepto narrativo

Al final del nivel, el jugador deposita el corazón de su amado en una balanza contra una pluma. Cuanto más "puro" (ligero) sea el corazón — menos errores, más fluido el ritmo — más baja la pluma. Es una referencia al juicio de Ma'at en el Antiguo Egipto, perfectamente conectada con la temática.

### Fórmula de puntuación

```
Puntuación_Final = (Puntos_Ritmo + Puntos_Tiempo + Puntos_Monedas) × Multiplicador_Combo
                   - Penalización_Daño - Penalización_Muertes

Donde:

Puntos_Ritmo = (N_Perfect × 100 + N_Good × 50) / Total_Beats × 1000
  → Máximo 1000 puntos. Mide la calidad rítmica durante todo el nivel.

Puntos_Tiempo = max(0, 500 - (TiempoReal - TiempoOptimo) × 10)
  → Bonus por completar el nivel cerca del tiempo óptimo.
  → TiempoOptimo se fija en diseño para cada nivel.

Puntos_Monedas = MonedasRecogidas × 5
  → Simple, escala con el nivel.

Multiplicador_Combo = 1.0 + (MaxComboAlcanzado / Total_Beats) × 0.5
  → Entre 1.0x y 1.5x según el combo máximo.

Penalización_Daño = DañoRecibido × 2
Penalización_Muertes = NMuertes × 100
```

### Rating — El Peso del Corazón

| Rango de Puntuación | Rating | Estado de la Balanza |
|---------------------|--------|----------------------|
| ≥ 2000 | S — Corazón de Pluma | La pluma cae al suelo |
| 1500-1999 | A — Corazón Puro | La balanza queda en equilibrio |
| 1000-1499 | B — Corazón Marcado | El corazón pesa ligeramente más |
| 500-999 | C — Corazón Herido | El corazón pesa claramente más |
| < 500 | D — Corazón de Piedra | El corazón aplasta la pluma |

### Implementación

```csharp
// ScoreManager.cs
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    // Acumuladores durante el nivel
    private int _perfectCount;
    private int _goodCount;
    private int _missCount;
    private int _totalBeats;
    private int _maxCombo;
    private int _currentCombo;
    private int _coinsCollected;
    private float _damageTaken;      // en unidades de energía perdida
    private int _deaths;
    private float _levelStartTime;
    private float _optimalTime;      // fijado por LevelData ScriptableObject

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void StartLevel(float optimalTime)
    {
        _optimalTime = optimalTime;
        _levelStartTime = Time.time;
        ResetCounters();
    }

    public void RegisterTiming(TimingResult result)
    {
        _totalBeats++;
        switch (result)
        {
            case TimingResult.Perfect: _perfectCount++; IncrementCombo(); break;
            case TimingResult.Good:    _goodCount++;    IncrementCombo(); break;
            case TimingResult.Miss:    _missCount++;    ResetCombo();     break;
        }
    }

    private void IncrementCombo()
    {
        _currentCombo++;
        if (_currentCombo > _maxCombo) _maxCombo = _currentCombo;
    }

    private void ResetCombo() => _currentCombo = 0;

    public void AddCoin() => _coinsCollected++;
    public void AddDamage(float amount) => _damageTaken += amount;
    public void AddDeath() => _deaths++;

    public ScoreData CalculateFinalScore()
    {
        float timeTaken = Time.time - _levelStartTime;

        float rhythmPoints = _totalBeats > 0
            ? ((_perfectCount * 100f + _goodCount * 50f) / _totalBeats) * 10f
            : 0f;

        float timePoints = Mathf.Max(0f, 500f - (timeTaken - _optimalTime) * 10f);
        float coinPoints = _coinsCollected * 5f;
        float comboMult  = 1f + (_totalBeats > 0 ? ((float)_maxCombo / _totalBeats) * 0.5f : 0f);
        float damage     = _damageTaken * 2f;
        float deathPen   = _deaths * 100f;

        float total = (rhythmPoints + timePoints + coinPoints) * comboMult - damage - deathPen;
        total = Mathf.Max(0f, total);

        return new ScoreData
        {
            total        = Mathf.RoundToInt(total),
            rhythmPoints = Mathf.RoundToInt(rhythmPoints),
            timePoints   = Mathf.RoundToInt(timePoints),
            coinPoints   = Mathf.RoundToInt(coinPoints),
            comboMult    = comboMult,
            perfect      = _perfectCount,
            good         = _goodCount,
            miss         = _missCount,
            deaths       = _deaths,
            rating       = GetRating(Mathf.RoundToInt(total))
        };
    }

    private ScoreRating GetRating(int score)
    {
        if (score >= 2000) return ScoreRating.S;
        if (score >= 1500) return ScoreRating.A;
        if (score >= 1000) return ScoreRating.B;
        if (score >= 500)  return ScoreRating.C;
        return ScoreRating.D;
    }

    private void ResetCounters()
    {
        _perfectCount = _goodCount = _missCount = _totalBeats = 0;
        _maxCombo = _currentCombo = _coinsCollected = _deaths = 0;
        _damageTaken = 0f;
    }
}

public struct ScoreData
{
    public int total, rhythmPoints, timePoints, coinPoints;
    public float comboMult;
    public int perfect, good, miss, deaths;
    public ScoreRating rating;
}

public enum ScoreRating { S, A, B, C, D }
```

---

## 8. SISTEMA DE TIENDA Y ECONOMÍA

> ✅ **Estado: implementado** (ver sección 1). Tienda en el lobby de cada nivel con 3 consumibles y 27 mejoras que se desbloquean por nivel (9 por nivel), **muescas** para equiparlas (3 base + compradas), cosméticos pagados con **iris** y guardado en JSON. Los precios reales están en `ShopCatalog.BuildDefault()` / el asset del catálogo; las tablas de abajo son el diseño original.

### Diseño de economía base

Principio: el jugador gana monedas durante el nivel (no en la tienda). La tienda es donde las gasta entre niveles. Los precios deben diseñarse para que en una primera run sin morir el jugador pueda permitirse 1-2 upgrades entre cada nivel.

**Distribución de monedas por nivel:**
- Nivel 1: 80-120 monedas (fácil, abundantes)
- Nivel 2: 60-100 monedas (más difícil de recoger)
- Nivel 3: 100-150 monedas (nivel pasado, más tranquilo)

### Catálogo de upgrades (ScriptableObjects)

```csharp
// UpgradeDefinition.cs
[CreateAssetMenu(menuName = "Economy/Upgrade Definition")]
public class UpgradeDefinition : ScriptableObject
{
    public string upgradeName;
    [TextArea] public string description;
    public string narrativeFlavor;      // texto in-lore
    public Sprite icon;
    public int cost;
    public int maxLevel;                // cuántas veces se puede comprar
    public UpgradeEffect[] effects;     // qué modifica
}

[System.Serializable]
public class UpgradeEffect
{
    public UpgradeEffectType type;
    public float valuePerLevel;
}

public enum UpgradeEffectType
{
    MaxEnergy,              // +X de capacidad máxima
    EnergyPerPerfectHit,    // +X energía por Perfect
    ActionCostReduction,    // -X% coste de acciones
    PerfectWindowIncrease,  // +X segundos a la ventana Perfect
    GoodWindowIncrease,
    MissEnergyPenaltyReduce,
    SpeedBonus,
    ComboMultiplierBonus
}
```

### Tabla de upgrades propuestos

| Nombre | Flavor text | Coste | Efecto | Niveles |
|--------|-------------|-------|--------|---------|
| Mejora de Circulación I | "El demonio ajusta tus venas" | 40 | +20 max energía | 3 |
| Ritmo Cardíaco Estable | "Latidos más fuertes, más claros" | 60 | +0.02s ventana Perfect | 2 |
| Torrente Sanguíneo | "La sangre fluye con más fuerza" | 50 | +20% energía por hit | 2 |
| Tolerancia al Dolor | "Ya no sientes cuando fallas" | 35 | -30% penalización Miss | 2 |
| Músculo Reforzado | "Tus piernas ya no tiemblan" | 45 | -15% coste acciones | 3 |
| Adrenalina | "El miedo te hace más rápido" | 70 | +8% velocidad base | 1 |

### Objetos con riesgo/recompensa

| Nombre | Ventaja | Desventaja | Coste |
|--------|---------|------------|-------|
| Golosina | Empiezas al 80% de energía | Llenado pasivo +50% más rápido | 30 |
| Jeringa Directa | Perfect da 2x energía | Miss quita 2x energía | 55 |
| Corazón Acelerado | +20% velocidad siempre | Coste de todas las acciones +25% | 80 |
| Sangre Espesa | Max energía +40 | No puedes entrar en modo Azul | 40 |
| Pacto de Sangre | Primera muerte en cada nivel no cuenta | Pierdes todas las monedas al morir | 65 |

---

## 9. CONTRATOS DEMONÍACOS

> ✅ **Estado: implementado.** 18 contratos en 3 dificultades (Encargo / Contrato / Gran contrato), cada uno con una versión por nivel de juego (objetivo y pago crecientes; máx. 3 firmados por nivel), que se firman con el ratón en un puesto del lobby. Se cumplen durante el nivel (algunos a mitad de nivel) y el pago se **cobra en el puesto**. Reglas especiales: «Contrarreloj» (máx. 100 pulsaciones → si no, game over; desactiva el Auto-latido), «Al compás» (moverse en el beat, con medios beats) y «A pelo» (sin tienda).

### Diseño inspirado en Hades / Dead Cells

Cada zona hub tiene un demonio que ofrece 1-3 contratos. Aceptarlos es opcional pero da recompensas jugosas (más monedas, upgrades gratuitos, lore).

```csharp
// ContractDefinition.cs
[CreateAssetMenu(menuName = "Contracts/Contract Definition")]
public class ContractDefinition : ScriptableObject
{
    public string contractName;
    [TextArea] public string demonDialogue;
    [TextArea] public string contractText;          // qué exactamente pides
    public ContractModifier[] modifiers;            // efectos negativos
    public ContractReward reward;                   // lo que ganas
    public bool isActive;                           // si está aceptado
}

[System.Serializable]
public class ContractModifier
{
    public ContractModifierType type;
    public float value;
    public string description;
}

public enum ContractModifierType
{
    MaxBeatsReduced,         // límite de beats antes de morir más bajo
    TimingWindowHalved,      // la ventana de timing se reduce a la mitad
    ExtraEnemiesSpawned,     // X% más enemigos en el nivel
    SpeedIncreased,          // el nivel va X% más rápido
    EnergyDrainFaster,       // el llenado pasivo va más rápido
    NoDoubleJump,            // pierdes el doble salto
    NoDash                   // pierdes el dash
}
```

### Contratos de ejemplo

**Contrato 1 — "El Precio de la Prisa"**
- Diálogo: *"¿Quieres llegar más rápido, humano? Te daré alas... pero cada error pesa más."*
- Efecto: La penalización por Miss se duplica
- Recompensa: 80 monedas extra + upgrade gratuito de nivel 1

**Contrato 2 — "Latidos Contados"**
- Diálogo: *"Tu corazón tiene un límite. No lo desperdicies."*
- Efecto: El nivel tiene un contador de beats máximos (si llegas a 0 antes de terminar, game over)
- Recompensa: Rating mínimo garantizado B + lore exclusivo

**Contrato 3 — "A Ciegas"**
- Diálogo: *"¿Sabes el ritmo de tu propio corazón sin verlo?"*
- Efecto: La barra de energía desaparece (sigues teniendo energía, pero sin UI)
- Recompensa: Multiplicador de puntuación ×1.5 en el nivel

---

## 10. SISTEMA DE MUERTE Y RESPAWN

> ✅ **Estado: implementado, con cambios respecto al diseño.** Al morir: la música del nivel se corta y entra la de game over con *fade in*; las monedas de la partida se conservan. Consumibles: **Vida extra** (sigues en el siguiente beat), **Checkpoint** (reapareces en el último checkpoint y la canción sigue desde ese segundo, tras el 3-2-1) y **Rebobinado** (4 beats atrás: obstáculos, bloques, monedas y kayaks vuelven a su estado). Reiniciar es asíncrono.

### Flujo de muerte

```
1. EnergyManager dispara OnSyringeFull
2. PlayerStateManager pasa a estado Dead
3. BeatManager.StopMusic() — la música para inmediatamente
4. PostProcessing: desaturación rápida (0.3s) + vignette roja
5. Slow motion: Time.timeScale = 0.1f durante 0.5s (sensación de peso)
6. Fade a negro completo (0.5s)
7. Aparece pantalla de Game Over:
   - Efecto EKG plano (línea horizontal con pitido largo)
   - Botones: RETRY / QUIT
   - Estadísticas de lo que llevabas (combo, precision, tiempo)

8. Al pulsar RETRY:
   - Fade a blanco progresivo (2s) — el blanco representa el electroshock
   - Sonido de desfibrilador (thump × 2 con pausa dramática)
   - Flash intenso + chromatic aberration
   - Fade rápido de blanco a normal (0.5s)
   - El nivel reinicia desde el punto de inicio
   - La música reinicia en sample 0 (sincronizada con el beat)
   - El jugador empieza a correr automáticamente
   - ScoreManager registra +1 muerte
```

### Implementación del respawn musical

```csharp
// En el sistema de respawn — momento crítico
IEnumerator RespawnSequence()
{
    // 1. Desfibrilador
    yield return new WaitForSeconds(1.5f);  // pausa dramática
    PlayDefibrilatorSound();

    // 2. Flash blanco
    yield return StartCoroutine(FlashWhite(duration: 0.3f));
    yield return new WaitForSeconds(0.8f);
    PlayDefibrilatorSound();  // segundo shock
    yield return StartCoroutine(FlashWhite(duration: 0.3f));
    yield return new WaitForSeconds(1.0f);

    // 3. Fade de blanco a normal mientras carga el nivel
    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    // (la música reinicia en Start del nivel)
}

// En el Start del nivel:
void StartLevel()
{
    // Asegurarse de que la música empieza en el beat 0
    // con un pequeño delay para que el fade de entrada termine
    StartCoroutine(StartMusicAfterFade());
}

IEnumerator StartMusicAfterFade()
{
    yield return new WaitForSeconds(fadeInDuration);
    BeatManager.Instance.StartMusic();
    // El jugador empieza a correr simultáneamente
    PlayerController.Instance.EnableAutoRun();
}
```

---

## 11. DISEÑO DE NIVELES

### Principios generales

1. **Enseña antes de exigir.** Cada mecánica nueva se introduce en un espacio seguro antes de usarla en un contexto desafiante.
2. **El nivel ES la música.** La geometría del nivel se construye alrededor del BPM y la estructura musical. Los saltos principales caen en downbeats, los giros en offbeats.
3. **Ritmo visual del nivel.** Los obstáculos y plataformas se mueven o pulsan al beat. El jugador debería poder intuir el ritmo visual antes de escucharlo.
4. **El auto-run marca el tempo.** La velocidad base del jugador + la longitud del nivel determinan cuántos beats tiene el nivel. Diseña el recorrido para que encaje exactamente con la duración de la canción.

### Nivel 1 — La Ira (tutorial disfrazado)
**Emoción:** Rabia bruta, desesperación, caos inicial.
**BPM:** 140-160 (agresivo, industrial)
**Duración:** ~2:30 min
**Paleta:** Rojo oscuro, negro, roca quemada, lava
**Mecánicas introducidas:** Auto-run, click básico, jump, dash
**Estructura del nivel:**
- Tramo 1 (0:00-0:45): Corredor recto, solo plataformas verticales. El jugador aprende el auto-run y el click.
- Tramo 2 (0:45-1:30): Introducción del salto. Huecos pequeños, timing permisivo.
- Tramo 3 (1:30-2:00): Primer dash necesario. Brecha que no se puede saltar.
- Tramo 4 (2:00-2:30): Combinación de salto + dash + click bajo presión. Drop de la canción = momento de intensidad máxima.
- Final: El personaje llega a una caverna y se detiene. Cutscene breve → Hub 1.

### Nivel 2 — La Celda (prisión)
**Emoción:** Sufrimiento contenido, monotonía aplastante.
**BPM:** 90-110 (más lento, opresivo, con subidas)
**Mecánicas introducidas:** Slide, Wallrun
**Nota de diseño:** El BPM más lento hace los fallos más dolorosos y las ventanas de timing más exigentes en términos de atención.

### Niveles 3, 4, 5 — esquema similar

Cada nivel sube la complejidad de las mecánicas combinadas. El nivel 4 (amor) tiene el BPM más variable (la música cambia de tempo en secciones). El nivel 5 introduce el único momento de silencio en el juego — un instante sin música justo antes del boss donde solo se escucha el latido.

---

## 12. JUICE, GAME FEEL Y POLISH

### Checklist de game feel (priorizado)

**Alta prioridad (afectan directamente a la sensación rítmica):**
- [ ] Screen shake en dash (leve, horizontal)
- [ ] Camera tilt en wallrun (±15°)
- [ ] FOV dinámico (75→95 en dash, 75→85 en wallrun)
- [ ] Chromatic aberration al fallar un Miss
- [ ] Vignette roja pulsante cuando la energía es baja
- [ ] Hitmarker visual + sonoro en Perfect
- [ ] Partículas de sangre/latido en cada hit rítmico
- [ ] El latido del corazón (como SFX) se mezcla con la música y cambia de intensidad según la energía

**Media prioridad (mejoran la experiencia):**
- [ ] Trails en el dash (desaparecen en 0.1s)
- [ ] Squash & stretch en los saltos (la cámara baja levemente al saltar, sube al aterrizar)
- [ ] Sound de footstep sincronizado al beat (no al paso real del jugador)
- [ ] UI elements pulsan al beat (el borde de la pantalla, el indicador de energía)
- [ ] Slow motion leve (Time.timeScale = 0.85f durante 0.05s) en cada Perfect

**Baja prioridad (si hay tiempo):**
- [ ] Efectos de partículas en monedas recogidas
- [ ] UI de combo con animación de números
- [ ] Distortion de audio en baja energía (low-pass filter en la música)

### Música adaptativa — Capas de audio

El sistema más impactante para el feel rítmico. La música tiene varias capas (stems) que se activan/desactivan según el estado del juego:

```
Stem 1: Kick drum + bajos — SIEMPRE activo
Stem 2: Melodía principal — activo cuando energía > 50%
Stem 3: Armonías — activo cuando energía > 75% o en modo Azul
Stem 4: Capa de distorsión — activo cuando energía < 25%
Stem 5: Capa de "caos" — activo durante contrato demoníaco activo
```

```csharp
// MusicLayerController.cs
public class MusicLayerController : MonoBehaviour
{
    [SerializeField] private AudioSource[] _stems;   // 5 AudioSources, mismo clip, distinto volumen
    [SerializeField] private float _fadeSpeed = 2f;
    private float[] _targetVolumes;

    void Start()
    {
        _targetVolumes = new float[_stems.Length];
        // Inicialmente solo stem 0 activo
        _targetVolumes[0] = 1f;
    }

    void Update()
    {
        float energy = EnergyManager.Instance?.EnergyPercent ?? 0.5f;
        bool isBlue  = EnergyManager.Instance?.IsBlueMode ?? false;

        _targetVolumes[1] = energy > 0.5f ? 1f : 0f;
        _targetVolumes[2] = (energy > 0.75f || isBlue) ? 0.8f : 0f;
        _targetVolumes[3] = energy < 0.25f ? 0.6f : 0f;
        // Stem 4 lo controla ContractManager

        for (int i = 0; i < _stems.Length; i++)
        {
            _stems[i].volume = Mathf.MoveTowards(
                _stems[i].volume, _targetVolumes[i], _fadeSpeed * Time.deltaTime);
        }
    }

    public void SetChaosLayer(bool active) => _targetVolumes[4] = active ? 0.7f : 0f;
}
```

---

## 13. DISEÑO NARRATIVO

### Estructura narrativa del juego

El juego cuenta la historia en dos capas simultáneas:

**Capa 1 — Gameplay como metáfora:** El corazón que el jugador lleva en la mano late cuando hace clic al ritmo. Si falla, el corazón se debilita. Esta mecánica ES la historia: el protagonista mantiene vivo a su amado literalmente con sus propios latidos.

**Capa 2 — Lore ambiental:** Cada nivel tiene fragmentos de historia contados a través de:
- Graffiti en las paredes (nivel 2 — prisión)
- Memorias visuales que aparecen al fondo cuando hay combos altos
- Diálogos breves con el demonio al inicio de cada nivel (voz en off)
- El propio nivel como visualización del estado emocional

### Arco del protagonista por nivel

| Nivel | Emoción | Lo que recuerda | Revelación |
|-------|---------|-----------------|------------|
| 1 | Rabia/Desesperación | El accidente de la mina | Que su amado sigue vivo en algún sentido |
| 2 | Sufrimiento | La vida en prisión antes del accidente | La injusticia que lo llevó allí |
| 3 | Traición/Inocencia | Su pasado libre antes de la cárcel | Quién lo traicionó realmente |
| 4 | Amor/Esperanza | Su relación con su compañero | Por qué vale la pena seguir |
| 5 | Optimismo oscuro | El momento en que hizo el trato | Que el demonio le mintió |
| Boss | Transformación | Nada — el presente | Que al matar al demonio, él mismo se convierte en lo que odia |

### El giro narrativo
El jugador descubre que el "objeto del infierno" no es un artefacto sino el corazón de su amado, que el demonio quería usar como llave. Al recuperarlo, el protagonista destruye la posibilidad de que su amado vuelva realmente — porque el corazón ya no pertenece al mundo de los vivos. La ira que siente al comprender esto lo transforma.

**Nota de diseño:** Este giro debe comunicarse con gameplay, no solo con cutscene. En el nivel 5, el jugador debería notar que el corazón en su mano late más fuerte que nunca — pero al llegar al boss, la música cambia y el latido se vuelve irregular.

---

## 14. INVESTIGACIÓN ACADÉMICA — PAPERS Y REFERENCIAS

### Preguntas de investigación del TFG

1. ¿Cómo afecta la sincronización automática de gameplay y música a la experiencia de flow del jugador?
2. ¿Cómo influyen las ventanas de timing (Perfect/Good/Miss) en la percepción de dificultad y satisfacción?
3. ¿Cómo afecta la barra de energía rítmica (consecuencia de los errores en tiempo real) a la tensión y el engagement?
4. ¿Qué características del diseño de niveles favorecen el estado de flow en un rhythm game?

### Papers relevantes

**Beat Tracking y Sincronización Musical**
- Bello, J.P. et al. (2005). "A Tutorial on Onset Detection in Music Signals." *IEEE Transactions on Speech and Audio Processing.* — Fundamentos del onset detection que usas implícitamente con tu BeatManager.
- Böck, S. & Schedl, M. (2011). "Enhanced Beat Tracking with Context-Aware Neural Networks." *ISMIR 2011.* — Tracking automático de BPM con NN.
- Davies, M.E.P. et al. (2009). "Evaluation Methods for Musical Audio Beat Tracking Algorithms." *ISMIR 2009.*

**Flow State y Videojuegos**
- Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience.* Harper & Row. — La referencia base del concepto de flow. Cita extensamente.
- Chen, J. (2007). "Flow in Games (and Everything Else)." *Communications of the ACM, 50*(4), 31-34. — Aplicación directa del flow a videojuegos. El autor diseñó *Journey*. Cita obligatoria.
- Sweetser, P. & Wyeth, P. (2005). "GameFlow: A Model for Evaluating Player Enjoyment in Games." *ACM Computers in Entertainment, 3*(3). — Modelo GameFlow con 8 componentes. Muy citado.

**Rhythm Games y Diseño**
- Thorn, A. (2015). *Play the Music, Play the Game: Game-Music Relations in Rhythm and Exergames.* — Análisis de rhythm games como género.
- Collins, K. (2008). *Game Sound: An Introduction to the History, Theory, and Practice of Video Game Music and Sound Design.* MIT Press. — Referencia estándar para música en videojuegos.
- Huiberts, S. (2010). *Captivating Sound: The Role of Audio for Immersion in Games.* PhD Thesis. — Muy relevante para la inmersión auditiva.

**Música Adaptativa**
- Livingstone, S.R. & Brown, A.R. (2005). "Dynamic Response: Real-time Adaptation for Music Emotion." *Proceedings of the Australasian Computer Music Conference.*
- Berndt, A. et al. (2006). "Musical Nonlinearity in Interactive Narrative Environments." *Proceedings of AISB.*

**Diseño de Mecánicas y Balance**
- Schell, J. (2008). *The Art of Game Design: A Book of Lenses.* Morgan Kaufmann. — Referencia general esencial. Especialmente los Lenses 15 (Risk/Reward), 25 (Skill), y 57 (Music).
- Cook, D. (2012). "The Chemistry Of Game Design." Gamasutra (ahora Game Developer). — Artículo sobre loops de habilidad. Disponible online gratis.

**Timing y Percepción Musical**
- Repp, B.H. & Su, Y.H. (2013). "Sensorimotor Synchronization: A Review of Recent Research." *Psychonomic Bulletin & Review, 20*(3), 403-452. — Cómo los humanos sincronizamos con el ritmo.
- Zatorre, R.J. & Salimpoor, V.N. (2013). "From Perception to Pleasure: Music and Its Neural Substrates." *PNAS, 110*(Suppl 2), 10430-10437. — Por qué el ritmo nos produce placer.

### Metodología de investigación propuesta

**Experimento 1 — Ventanas de timing:** Implementa 3 configuraciones de ventanas (estrecha: ±50ms, media: ±100ms, amplia: ±150ms). Pide a N=10-15 sujetos que jueguen el mismo nivel con cada configuración. Mide: tasa de éxito, tiempo en completar, autoreporte de satisfacción (escala Likert).

**Experimento 2 — Energía y tensión:** Mide la correlación entre el nivel de energía del jugador y la frecuencia de errores. ¿Los jugadores fallan más cuando están en baja energía (estrés)? Esto conecta con el concepto de choking under pressure.

**Experimento 3 — Flow state:** Usa el cuestionario FSS-2 (Flow State Scale-2, Jackson & Eklund 2002) después de sesiones de juego. Correlaciona las puntuaciones de flow con la precisión rítmica media.

---

## 15. RIESGOS TÉCNICOS IDENTIFICADOS

### Riesgo 1 — Latencia de audio en WebGL / builds de entrega ⚠️ ALTO
**Problema:** Unity + Windows tiene latencia variable de audio. Si tu TFG se evalúa en un PC distinto al tuyo, el timing puede desincronizarse completamente.
**Mitigación:** Todo el timing va por el reloj DSP (canción, metrónomo y 3-2-1 con `PlayScheduled`). El offset de latencia se puede ajustar en el `BeatManager` (y se lee de `PlayerPrefs` si existe). La pantalla de calibración se descartó; si la evaluación se hace en otro PC, ajustar el offset a mano.

### Riesgo 2 — Wallrun + CharacterController ⚠️ MEDIO
**Problema:** El CharacterController de Unity no interactúa bien con superficies verticales. Puede clipear, flotar o quedar pegado.
**Mitigación:** Usa raycasts manuales para detectar la pared, no dependas del collision normal del CharacterController. Desactiva la gravedad manualmente durante el wallrun.

### Riesgo 3 — Scope creep ⚠️ ALTO
**Problema:** Con tantísimas ideas, es muy fácil pasar semanas en una feature que no es crítica para el TFG.
**Mitigación:** Cada feature nueva pasa por el filtro: "¿Esto mejora la investigación académica o solo el juego?" Si es solo el juego, va a la lista de trabajo futuro.

### Riesgo 4 — Música original ⚠️ MEDIO
**Problema:** Componer música original para 5 niveles más el boss es un proyecto en sí mismo.
**Mitigación:** Para el TFG, prioriza 1 canción completa (Nivel 1) y para los demás usa versiones cortas/loops. Alternativamente, usa música libre de derechos con stems disponibles (Incompetech, Free Music Archive) para los niveles de demostración.

### Riesgo 5 — Performance en builds de entrega ⚠️ BAJO-MEDIO
**Problema:** Efectos de postprocesado + partículas + audio layers pueden bajar los FPS en PCs de gama media.
**Mitigación:** Profiler desde el principio. Mantén un presupuesto: máximo 500 partículas simultáneas, máximo 4 audio sources simultáneos de música.

---

## 16. CÓDIGO C# DE REFERENCIA

### Sistema de pasos sincronizados

```csharp
// FootstepSync.cs
public class FootstepSync : MonoBehaviour
{
    [SerializeField] private AudioSource _footstepSource;
    [SerializeField] private AudioClip[] _footstepClips;
    [SerializeField] private BeatManager _beatManager;
    [Range(0f, 1f)][SerializeField] private float _footstepVolume = 0.4f;

    // Suscríbete al OnHalfBeat del BeatManager para pasos en subdivisión
    void OnEnable()
    {
        if (_beatManager != null)
            _beatManager.OnHalfBeat.AddListener(PlayFootstep);
    }

    void OnDisable()
    {
        if (_beatManager != null)
            _beatManager.OnHalfBeat.RemoveListener(PlayFootstep);
    }

    void PlayFootstep()
    {
        // Solo si el jugador está en el suelo y en movimiento
        var cc = GetComponent<CharacterController>();
        if (cc == null || !cc.isGrounded) return;

        AudioClip clip = _footstepClips[Random.Range(0, _footstepClips.Length)];
        _footstepSource.PlayOneShot(clip, _footstepVolume);
    }
}
```

---

## 17. REGISTRO DE LIMPIEZA Y CAMBIOS — AUDITORÍA SEP. 2026

Auditoría de código en 4 fases: detectar huérfanos → retirarlos (en cuarentena y verificando que compila) → simplificar el código restante sin cambiar el comportamiento → actualizar este documento.

### Archivos eliminados
| Archivo | Motivo |
|---|---|
| `GrapplingHook.cs`, `GrapePoint.cs` | Stubs vacíos del gancho descartado. |
| `HUD/OxygenBarUI.cs`, `HUD/UnderwaterFX.cs` | Stubs vacíos del buceo/oxígeno (eliminado: caer al agua = game over). |
| `HUD/SyringeFillUI.cs` | Sin uso (el HUD de la jeringuilla va por `LiquidLevel`). |
| `HUD/RhythmShockwave.cs` | Sin prefab ni referencias. |
| `BeatObstacleSpawner.cs`, `BeatApproachIndicator.cs` | Generador de obstáculos sin uso; el indicador (en `Breakable.prefab`) no hacía nada sin él. |
| `GameStarter.cs` | Sustituido por `StartSequenceManager`. |
| `LatencyCalibrator.cs` | Pantalla nunca conectada a una escena. |
| `LiquidWobble.cs`, `ConstantRotator.cs`, `BoneRope.cs` | Sin uso en escenas ni código. |
| `ArmPlaceholder.cs` | Brazos-cápsula del bloqueo; el objeto `ArmRig` ya estaba desactivado. |
| `PlayerSyncDisplay.cs` | Overlay de depuración del editor. |
| `Editor/DuplicateZOffsetTool.cs`, `Editor/DuplicateWithOffsetEditor.cs` | Herramientas duplicadas de «duplicar con offset Z». |
| `Editor/RopeMaterialBuilder.cs` | Generador del material de cuerda (ya creado). |
| `_Recovery/0.unity` | Copia de recuperación automática de Unity. |

También se quitaron del `Player.prefab` el componente `ArmPlaceholder` y el objeto `RopeMesh` (`TubeRenderer` de la cuerda del corazón del buceo), y de `Breakable.prefab` el `BeatApproachIndicator`.

### Código muerto eliminado (sin cambio de comportamiento)
- **Métodos sin llamadas:** `BeatManager.StartMusicFrom/SetBpm/SetLatencyOffset/BeatProgress/ClickIndex`, `EnergyManager.HasEnergy/EnterBlueMode/HitRed()/MaxEnergy`, `BossController.SpawnPillar/PlayerHitBlood/CurrentDifficulty`, `BossPillar.HitsFrontFace`, `BossPlatformSpawner.CollapseDropAt/StopSpawning/IsCollapsed`, `BossTailSweep.IsBusy/GripHand`, `BossMusicController.EndFinished`, `PlayerMovement.StartMoving`, `CameraDashEffect.OnDashSimple`, `BloodSucker.SetDrain`, `FloatingObject.React`, `RhythmRingVisual.SetElapsed`, `RecordedPath.TryGetPositionAtTime`, `TubeRenderer.Hide/SetEndpoints` (modo externo), `PlayerProgress.IsContractClaimed`, `LevelCoins.SetRun`, `LevelEndManager.SetEnd`, `CrosshairController.CurrentAngularVelocity`, `KayakController.IsFloating`, `PlayerPathRecorder.IsRecording`.
- **Campos sin efecto:** `RhythmInput._hitWindow` (legacy), `BreakableBlock.destroyOnBreak`, `PlayerMovement.dashCooldown/slideCooldown/wallrunSpeed` (+ contadores de cooldown), `KayakController.endPointRadius`, `BossController._playerHitBlood`, `WaterDiveController.debugLogs`, variables asignadas y nunca leídas (`_fallDuration`, `_musicSourceOriginalVolume`, `_gameOverManager`, `_defaultStepOffset`, `_slideDirection`), `Update()` vacío de `BeatGridVisualizer`, 6 `using` sin uso.
- **Guardado:** fuera las migraciones antiguas (`notchVersion`, `contractsClaimVersion`, `disabledUpgrades`, `version`).
- **Comentarios obsoletos** corregidos (referencias a buceo/oxígeno, «versión anterior», «compatibilidad», «legacy», scripts borrados).

### Cambios de comportamiento acordados
- **Anti-spam rehecho:** fuera la ventana deslizante de 10 inputs de `EnergyManager`; ahora es por beat en `RhythmInput` (clics de más ×2, ×4… hasta ×8, con «calor» hasta ×4 si se repite). Un nivel ya no se puede pasar spameando.
- **`WaterDiveController`:** detección del agua solo por raycast (antes también por colisión y trigger, que eran redundantes: el agua es sólida).
- **3-2-1-¡YA!:** metrónomo generado por código (`MetronomeClicks`, sin la latencia del clip de audio; el campo `_metroSource` desapareció); tic agudo = puedes empezar, grave = en racha; reinicio inmediato al fallar; cada acierto tiene que caer en el beat siguiente exacto (antes se podía saltar un beat y seguir); solo late el círculo que toca; arreglado que los círculos dejaran de latir tras un fallo; la canción entra en el beat siguiente al ¡YA! aunque se pulse un pelín antes.
- **Metrónomo de la tienda** refactorizado para usar `MetronomeClicks` (suena igual).

### Tienda (oct. 2026)
- Quitadas las mejoras **Coágulo** (`miss_guard`) y **Pulso limpio** (`clean_pulse`), con sus assets y su código en `EnergyManager` / `RhythmInput`.
- Quitado el **árbol de desbloqueos** y la pantalla de categorías: `ShopUITree.cs`, `ShopUINotches.cs`, `ShopUICosmetics.cs` → todo en `ShopUI.cs` con el nuevo diseño (rejilla + ficha).
- `ShopUnlocks` pasa a ser una tabla de **nivel de desbloqueo** por objeto (10 por nivel).

### Herramientas nuevas
- `LevelHierarchyOrganizer` (Tools → Nivel → Organizar jerarquía): los tres niveles se han reorganizado por segmentos de 4 beats. Copia de seguridad previa en `_CopiaEscenas_antesDeOrganizar/` (se puede borrar tras comprobar los niveles).

### Oct. 2026 — Menús, ajustes e idioma
- **Ajustes** (`GameSettings`): volumen y sensibilidad compartidos por el menú principal y la pausa; al mover el volumen suena el metrónomo generado (`VolumePreview`). La sensibilidad ya la aplica `PlayerMovement` (0,2×–3×).
- **Pausa**: oculta todo el HUD (Canvas, avisos OnGUI y carteles del tutorial vía `PauseMenuManager.IsPaused`); botón y panel de Ajustes en el prefab Canvas (`Tools/Pausa/…`). En modo prueba, volver al menú avisa y descalifica (`StudySession.Abort`, fila `abandono`).
- **Menú principal**: fondo de fuego (`MenuFireBackground`), música de Game Over en bucle, rótulos de los sliders.
- **Idioma** (`Loc`): español por defecto / inglés, elegido en Ajustes (`LanguageButton`). Textos de código con `Loc.T(es, en)`; textos de escenas/prefabs con `LocalizedText` (`Tools/Idioma/Preparar menús`). Tienda en inglés en `ShopTextsEn`; contratos, cosméticos, estudio y tutorial traducidos. El Google Form final sigue en español.

### Oct. 2026 — Organización de los scripts
Carpetas de `Assets/Scripts` por sistema:
- `Rhythm/`: BeatManager (+ TimingResult), RhythmInput, MetronomeClicks, GlassShatterSfx, PulseToTheBeat.
- `GameFlow/`: inicio (StartSequenceManager, StartWall, DoorInteraction, LobbyMusic), fin de nivel y game over.
- `Player/`: movimiento, energía, kayak, bloqueo, mano, cámara, pasos, buceo.
- `Level/`: `Obstacles/`, `Enemies/` (BloodSucker, GazeSwitch), `Environment/` (agua, tirolina, rejilla de beats), `Collectibles/` (monedas, iris), `LevelDesign/` (herramientas de diseño en escena).
- `UI/`: utilidades comunes (SimpleUI, UIKit, CursorRestorer), `HUD/` y `Menus/` (principal, pausa, ajustes, fondo de fuego).
- `Settings/`: GameSettings (volumen, sensibilidad, VolumePreview), idioma (Loc, LocalizedText, LanguageButton).
- `Shop/`: `Data/` (progreso, catálogo, textos, desbloqueos, muescas, cosméticos), `Perks/` (efectos de mejoras y consumibles, + IRewindable en TimeRewinder), `UI/`.
- `Boss/`, `Contracts/`, `Study/` (StudyConfig dentro de StudySettings), `Tutorial/`.
- `Editor/`: `BeatTracking/`, `Inspectors/`, `Level/` (incluye GameEditorMenus = menús Tools de Tienda/Contratos/Lobby), `Menus/` (MenuDesignTool reúne las herramientas de menús, idioma y fondo de fuego; UISharpnessTool).
Fusiones: 4 menús de editor pequeños → GameEditorMenus; MainMenuLayoutFix + LanguageTool + PauseMenuSettingsTool → MenuDesignTool (sin el código muerto); StudyConfig → StudySettings; IRewindable → TimeRewinder; TimingResult → BeatManager.

---

*Documento generado como referencia de desarrollo — actualizar con cada milestone completado.*
*Estado (sep. 2026): 3 niveles + jefe jugables, tienda, muescas, contratos y cosméticos implementados; código auditado y limpio (sección 17).*
