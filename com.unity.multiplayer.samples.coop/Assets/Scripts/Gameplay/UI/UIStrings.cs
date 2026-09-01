using System;
using System.Collections.Generic;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Spanish for the interface text this project inherited from the sample it was built on.
    /// <see cref="ToonMenuRestyler"/> swaps these in as it walks the canvases.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the game was half-translated.</b> Everything this project wrote itself — the
    /// kill feed, the phase announcements, the pause menu — is Spanish. Everything that came with
    /// the sample — the lobby, character select, the settings window — is English, and it lives in
    /// prefabs this project cannot reliably edit (the Editor serves its own cached copy of an
    /// asset, which has cost a build cycle here before). So a player logged in in Spanish and
    /// picked a hero in English.</para>
    ///
    /// <para><b>Why exact matches only.</b> A room name, a player name and a score are also
    /// strings passing through the same pass. Matching whole labels and nothing else means a
    /// player called "Ready" keeps their name, and any label not in this table is simply left
    /// alone — which is the failure this pass should have.</para>
    ///
    /// <para><b>Adding to it.</b> Key on exactly what the prefab says, trimmed. Rich-text markup
    /// around the label does not matter — <see cref="TryTranslate"/> strips tags before it looks,
    /// and puts them back afterwards.</para>
    /// </remarks>
    public static class UIStrings
    {
        static readonly Dictionary<string, string> s_Table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Lobby: creating, browsing and joining rooms.
            { "Create", "Crear" },
            { "Join", "Unirse" },
            { "Quick Join", "Partida rápida" },
            { "Create Room", "Crear sala" },
            { "Find & Join", "Buscar sala" },
            { "Join Selected", "Unirse a la sala" },
            { "Session List", "Salas públicas" },
            { "Public Rooms", "Salas públicas" },
            { "no sessions", "No hay salas abiertas" },
            { "Create a new Session...", "Crear una sala nueva..." },
            { "Join an existing Session...", "Unirse a una sala existente..." },
            { "Your Name:", "Tu nombre:" },
            { "Displayed Name", "Nombre visible" },
            { "Private", "Privada" },
            // "Room" is deliberately absent: it is also the default name a room gets, and a room
            // someone actually called "Room" would be renamed under them in the list.
            { "Refresh", "Actualizar" },
            { "Session code", "Código de sala" },
            { "Password", "Contraseña" },
            { "You will host the match", "Vas a hospedar la partida" },
            { "Browse public rooms", "Explora las salas públicas" },

            // Direct-IP popup.
            { "Host", "Crear" },
            { "Host with IP", "Crear con IP" },
            { "Join with IP", "Unirse con IP" },
            { "Input the Host IP", "IP del anfitrión" },
            { "Input listen IP", "IP de escucha" },
            { "Create a new host...", "Crear un anfitrión nuevo..." },
            { "Join an existing host...", "Unirse a un anfitrión..." },
            { "Connecting...", "Conectando..." },

            // Profiles.
            { "Create New Profile", "Crear perfil" },
            { "Profile Name", "Nombre del perfil" },
            { "Select a profile or create a new one", "Elige un perfil o crea uno nuevo" },
            { "No profiles found.", "No hay perfiles." },

            // Character select.
            { "Choose your hero!", "¡Elige tu héroe!" },
            { "Choose your class:", "Elige tu clase:" },
            { "Get Ready!", "¡Prepárate!" },
            { "READY!", "¡LISTO!" },
            { "Welcome!", "¡Bienvenido!" },
            { "Waiting for other players...", "Esperando a los demás jugadores..." },
            { "Seating players...", "Ubicando jugadores..." },
            { "Copy to clipboard", "Copiar al portapapeles" },
            { "Join Code:", "Código de sala:" },
            { "An Error occurred!", "¡Ocurrió un error!" },

            // Loading, results and settings.
            { "Loading...", "Cargando..." },
            { "YOU WON!", "¡GANASTE!" },
            { "TRY AGAIN", "REINTENTAR" },
            { "Settings", "Ajustes" },
            { "Overall Volume", "Volumen general" },
            { "Music Volume", "Música" },
            { "Return to Menu?", "¿Volver al menú?" },
            { "Quit", "Salir" },
            { "Cancel", "Cancelar" },
            { "Yes", "Sí" },
            { "No", "No" },
            { "OK", "Aceptar" },
        };

        /// <summary>
        /// The Spanish for <paramref name="source"/>, or false if this is not a string the table
        /// knows — a player's name, a room's name, a score.
        /// </summary>
        /// <remarks>
        /// Leading and trailing rich-text markup is peeled off before the lookup and put back
        /// after, so a label the menus wrapped in <c>&lt;b&gt;</c> still matches and still comes
        /// back bold.
        /// </remarks>
        public static bool TryTranslate(string source, out string translated)
        {
            translated = null;

            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            string trimmed = source.Trim();
            string prefix = string.Empty;
            string suffix = string.Empty;

            // Peel whole tags off either end. Anything with markup in the middle is a composed
            // string, not a label, and is left alone by the lookup that follows.
            while (trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.IndexOf('>') > 0)
            {
                int close = trimmed.IndexOf('>');
                prefix += trimmed.Substring(0, close + 1);
                trimmed = trimmed.Substring(close + 1).TrimStart();
            }

            while (trimmed.EndsWith(">", StringComparison.Ordinal) && trimmed.LastIndexOf('<') >= 0)
            {
                int open = trimmed.LastIndexOf('<');
                suffix = trimmed.Substring(open) + suffix;
                trimmed = trimmed.Substring(0, open).TrimEnd();
            }

            if (!s_Table.TryGetValue(trimmed, out string value))
            {
                return false;
            }

            translated = prefix + value + suffix;

            return !string.Equals(translated, source, StringComparison.Ordinal);
        }
    }
}
