// Locked windows and the hidden interface.
// Ctrl+Alt+click a window to lock it in place (or unlock it): a locked window can't be dragged,
// closed with right-click or resized; a padlock shows on it while Ctrl+Alt is held. The lock is
// saved with the layout (Gump.Save / Profile.ReadGumps).
// The "ToggleInterface" macro hides every window, bar and the chat (names and health lines over
// heads stay); press it again to bring them back. Windows that open while hidden still show.

using System.Collections.Generic;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal static class IexWindowLock
    {
        const ushort Padlock = 0x082C; // the stock anchor lock

        // ------------------------------------------------------------------ locking

        static Gump Root(Control c)
        {
            while (c?.Parent != null)
            {
                c = c.Parent;
            }

            return c as Gump;
        }

        /// <summary>True when the control's window is locked.</summary>
        public static bool Locked(Control c) => Root(c)?.IexLocked ?? false;

        static bool Lockable(Gump g) =>
            g != null
            && g is not WorldViewportGump
            && g is not NameOverheadGump
            && !g.IsModal
            && !(g is UseSpellButtonGump && (ProfileManager.CurrentProfile?.FastSpellsAssign ?? false)); // Ctrl+Alt+click edits its macro

        /// <summary>A mouse button went down over the interface. True when it locked or unlocked a window.</summary>
        public static bool OnMouseDown(Control over, MouseButtonType button)
        {
            if (button != MouseButtonType.Left || !Keyboard.Ctrl || !Keyboard.Alt)
            {
                return false;
            }

            Gump g = Root(over);

            if (!Lockable(g))
            {
                return false;
            }

            g.IexLocked = !g.IexLocked;
            return true;
        }

        public static void Save(Gump g, XmlTextWriter writer)
        {
            if (g.IexLocked)
            {
                writer.WriteAttributeString("iexlock", "1");
            }
        }

        public static void Restore(Gump g, XmlElement xml)
        {
            if (g != null && xml.GetAttribute("iexlock") == "1")
            {
                g.IexLocked = true;
            }
        }

        /// <summary>After a window is drawn (UIManager.Draw): its padlock while Ctrl+Alt is held.</summary>
        public static void AddPadlock(RenderLists renderLists, Gump g, float layerDepth)
        {
            if (!g.IexLocked || !g.IsVisible || g.IsDisposed || !Keyboard.Ctrl || !Keyboard.Alt)
            {
                return;
            }

            ref readonly var info = ref Client.Game.UO.Gumps.GetGump(Padlock);

            if (info.Texture == null)
            {
                return;
            }

            var texture = info.Texture;
            Rectangle uv = info.UV;
            int x = g.X + g.Width - uv.Width - 2, y = g.Y + 2;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0);

            renderLists.AddGumpWithAtlas(batcher =>
            {
                batcher.Draw(texture, new Vector2(x, y), uv, hue, layerDepth + 1);
                return true;
            });
        }

        // ------------------------------------------------------------------ hiding

        static readonly List<Control> _hidden = new List<Control>();

        public static bool Hidden { get; private set; }

        public static void ToggleInterface()
        {
            if (Hidden && _hidden.TrueForAll(c => c.IsDisposed))
            {
                Hidden = false; // logged out while hidden: those windows are gone
                _hidden.Clear();
            }

            if (!Hidden)
            {
                Hidden = true;
                _hidden.Clear();

                foreach (Gump g in UIManager.Gumps)
                {
                    if (g.IsVisible && !g.IsDisposed && g is not WorldViewportGump && g is not NameOverheadGump && !g.IsModal)
                    {
                        g.IsVisible = false;
                        _hidden.Add(g);
                    }
                }

                if (UIManager.SystemChat is { IsVisible: true } chat)
                {
                    chat.IsVisible = false; // it keeps the keyboard, so this key still works
                    _hidden.Add(chat);
                }
            }
            else
            {
                Hidden = false;

                foreach (Control c in _hidden)
                {
                    if (!c.IsDisposed)
                    {
                        c.IsVisible = true;
                    }
                }

                _hidden.Clear();
            }
        }
    }
}
