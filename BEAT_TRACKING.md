# Beat tracking: detección automática del tempo

Herramienta: **Tools → Beat Tracking → Analizador de tempo** (`Assets/Scripts/Editor/BeatTracker.cs` + `BeatTrackingWindow.cs`).

## Qué hace
1. Analiza una canción y da su **BPM** y el **desfase del primer beat** (si la canción empieza justo en un beat, ≈ 0 ms).
2. **Aplicar al BeatManager**: escribe el BPM detectado en el `BeatManager` de la escena (con Deshacer). Así la sincronización de todo el juego (input, 3-2-1, ataques del jefe, carteles…) sale del análisis automático.
3. **Evaluar todas las canciones**: analiza `Assets/Songs`, las compara con su BPM de referencia y guarda `BeatTracking_Evaluacion.csv` en la carpeta del proyecto.

## Método (offline, sin librerías externas)
1. **Preprocesado**: mezcla a mono y diezmado a ~11 kHz.
2. **Envolvente de ataques (onset strength)**: *flujo espectral*, es decir, la suma de las subidas del espectro en log-magnitud entre tramas consecutivas. Usa FFT de 1024 muestras con ventana de Hann y saltos de 256, lo que da unas 43 tramas/s. A la envolvente se le resta su media local (1 s), se rectifica, se normaliza y se suaviza.
3. **Estimación del tempo por «peine»**: para cada tempo entre 50 y 210 BPM (paso de 0,25) se mide la energía media de la envolvente en un tren de pulsos a ese tempo, probando 24 fases y quedándose con la mejor.
4. **Corrección de octava**: el máximo del peine suele caer en la *mitad* del tempo real, porque un pulso lento acierta en los tiempos fuertes. Se revisan sus múltiplos métricos (×4/3, ×3/2, ×2, ×3) y se elige el **más rápido que conserve ≥ 80 %** de la fuerza del máximo.
5. **Ajuste fino**: ±1 % en pasos de 0,01 BPM con 64 fases. Devuelve el BPM final y la fase. La fase se corrige con medio tamaño de ventana, que es donde cae de media el ataque dentro de la trama.
6. **Confianza** = fuerza del peine elegido / fuerza media de todos los tempos (≥ 2 pulso claro; < 1,5 dudoso).

Es un enfoque clásico de *onset detection + periodicidad* (Scheirer 1998; Ellis 2007, «Beat Tracking by Dynamic Programming»; Böck et al. / librosa usan variantes del mismo esquema).

## Resultados con la música del juego
Referencia: BPM con el que se compuso cada pista (las intros y el final comparten tempo con su canción). Métricas habituales (MIREX): **Acierto 1** = dentro de ±4 %; **Acierto 2** = también vale el doble, la mitad, el triple o el tercio (error de octava).

| Pista | Duración (s) | BPM detectado | Pulso más fuerte | Desfase 1.er beat (ms) | Confianza | Referencia | Error | Resultado |
|---|---|---|---|---|---|---|---|---|
| DESCENSO (Nivel 1) | 170.7 | 179.99 | 90.00 | −11 | 3.66 | 180 | −0.01 % | ✅ exacto |
| EPLAC (Nivel 2) | 188.0 | 120.00 | 60.00 | 0 | 2.61 | 120 | 0.00 % | ✅ exacto |
| LDNM (Nivel 3) | 195.4 | 69.99 | 70.00 | −7 | 5.65 | 70 | −0.01 % | ✅ exacto |
| LDNM-BOSS-LOOP (jefe) | 54.9 | 70.01 | 70.00 | +6 | 3.76 | 70 | +0.01 % | ✅ exacto |
| DESCENSO-INTRO | 13.3 | 90.00 | 90.00 | −6 | 3.17 | 180 | −50 % | 🟡 octava |
| EPLAC-INTRO | 8.0 | 60.08 | 60.00 | 0 | 3.30 | 120 | −50 % | 🟡 octava |
| LDNM-INTRO | 6.9 | 58.80 | 58.50 | +30 | 2.29 | 70 | −16 % | ❌ |
| LDNM-END | 3.4 | 54.20 | 54.00 | +81 | 2.51 | 70 | −23 % | ❌ |
| GameOver (menú) | 136.0 | 59.99 | 60.00 | −16 | 8.09 | 120 | −50 % | 🟡 octava |
| Lobby1 | 192.0 | 60.00 | 60.00 | −16 | 9.88 | 60 | 0.00 % | ✅ exacto |
| Lobby2 | 128.0 | 60.00 | 60.00 | +203 | 6.34 | 120 | −50 % | 🟡 octava |
| Lobby3 | 57.6 | 50.01 | 50.00 | −10 | 9.55 | 100 | −50 % | 🟡 octava |

*(Resultados obtenidos con un prototipo en Python del mismo algoritmo. Al pasar «Evaluar todas las canciones» en Unity pueden variar en centésimas por la precisión numérica.)*

**Resumen:**
- **Canciones de los niveles y del jefe (4/4):** tempo exacto, con error < 0,02 %, y el primer beat a ≤ 11 ms del inicio. Esto confirma por análisis la suposición del juego de que «el beat 0 es el inicio del archivo».
- **Intros y final (cortos, 3-13 s):** 2 aciertos de octava y 2 fallos. Con menos de ~16 beats no hay periodicidad suficiente para distinguir el tempo de sus divisiones, una limitación conocida del método. En el juego se usa el tempo de su canción principal.
- **Total (12 pistas):** tempo exacto en **5/12** y correcto salvo octava en **10/12**. Todos los fallos de octava dan la **mitad** del tempo.
- **Música de ambiente a la mitad:** en Lobby2 y GameOver (120) y Lobby3 (100) los pulsos intermedios son muy débiles. El peine a 120 conserva solo el 53-62 % de la fuerza del de 60, prácticamente lo mismo que LDNM a 140 frente a su tempo real de 70 (56-58 %). Con solo la envolvente de ataques es **imposible distinguir** «canción a 70» de «canción a 120 con medios tiempos suaves»: es la ambigüedad de octava, un problema conocido del beat tracking (por eso MIREX usa también la métrica «salvo octava»). Bajar el umbral del 80 % arreglaría esas pistas pero rompería LDNM. Para el juego no importa: en un tempo a la mitad, los beats detectados caen sobre beats reales (uno de cada dos).
- **Pulso más fuerte frente a tempo elegido:** en DESCENSO y EPLAC el máximo bruto estaba en la mitad del tempo (90 y 60 BPM). La corrección de octava del paso 4 es la que consigue el acierto exacto; sin ella habrían sido errores de octava.

## Cómo usarlo en la memoria
- **Objetivo de la propuesta** («sincronizar música y jugabilidad de manera automática usando beat tracking»): el BPM del `BeatManager` puede salir del análisis automático (botón *Aplicar*). Después, la sincronización en tiempo de ejecución se hace con el reloj DSP del audio (`AudioSettings.dspTime`, `PlayScheduled`), que evita la deriva del reloj de frames.
- **Validación**: la tabla de arriba (o el CSV) compara lo detectado con la referencia.
- **Limitaciones y trabajo futuro**: pistas cortas, cambios de tempo (el método asume tempo constante), detección del compás (downbeat), y seguimiento beat a beat con programación dinámica para canciones con tempo variable.
