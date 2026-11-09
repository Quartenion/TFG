# INF-HARTO

INF-HARTO es un juego rítmico en primera persona desarrollado como Trabajo Fin de Grado del Grado en Ingeniería Informática de la Universidad de Granada.

**Descarga (Windows y Linux):** https://quartenion.itch.io/inf-harto

## El juego

Después de un accidente en las minas de una prisión, un minero se queda atrapado bajo los escombros con su compañero, que muere en el derrumbe. Desesperado, empieza a rezar, y un ente que dice ser Dios se le aparece. Le ofrece un trato: «Te abriré un camino hasta donde tu amigo descansa. Lleva su corazón, todavía vivo, hasta el recipiente que te espera al final, y volverá a latir. Allí, os volveréis a encontrar». Para que el corazón no se necrose por el camino, tienes que llevarlo en la mano y hacerlo latir al RITMO de la música. Si pierdes el ritmo, morís los dos.

Cada latido a tiempo genera energía, y la gastas al hacer acciones como saltar, deslizarte o hacer un dash. La jeringuilla roja drena lentamente la sangre del corazón: si se llena del todo, es GAME OVER. Si aciertas suficientes latidos, llenas la jeringuilla azul y consigues energía extra. Toda la música es original y se compuso específicamente para el juego, que tiene tres niveles, cada uno con su propia canción y su propio tempo, y en el nivel 3, un jefe final.

## Controles

- **Clic izquierdo / derecho:** latido (al ritmo). En el kayak: remar hacia la derecha / izquierda
- **W A S D:** moverse
- **Espacio:** saltar (otra vez en el aire: doble salto)
- **Shift:** deslizarse
- **A / D + E:** dash hacia ese lado
- **S (en el aire):** caer en picado
- **Q:** romper puertas y bloquear ataques
- **F:** engancharse a la tirolina
- **E:** interactuar (puertas, tienda, contratos)
- **TAB:** ver los controles
- **Esc / P:** pausa

Las teclas se pueden cambiar en Ajustes → Controles.

**Requisitos:** Windows 10/11 (64 bits) o Linux (64 bits), un ratón con clic izquierdo y derecho (no se puede jugar con el touchpad) y auriculares con cable (los Bluetooth retrasan el sonido).

## Cómo abrir el proyecto

1. Instala **Unity 6 (6000.4.4f1)** desde Unity Hub y **Git LFS** (`git lfs install`).
2. Clona el repositorio:
   ```bash
   git clone https://github.com/Quartenion/TFG.git
   ```
3. Abre la carpeta desde Unity Hub.
4. Abre la escena `Assets/Scenes/MainMenu.unity` y pulsa **Play**.

Escenas del juego: `MainMenu`, `Tutorial`, `Level1`, `Level2` y `Level3`.

### Recursos que no están en el repositorio

Algunos recursos de terceros que usa el juego no se pueden redistribuir en un repositorio público (cielos y modelos de la Unity Asset Store, el shader de lava, el tridente y varios efectos de sonido). Por eso, al abrir el proyecto, algunos materiales, modelos y sonidos aparecerán vacíos o en rosa. La lista completa, con el origen de cada uno para descargarlo, está en el apéndice D de la memoria. El juego publicado en itch.io sí los incluye.

### Estudio con jugadores

El juego incluye un modo PRUEBA que se usó para el estudio del TFG y que envía datos anónimos de la partida a un formulario. La clave con la que se firman esos datos (`Assets/Resources/StudyKey.txt`) no está incluida en el repositorio, por lo que desde el editor el juego funciona con normalidad, pero no puede enviar datos.

## Estructura

```
Assets/
├── Scripts/
│   ├── Rhythm/      <- reloj musical (BeatManager) y evaluación de los clics
│   ├── Player/      <- movimiento, acciones y energía
│   ├── Level/       <- obstáculos y mecánicas de cada nivel
│   ├── Boss/        <- jefe final
│   ├── Shop/        <- tienda de mejoras
│   ├── Contracts/   <- contratos opcionales
│   ├── Tutorial/    <- tutorial guiado
│   ├── Study/       <- modo prueba y telemetría del estudio
│   ├── UI/          <- HUD y menús
│   ├── GameFlow/    <- flujo de cada intento y guardado
│   ├── Settings/    <- ajustes
│   ├── Rendering/   <- efectos visuales
│   └── Editor/      <- herramientas para diseñar los niveles
├── Scenes/
├── Songs/           <- música original del juego
└── ...
```

## Créditos

Programación, diseño, modelos 3D y música: Fernando Lanzarot Facio.

Recursos de terceros:

- «Heartbeat loop» de .name (Freesound, 418866), con licencia CC BY 4.0: https://freesound.org/s/418866/
- Free Pack – Rocks Stylized de PolyOne Studio (Sketchfab), con licencia CC BY.
- Fuentes Metal Mania (Open Window) y Pirata One (Rodrigo Fuenzalida, Nicolás Massi), con licencia SIL Open Font License 1.1.
- Fuente Aftermath BRK de Brian Kent (freeware).
- El resto de efectos de sonido proceden de Pixabay, Freesound (CC0) y SoundJay, cuyas licencias no exigen atribución.

## Licencia

El código fuente se publica bajo la licencia MIT. La música, los modelos 3D y el resto del contenido creado para el juego son propiedad del autor, con todos los derechos reservados. Los recursos de terceros mantienen su propia licencia. Más detalles en [LICENSE](LICENSE).
