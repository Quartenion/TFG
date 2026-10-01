# Modo prueba: formularios, exportación y datos

Guía para preparar la prueba con jugadores. Hay que crear **dos** formularios de Google y pegar sus enlaces en `Assets/Scripts/Study/StudySettings.cs`.

---

## 1. Formulario OCULTO de datos (lo rellena el juego solo)

El juego envía aquí una línea de texto por cada intento y por cada cuestionario corto. El jugador nunca lo ve.

1. Crea un Google Form nuevo llamado, por ejemplo, **«TFG – datos de partida»**.
2. Añade **una sola pregunta** de tipo **Párrafo** con el título `datos`. No la marques como obligatoria.
3. En **Configuración**:
   - *Respuestas* → **No** recopilar correos y **no** exigir inicio de sesión.
   - Desactiva «Limitar a 1 respuesta».
4. En **Respuestas** → icono de Hojas de cálculo → **Crear hoja de cálculo** (ahí caerán los datos).
5. Saca los dos valores que pide el juego:
   - Menú **⋮ → Obtener enlace rellenado previamente** → escribe `x` en la pregunta → **Obtener enlace** → **Copiar enlace**.
   - El enlace tiene esta forma: `https://docs.google.com/forms/d/e/1FAIpQLSe…/viewform?usp=pp_url&entry.123456789=x`
   - **`DataFormUrl`** = la parte hasta `viewform`, cambiando `viewform` por `formResponse`: `https://docs.google.com/forms/d/e/1FAIpQLSe…/formResponse`
   - **`DataEntryId`** = `entry.123456789` (el número de tu enlace).
6. Pégalos en `StudySettings.cs`.

**Para comprobarlo:** juega una parte en el editor de Unity y mira la hoja. Debe aparecer una fila nueva. Además, en la consola de Unity sale `[Estudio] …` con la línea enviada.

**Para analizar:** en la hoja, selecciona la columna `datos` → **Datos → Dividir texto en columnas** → separador **punto y coma**. Pon como cabecera la línea que corresponda:

Filas de **intento** (`tipo = intento`):

```
tipo;participante;fecha_utc;version;parte;nivel;config;orden;intento;resultado;duracion_s;beats_alcanzados;precision_sync;latidos;aciertos;perfect;good;fallos;mejor_racha;desfase_medio_ms;desfase_abs_medio_ms;desfase_sd_ms;energia_gastada_acciones;energia_perdida_fallos;energia_perdida_azul;energia_recuperada;energia_media;energia_min;tiempo_azul_s;entradas_azul;saltos;dashes;slides;wallruns;monedas;mejoras_equipadas;monedas_recogidas;monedas_nivel_total;contratos_activos;contratos_fallados
```

Filas de **cuestionario** (`tipo = cuestionario`):

```
tipo;participante;fecha_utc;version;parte;nivel;config;orden;p1;p2;p3;p4;p5;p6;p7;p8;preferencia
```

Filas de **tienda** (`tipo = tienda`, solo en las partes 4 y 5):

```
tipo;participante;fecha_utc;version;parte;nivel;evento;objeto;precio;monedas_restantes;muescas_usadas;muescas_total;duracion_s
```

Filas de **contrato** (`tipo = contrato`, solo en las partes 4 y 5):

```
tipo;participante;fecha_utc;version;parte;nivel;evento;contrato;dificultad;recompensa;monedas
```

Qué significa cada columna:

| Columna | Significado |
|---|---|
| `parte` | 1 = práctica con tutorial, 2 y 3 = Nivel 1 con cada configuración, 4 = Nivel 2, 5 = Nivel 3 |
| `config` | `Normal`, `Permisiva` (acciones ×0,6 y fallos ×0,5) o `Exigente` (acciones ×1,4 y fallos ×1,6). Valores en `StudyConfig.cs` |
| `orden` | `AB` = jugó primero la permisiva; `BA` = primero la exigente (se sortea) |
| `resultado` | `completado`, `muerte_obstaculo`, `muerte_energia`, `game_over_forzado` o `salida` (salió al menú) |
| `precision_sync` | % de beats en los que iba sincronizado con la música (solo si completa el nivel) |
| `desfase_*_ms` | Desfase del clic respecto al beat: medio (+ = tarde, − = pronto), absoluto medio y desviación típica |
| `energia_*` | En % de la barra: gastada en acciones, perdida por fallos, bonus azul perdido al fallar, recuperada con aciertos; media y mínima (0–100 rojo, 100–200 con bonus azul) |
| `p1…p8` | Respuestas 1–7 del cuestionario corto (preguntas abajo) |
| `monedas`, `mejoras_equipadas` | Monedas en el monedero y mejoras equipadas (separadas por `\|`) al empezar el intento. Solo hay mejoras en las partes 4 y 5 |
| `monedas_recogidas`, `monedas_nivel_total` | Monedas cogidas en el intento y monedas que hay en el nivel (para calcular el % recogido) |
| `contratos_activos`, `contratos_fallados` | Contratos firmados para ese nivel y los que ya eran imposibles al acabar el intento |
| `evento` (contrato) | `firmar`, `rescindir`, `cumplido` (a mitad de nivel o en la meta), `cobrar`; `dificultad` 1–3 |
| `evento` (tienda) | `abrir`, `cerrar` (con `duracion_s` = segundos dentro de la tienda), `compra`, `equipar`, `quitar`, `muesca` |
| `preferencia` | Tras la 2.ª partida del Nivel 1: configuración preferida (`Permisiva` / `Exigente` / `igual`) |

