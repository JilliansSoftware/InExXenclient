// Defaults for new characters: the old-client hotkeys (the IMY macros.txt) and an old-client
// feel with a few conveniences. Existing profiles are never changed, and a profile a player
// saves with "Set as default" (Data/Profiles/default.json) still wins over these.

using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Microsoft.Xna.Framework;
using SDL3;
using K = SDL3.SDL.SDL_Keycode;

namespace ClassicUO.IEX
{
    internal static class IexDefaults
    {
        public static Profile NewProfile()
        {
            return new Profile
            {
                // Old client (uo.cfg): footsteps, hold Tab for combat, incoming names,
                // colored lighting, no dark nights and no always-run are already ClassicUO's defaults.
                NoColorObjectsOutOfRange = true, // ColorOutOfRange=on: gray out what is out of range

                // Conveniences chosen for the shard.
                UseCircleOfTransparency = true,
                CircleOfTransparencyType = 1, // like the original client
                GameWindowSize = new Point(800, 600), // old client: 640x480; fits small laptop screens
                ShowJournalSystem = true,
                ShowJournalObjects = true,
                ShowJournalClient = true,
                IexDarkUi = true, // the shard's black stone and gold windows
                IexEnhancedLighting = true, // tinted nights, warm fire light, glow
                IexEnhancedWeather = true, // rain streaks and splashes, storms, water glints
            };
        }

        public static void CreateMacros(MacroManager macros)
        {
            Open(macros, "Status", K.SDLK_S, alt: true, sub: MacroSubType.Status);
            Add(macros, "War/Peace", K.SDLK_C, alt: true, MacroType.WarPeace);
            Open(macros, "Paperdoll", K.SDLK_P, alt: true, sub: MacroSubType.Paperdoll);
            Open(macros, "Skills", K.SDLK_I, alt: true, sub: MacroSubType.Skills);
            Open(macros, "Journal", K.SDLK_J, alt: true, sub: MacroSubType.Journal);
            Open(macros, "Backpack", K.SDLK_B, alt: true, sub: MacroSubType.Backpack);
            Open(macros, "Overview", K.SDLK_R, alt: true, sub: MacroSubType.Overview);
            Open(macros, "Options", K.SDLK_O, alt: true, sub: MacroSubType.Configuration);
            Open(macros, "Spellbook", K.SDLK_S, ctrl: true, sub: MacroSubType.MageSpellbook);
            Add(macros, "All Names", K.SDLK_N, alt: true, MacroType.AllNames);
            Add(macros, "Last Object", K.SDLK_M, alt: true, MacroType.LastObject);
            Text(macros, "Guards", K.SDLK_G, MacroType.Yell, "Guards!", ctrl: true, alt: true);
            Add(macros, "Last Skill", K.SDLK_L, alt: true, MacroType.LastSkill, shift: true);
            Skill(macros, "Animal Taming", K.SDLK_A, MacroSubType.AnimalTaming, alt: true);
            Skill(macros, "Hiding", K.SDLK_H, MacroSubType.Hiding, alt: true);
            Skill(macros, "Meditation", K.SDLK_M, MacroSubType.Meditation, ctrl: true);
            Add(macros, "Last Spell", K.SDLK_L, alt: true, MacroType.LastSpell, ctrl: true);
            Text(macros, "All Follow Me", K.SDLK_F9, MacroType.Say, "all follow me");
            Text(macros, "All Kill", K.SDLK_F10, MacroType.Say, "all kill");
            Text(macros, "Lock Down", K.SDLK_F9, MacroType.Say, "i wish to lock this down", alt: true);
            Text(macros, "Release", K.SDLK_F10, MacroType.Say, "i wish to release this", alt: true);
        }

        static void Open(MacroManager m, string name, K key, MacroSubType sub, bool alt = false, bool ctrl = false)
        {
            m.PushToBack(new Macro(name, key, alt, ctrl, false) { Items = new MacroObject(MacroType.Open, sub) });
        }

        static void Add(MacroManager m, string name, K key, bool alt, MacroType type, bool ctrl = false, bool shift = false)
        {
            m.PushToBack(new Macro(name, key, alt, ctrl, shift) { Items = new MacroObject(type, MacroSubType.MSC_NONE) });
        }

        static void Skill(MacroManager m, string name, K key, MacroSubType skill, bool alt = false, bool ctrl = false)
        {
            m.PushToBack(new Macro(name, key, alt, ctrl, false) { Items = new MacroObject(MacroType.UseSkill, skill) });
        }

        static void Text(MacroManager m, string name, K key, MacroType type, string text, bool ctrl = false, bool alt = false)
        {
            m.PushToBack(new Macro(name, key, alt, ctrl, false) { Items = new MacroObjectString(type, MacroSubType.MSC_NONE, text) });
        }
    }
}
