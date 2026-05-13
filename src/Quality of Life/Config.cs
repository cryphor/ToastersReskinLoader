// Trimmed QoLConfig — kept just the fields used by the surviving
// QoL features (goalie wide-view camera, arena visual disable, dev console,
// debug logging). The bigger PoncePlayerInput config surface (keybinds,
// position overrides, chat/tag, mute/social, sounds, etc.) was removed when
// the scope was scaled back.

using System;
using System.Collections.Generic;

namespace ToasterReskinLoader.QoL
{
    [Serializable]
    public class QoLConfig
    {
        // Arena visuals
        public bool disableArenaVisuals = false;
        public bool disableArenaProps = false;
        public bool disableArenaLights = false;
        public bool disableArenaSkybox = false;
        public bool disableArenaParticles = false;
        public float arenaAudioVolume = 0.9f;

        // Base-game UX patches (default on)
        public bool enableEscCloseMenus = true;
        public bool enableChatAnyPhase = true;
        public bool enableChatDragSelect = true;
        public bool enableInlineServerBrowserFilters = true;

        // Debug + dev console
        public bool enableDebugLogging = false;
        public bool enableDevConsole = false;
        // Persisted dev console window position/size
        public float devConsoleX = 40f;
        public float devConsoleY = 40f;
        public float devConsoleW = 900f;
        public float devConsoleH = 460f;

    }
}