**Copia de seguridad:** cada ordenador guarda también un CSV en `%USERPROFILE%\AppData\LocalLow\<Empresa>\<Juego>\Estudio\<código>.csv`. Si un jugador no tenía internet, pídele ese archivo.

### Cuestionario corto dentro del juego (escala 1–7)

Sale tras cada parte, salvo la práctica. Está adaptado de escalas de *flow* y experiencia de juego (Flow Short Scale, GEQ).

| | Pregunta | Qué mide |
|---|---|---|
| p1 | Me he divertido. | Disfrute |
| p2 | Sentía que controlaba lo que hacía. | Control (*flow*) |
| p3 | Estaba totalmente concentrado/a en el juego. | Concentración (*flow*) |
| p4 | Mis acciones iban al ritmo de la música de forma natural. | Fluidez rítmica |
| p5 | El nivel me ha resultado difícil. | Desafío percibido |
| p6 | La barra de energía me obligaba a estar pendiente del ritmo. | Efecto de la energía |
| p7 | Cuando fallaba al hacer clic, entendía por qué (los fallos me parecían justos). | Efecto de los errores |
| p8 | Me habría gustado seguir jugando. | Motivación |

Los textos se cambian en `StudyHub.Questions`.

---

## 2. CUESTIONARIO FINAL (lo rellena el jugador al terminar)

Crea otro Google Form, por ejemplo **«TFG – cuestionario final»**. En Configuración, **no** recopiles correos.

El formulario se divide en **3 secciones** (botón «Añadir sección»). Quien no haya jugado los niveles 2 y 3 se salta la sección de tienda y contratos y **no le quedan preguntas sin contestar**.

**Sección 1: Sobre ti y el juego**

1. **Código de participante** (respuesta corta, obligatoria). Lo rellena el juego.
2. **Edad** (opción única): 18–24 · 25–34 · 35–44 · 45 o más.
3. **¿Con qué frecuencia juegas a videojuegos?** Nunca · De vez en cuando · Varias veces por semana · A diario.
4. **Experiencia con juegos de ritmo** (Guitar Hero, Beat Saber, osu!…): Ninguna · Poca · Bastante · Mucha.
5. **¿Tocas algún instrumento o tienes formación musical?** Sí · No.
6. **¿Con qué has jugado?** Auriculares con cable · Auriculares Bluetooth · Altavoces.
7. **Escala lineal 1–7** (1 = nada de acuerdo, 7 = totalmente de acuerdo), una pregunta por frase:
   - Hacer clic al ritmo para recuperar energía es una mecánica original.
   - Combinar el movimiento (saltar, deslizarse…) con el ritmo me ha resultado natural.
   - La barra de energía hacía el juego más interesante.
   - El tutorial me explicó bien cómo jugar.
   - Notaba retraso entre mi clic y el sonido.
   - Recomendaría este juego a otra persona.
8. **¿Has jugado los niveles 2 y 3 (con tienda y contratos)?** Opción única, obligatoria: Sí · No.
   En los tres puntos (⋮) de la pregunta elige **«Ir a la sección según la respuesta»**: *Sí → Sección 2*, *No → Sección 3*.

**Sección 2: Tienda, contratos y economía** (solo quien jugó los niveles 2 y 3)

9. **Escala lineal 1–7**, una pregunta por frase:
   - Entendía qué hacía cada mejora de la tienda.
   - Las mejoras que compré cambiaron de forma notable cómo jugaba.
   - Tener un número limitado de muescas me obligó a elegir con cuidado.
   - Las monedas que gané me parecieron suficientes para comprar algo útil.
   - Los precios de la tienda me parecieron razonables.
   - Los contratos (retos) me motivaron a jugar de otra forma.
   - La dificultad de los contratos estaba bien equilibrada con lo que pagaban.
10. **¿Qué mejora de la tienda te pareció más útil o interesante, y por qué?** (párrafo, opcional)

Al final de la sección 2: «Después de la sección 2» → **Ir a la sección 3**.

**Sección 3: Comentarios**

11. **¿Qué es lo que más te ha gustado?** (párrafo)
12. **¿Y lo que menos?** (párrafo)
13. **¿Has encontrado algún fallo o algo que no funcionara bien?** (párrafo)

Luego saca el enlace para el juego:

