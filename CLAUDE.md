# Crafting Queue — instrucciones para Claude Code

Mod de BepInEx 5 para Graveyard Keeper 2. **Lee `docs/HANDOFF.md` antes de empezar**: tiene el estado actual,
cómo compilar/instalar/publicar, el mapa del código y lo que falta probar.

Reglas rápidas:
- Responde al usuario en **español**. Textos para jugadores (README, notas, Nexus) en inglés y español.
- El número de versión **solo sube al publicar**, no en cada compilación de prueba. Misma versión en GitHub y Nexus.
- Git con identidad `VERTO13 <13241922+VERTO13@users.noreply.github.com>`. Nunca datos personales, claves ni tokens:
  el repo es público.
- Confirma antes de cualquier acción pública o irreversible (releases, publicar en Nexus, mensajes, borrar).
- El juego bloquea las DLL mientras está abierto: pide cerrarlo antes de instalar.
- Pixel art nítido y sin espacio desperdiciado: el usuario es muy exigente con el diseño.
- No cambies secciones/claves del `.cfg` (están en español); cambiarlas borra los ajustes de los jugadores.
