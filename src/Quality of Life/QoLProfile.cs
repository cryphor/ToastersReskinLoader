// QoLProfile — the persisted slice of the QoL feature surface, stored
// as a nested object inside Toaster's ReskinProfile.json.
//
// Trimmed to match the surviving QoLConfig (arena disable extras,
// goalie wide-view camera, dev console, debug logging).

using Newtonsoft.Json;

namespace ToasterReskinLoader.QoL
{
    public class QoLProfile
    {
        // Arena visuals
        [JsonProperty("disableArenaVisuals")]
        public bool DisableArenaVisuals { get; set; } = false;
        [JsonProperty("disableArenaProps")]
        public bool DisableArenaProps { get; set; } = false;
        [JsonProperty("disableArenaLights")]
        public bool DisableArenaLights { get; set; } = false;
        [JsonProperty("disableArenaSkybox")]
        public bool DisableArenaSkybox { get; set; } = false;
        [JsonProperty("disableArenaParticles")]
        public bool DisableArenaParticles { get; set; } = false;
        [JsonProperty("arenaAudioVolume")]
        public float ArenaAudioVolume { get; set; } = 0.9f;

        // Base-game UX patches (default on)
        [JsonProperty("enableEscCloseMenus")]
        public bool EnableEscCloseMenus { get; set; } = true;
        [JsonProperty("enableChatAnyPhase")]
        public bool EnableChatAnyPhase { get; set; } = true;
        [JsonProperty("enableChatDragSelect")]
        public bool EnableChatDragSelect { get; set; } = true;
        [JsonProperty("enableInlineServerBrowserFilters")]
        public bool EnableInlineServerBrowserFilters { get; set; } = true;

        // Debug + dev console
        [JsonProperty("enableDebugLogging")]
        public bool EnableDebugLogging { get; set; } = false;
        [JsonProperty("enableDevConsole")]
        public bool EnableDevConsole { get; set; } = false;
        [JsonProperty("devConsoleX")]
        public float DevConsoleX { get; set; } = 40f;
        [JsonProperty("devConsoleY")]
        public float DevConsoleY { get; set; } = 40f;
        [JsonProperty("devConsoleW")]
        public float DevConsoleW { get; set; } = 900f;
        [JsonProperty("devConsoleH")]
        public float DevConsoleH { get; set; } = 460f;

        public ToasterReskinLoader.QoL.QoLConfig ToConfig()
        {
            return new ToasterReskinLoader.QoL.QoLConfig
            {
                disableArenaVisuals = DisableArenaVisuals,
                disableArenaProps = DisableArenaProps,
                disableArenaLights = DisableArenaLights,
                disableArenaSkybox = DisableArenaSkybox,
                disableArenaParticles = DisableArenaParticles,
                arenaAudioVolume = ArenaAudioVolume,
                enableEscCloseMenus = EnableEscCloseMenus,
                enableChatAnyPhase = EnableChatAnyPhase,
                enableChatDragSelect = EnableChatDragSelect,
                enableInlineServerBrowserFilters = EnableInlineServerBrowserFilters,
                enableDebugLogging = EnableDebugLogging,
                enableDevConsole = EnableDevConsole,
                devConsoleX = DevConsoleX,
                devConsoleY = DevConsoleY,
                devConsoleW = DevConsoleW,
                devConsoleH = DevConsoleH,
            };
        }

        public void FromConfig(ToasterReskinLoader.QoL.QoLConfig c)
        {
            if (c == null) return;
            DisableArenaVisuals = c.disableArenaVisuals;
            DisableArenaProps = c.disableArenaProps;
            DisableArenaLights = c.disableArenaLights;
            DisableArenaSkybox = c.disableArenaSkybox;
            DisableArenaParticles = c.disableArenaParticles;
            DisableArenaParticles = c.disableArenaParticles;
            ArenaAudioVolume = c.arenaAudioVolume;
            EnableEscCloseMenus = c.enableEscCloseMenus;
            EnableChatAnyPhase = c.enableChatAnyPhase;
            EnableChatDragSelect = c.enableChatDragSelect;
            EnableInlineServerBrowserFilters = c.enableInlineServerBrowserFilters;
            EnableDebugLogging = c.enableDebugLogging;
            EnableDevConsole = c.enableDevConsole;
            DevConsoleX = c.devConsoleX;
            DevConsoleY = c.devConsoleY;
            DevConsoleW = c.devConsoleW;
            DevConsoleH = c.devConsoleH;
        }
    }
}
