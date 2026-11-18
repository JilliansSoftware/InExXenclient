// The resize corner of the shard's resizable windows (journal, skills, shop, spell bar).
// The grip art is a few thin diagonal lines, and gump art only answers the mouse where it has
// pixels, so most clicks on the corner fell through to the window and dragged it. This grip
// takes the whole corner square (size px; smaller where content runs close to the corner),
// draws the art in its bottom-right corner, and lights up under the mouse so it's clear it
// can be grabbed.

using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.IEX
{
    internal sealed class IexResizeGrip : Control
    {
        static readonly Color Glow = new Color(232, 192, 96);

        /// <summary>A grip in the bottom-right corner of a window of this size.</summary>
        public IexResizeGrip(int windowWidth, int windowHeight, int size = 18)
        {
            X = windowWidth - size;
            Y = windowHeight - size;
            Width = size;
            Height = size;
            AcceptMouseInput = true;
            CanMove = false;
            WantUpdateSize = false;
        }

        public override bool Contains(int x, int y) => true; // the whole square, not just the art's pixels

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;
            bool hot = MouseIsOver;
            ref readonly var art = ref Client.Game.UO.Gumps.GetGump(IexDarkArt.ResizeGrip);
            var texture = art.Texture;
            var uv = art.UV;
            int w = Width, h = Height;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                if (hot)
                {
                    // a soft gold wash in the corner
                    batcher.Draw(SolidColorTextureCache.GetTexture(Glow), new Rectangle(x + w - System.Math.Min(w, 13), y + h - System.Math.Min(h, 13), System.Math.Min(w, 13) - 2, System.Math.Min(h, 13) - 2), ShaderHueTranslator.GetHueVector(0, false, 0.18f), layerDepth);
                }

                if (texture != null)
                {
                    batcher.Draw(texture, new Rectangle(x + w - System.Math.Min(3, w - uv.Width) - uv.Width, y + h - System.Math.Min(3, h - uv.Height) - uv.Height, uv.Width, uv.Height), uv,
                        ShaderHueTranslator.GetHueVector(0, false, hot ? 1f : 0.85f), layerDepth);
                }
                else
                {
                    // no themed art: a small triangle of dots
                    var dot = SolidColorTextureCache.GetTexture(Glow);

                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col <= row; col++)
                        {
                            batcher.Draw(dot, new Rectangle(x + w - 5 - col * 3, y + h - 5 - (row - col) * 3, 2, 2), ShaderHueTranslator.GetHueVector(0, false, hot ? 1f : 0.6f), layerDepth);
                        }
                    }
                }

                return true;
            });

            return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
        }
    }
}
