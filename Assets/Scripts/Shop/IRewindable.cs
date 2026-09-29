/// <summary>
/// Objetos del nivel que dependen del reloj de la canción (osciladores, bloques que caen, bolas de fuego, pinchos…)
/// y que tienen que VOLVER ATRÁS con el Rebobinado.
///
/// TimeRewinder llama a OnTimeRewind() en cada frame de la animación de rebobinado (con el reloj del BeatManager
/// apuntando al instante que se está mostrando) y una vez más al retomar la canción. En ese momento
/// BeatManager.ScheduledStartDSP ya representa el nuevo tiempo, así que cada objeto solo tiene que recalcular sus
/// instantes DSP y colocarse en el estado que le toca AHORA (sin disparar sonidos de cosas que ya pasaron).
/// Tiene que ser idempotente: se llama muchas veces seguidas.
/// </summary>
public interface IRewindable
{
    void OnTimeRewind();
}