- **⋮ → Obtener enlace rellenado previamente** → en la pregunta 1 escribe exactamente `CODIGO` → **Obtener enlace** → **Copiar**.
- Pega el enlace completo en **`FinalFormUrl`**. El juego cambia `CODIGO` por el código del jugador.

---

## 3. Exportar el juego y repartirlo

0. **Activa la versión de prueba:** en la escena MainMenu, selecciona **MainMenuManager** y marca **Playtest**. El botón Play pasa a ser «PARTICIPAR EN LA PRUEBA». Guarda la escena. Para tu versión normal, desmárcalo.
1. **Ajustes:** Unity → *Edit → Project Settings → Player*. Pon *Company Name* y *Product Name*, que deciden la carpeta del CSV.
2. **Escenas:** comprueba en *File → Build Profiles* que estén **MainMenu, Level1, Level2 y Level3**, con MainMenu la primera.
3. **Compilar:** *Build Profiles → Windows → Build*, a una carpeta vacía (p. ej. `Build_Estudio`).
4. **Comprimir y compartir:** comprime la carpeta entera en un zip y compártelo por Google Drive con «cualquiera con el enlace».
5. **Mensaje para los participantes:**
   > Descomprime el zip y abre `<NombreDelJuego>.exe`. Si Windows avisa con «Windows protegió su PC», pulsa *Más información → Ejecutar de todas formas*. Usa auriculares con cable. En el menú pulsa **«Participar en la prueba»** y sigue los pasos (~30 min). Al final se abre un cuestionario en el navegador.

**Antes de repartirlo, haz una prueba completa tú.** Comprueba que llegan filas a la hoja y que el enlace final abre el formulario con el código puesto.

---

**Por qué la tienda y los contratos solo están en las partes 4 y 5 (para la memoria):** en las partes 1–3 se comparan las configuraciones de energía. Si cada jugador llevara mejoras distintas, no se sabría si las diferencias se deben a la configuración o a las mejoras. Por eso ahí todos juegan sin tienda. En las partes 4 y 5 se abre la tienda con las monedas ganadas y se registra qué se compra, qué se equipa y cuánto tiempo se pasa en ella. Lo mismo con los contratos: firmas, cumplidos, cobros y retos fallados. Así se evalúan la tienda, las muescas, los contratos y la economía (monedas recogidas frente a monedas disponibles, precios, recompensas) sin contaminar la comparación principal.

## 4. Ética y protección de datos (para la memoria)

- **Consentimiento informado** al empezar: finalidad, duración, datos que se recogen, anonimato, carácter voluntario, mayoría de edad.
- **Datos anónimos:** código aleatorio (`P-XXXXX`). No se recogen nombre, correo, IP en el juego ni datos de salud.
- **Finalidad única:** los datos se usan solo para el TFG. Se pueden borrar al terminar.
- Así se cumple el principio de **minimización de datos del RGPD**: al no ser datos personales identificables, no hace falta tratamiento adicional.

## 5. Seguridad al publicar en itch.io

El enlace del formulario oculto va dentro del juego: cualquiera que lo extraiga podría mandar filas falsas. Para protegerse:

1. **La clave** está en `Assets/Resources/StudyKey.txt` (una sola línea: una frase larga, solo letras, números y guiones). Ese archivo está en el `.gitignore`, así que **no se sube a GitHub**; guarda una copia aparte (si lo pierdes, no podrás verificar las filas). No la publiques.
2. **Validación en Google Forms** (pregunta de párrafo del formulario oculto → ⋮ → *Validación de respuestas* → *Expresión regular* → *Coincide con*):
   ```
   (intento|cuestionario|tienda|contrato|abandono);P-[A-Z2-9]{5};.*;[0-9]+;[0-9a-f]{8}
   ```
   Google rechaza todo lo que no tenga el formato de una fila del juego.
3. **Limpieza al analizar.** Cada fila termina en `fila;verificacion`:
   - `fila`: número de fila dentro de la sesión. Si hay dos filas con el mismo participante y número, es un reenvío: quédate con una.
   - `verificacion`: HMAC-SHA256 (8 primeros caracteres hex) de la fila sin ese último campo, con la clave `RowKey`. Descarta las filas cuya verificación no cuadre:
   ```python
   import hmac, hashlib
   def valida(fila, clave):
       cuerpo, firma = fila.rsplit(";", 1)
       return hmac.new(clave.encode(), cuerpo.encode(), hashlib.sha256).hexdigest()[:8] == firma
   ```
4. El juego **solo envía por HTTPS a docs.google.com**, no lee ni interpreta las respuestas, no ejecuta nada descargado y solo envía datos tras aceptar el consentimiento.
5. **Si publicas la versión web (WebGL)**: Google Forms no permite leer la respuesta desde el navegador (CORS). Los datos llegan igualmente, pero el juego no reintenta en la web para no duplicar filas (si alguna se repite, se quita con la columna `fila`). La versión descargable (Windows) no tiene este problema.
6. Exporta con **Development Build desactivado**.
