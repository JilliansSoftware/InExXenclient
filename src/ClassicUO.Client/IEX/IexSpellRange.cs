// While a spell's target cursor is up: a ring on the ground showing how far it reaches, and for
// area and field spells the tiles it will hit, under the cursor (grey when out of reach).
// The numbers are the shard's (ModernUO, T2A rules): ITargetingSpell 10 tiles, fields and
// Wall of Stone 15, Night Sight 12, Resurrection 1; areas are squares (GetMobilesInRange).

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.IEX
{
    internal static class IexSpellRange
    {
        const uint CastWindow = 8000; // a target cursor this soon after casting belongs to the spell (ms)

        static readonly Color RingColor = new Color(235, 200, 110);
        static readonly Color HarmColor = new Color(230, 70, 50);
        static readonly Color HelpColor = new Color(80, 200, 110);
        static readonly Color PlainColor = new Color(120, 170, 235);
        static readonly Color FarColor = new Color(140, 140, 140);

        static int _castId;
        static uint _castAt;
        static int _activeId; // the spell whose target cursor is up
        static bool _wasTargeting;
        static readonly Dictionary<uint, Texture2D> _diamonds = new Dictionary<uint, Texture2D>();

        public static bool On => ProfileManager.CurrentProfile?.IexSpellRange ?? false;

        /// <summary>Called when a spell is cast (GameActions.CastSpell / CastSpellFromBook).</summary>
        public static void Casting(int id)
        {
            _castId = id;
            _castAt = Time.Ticks;
        }

        // ------------------------------------------------------------------ the shard's numbers

        public static int Range(int id) => id switch
        {
            24 or 28 or 34 or 39 or 47 or 50 => 15, // Wall of Stone, the fields, Dispel Field
            59 => 1, // Resurrection
            IexSpellbook.NightSightIndex => 12,
            _ => 10
        };

        // Area spells: the square around the target they hit (-1: none).
        static int Area(World world, int id) => id switch
        {
            25 => 2, // Arch Cure
            26 => 3, // Arch Protection
            46 => 2, // Mass Curse
            48 => 1 + (int)(world.Player.Skills[25].Value / 20f), // Reveal (Magery)
            49 => 2, // Chain Lightning
            54 => 8, // Mass Dispel
            55 => 2, // Meteor Swarm
            _ => -1
        };

        // Field spells: half the length of the wall across your line of sight (0: none).
        static int Field(int id) => id switch
        {
            24 => 1, // Wall of Stone
            28 or 39 or 47 or 50 => 2, // Fire, Poison, Paralyze, Energy Field
            _ => 0
        };

        // ------------------------------------------------------------------ drawing

        /// <summary>Called in the world pass (GameScene.DrawWorld).</summary>
        public static void Draw(UltimaBatcher2D batcher, World world, int offsetX, int offsetY)
        {
            TargetManager tm = world.TargetManager;
            bool targeting = tm.IsTargeting && (tm.TargetingState == CursorTarget.Object || tm.TargetingState == CursorTarget.Position);

            if (targeting && !_wasTargeting)
            {
                _activeId = _castId != 0 && Time.Ticks - _castAt < CastWindow ? _castId : 0;
                _castId = 0; // later cursors aren't this spell's
            }
            else if (!targeting)
            {
                _activeId = 0;
            }

            _wasTargeting = targeting;

            if (_activeId == 0 || !On || world.Player == null || world.Map == null)
            {
                return;
            }

            PlayerMobile player = world.Player;
            int range = Range(_activeId);
            Texture2D line = SolidColorTextureCache.GetTexture(RingColor);
            float depth = player.CalculateDepthZ() + 0.5f;

            // The ring: the outer edges of the square of tiles in reach.
            for (int d = -range; d <= range; d++)
            {
                Edge(batcher, world, line, player.X + range, player.Y + d, 0, offsetX, offsetY, depth, 0.75f);
                Edge(batcher, world, line, player.X - range, player.Y + d, 1, offsetX, offsetY, depth, 0.75f);
                Edge(batcher, world, line, player.X + d, player.Y + range, 2, offsetX, offsetY, depth, 0.75f);
                Edge(batcher, world, line, player.X + d, player.Y - range, 3, offsetX, offsetY, depth, 0.75f);
            }

            // What it hits, under the cursor.
            int area = Area(world, _activeId), field = Field(_activeId);

            if (area < 0 && field == 0 || !UIManager.IsMouseOverWorld || SelectedObject.Object is not GameObject target)
            {
                return;
            }

            int tx = target.X, ty = target.Y;
            bool far = Math.Max(Math.Abs(tx - player.X), Math.Abs(ty - player.Y)) > range;
            SpellDefinition def = _activeId == IexSpellbook.NightSightIndex ? IexSpellbook.NightSight : SpellDefinition.FullIndexGetSpell(_activeId);
            Color color = far ? FarColor
                : def?.TargetType == TargetType.Harmful ? HarmColor
                : def?.TargetType == TargetType.Beneficial ? HelpColor
                : PlainColor;

            Texture2D fill = Diamond(color);
            Texture2D edge = SolidColorTextureCache.GetTexture(color);
            Vector3 fillHue = ShaderHueTranslator.GetHueVector(0, false, 0.3f);

            if (area >= 0)
            {
                for (int x = tx - area; x <= tx + area; x++)
                {
                    for (int y = ty - area; y <= ty + area; y++)
                    {
                        Tile(batcher, world, fill, fillHue, x, y, offsetX, offsetY);
                    }
                }

                for (int d = -area; d <= area; d++)
                {
                    Edge(batcher, world, edge, tx + area, ty + d, 0, offsetX, offsetY, depth, 0.9f);
                    Edge(batcher, world, edge, tx - area, ty + d, 1, offsetX, offsetY, depth, 0.9f);
                    Edge(batcher, world, edge, tx + d, ty + area, 2, offsetX, offsetY, depth, 0.9f);
                    Edge(batcher, world, edge, tx + d, ty - area, 3, offsetX, offsetY, depth, 0.9f);
                }
            }
            else
            {
                // A wall across the line from you to the target (SpellHelper.GetEastToWest).
                int dx = player.X - tx, dy = player.Y - ty;
                int rx = (dx - dy) * 44, ry = (dx + dy) * 44;
                bool eastToWest = rx >= 0 && ry < 0 || ry >= 0 && rx < 0;

                for (int i = -field; i <= field; i++)
                {
                    int x = eastToWest ? tx + i : tx, y = eastToWest ? ty : ty + i;
                    Tile(batcher, world, fill, fillHue, x, y, offsetX, offsetY);

                    for (int side = 0; side < 4; side++)
                    {
                        Edge(batcher, world, edge, x, y, side, offsetX, offsetY, depth, 0.9f);
                    }
                }
            }
        }

        // The ground height at a tile (the land's average, as it is drawn).
        static int GroundZ(World world, int x, int y, out float depth)
        {
            for (GameObject o = world.Map.GetTile(x, y, false); o != null; o = o.TNext)
            {
                if (o is Land land)
                {
                    depth = land.CalculateDepthZ() + 0.6f;
                    return land.IsStretched ? land.AverageZ : land.Z;
                }
            }

            depth = -1;
            return world.Player.Z;
        }

        // The centre of a tile's diamond on screen (world pass coordinates).
        static Vector2 Centre(int x, int y, int z, int offsetX, int offsetY) =>
            new Vector2((x - y) * 22 - offsetX, (x + y) * 22 - (z << 2) - offsetY);

        // One side of a tile's diamond: 0 = +X (right-bottom), 1 = -X (left-top), 2 = +Y (left-bottom), 3 = -Y (top-right).
        static void Edge(UltimaBatcher2D batcher, World world, Texture2D tex, int x, int y, int side, int offsetX, int offsetY, float fallbackDepth, float alpha)
        {
            int z = GroundZ(world, x, y, out float depth);
            Vector2 c = Centre(x, y, z, offsetX, offsetY);
            Vector2 top = c + new Vector2(0, -22), right = c + new Vector2(22, 0), bottom = c + new Vector2(0, 22), left = c + new Vector2(-22, 0);

            (Vector2 a, Vector2 b) = side switch
            {
                0 => (right, bottom),
                1 => (left, top),
                2 => (left, bottom),
                _ => (top, right)
            };

            batcher.DrawLine(tex, a, b, ShaderHueTranslator.GetHueVector(0, false, alpha), 2f, depth < 0 ? fallbackDepth : depth + 0.05f);
        }

        static void Tile(UltimaBatcher2D batcher, World world, Texture2D fill, Vector3 hue, int x, int y, int offsetX, int offsetY)
        {
            int z = GroundZ(world, x, y, out float depth);

            if (depth < 0)
            {
                return;
            }

            Vector2 c = Centre(x, y, z, offsetX, offsetY);
            batcher.Draw(fill, new Rectangle((int)c.X - 22, (int)c.Y - 22, 44, 44), null, hue, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        // A 44x44 diamond the size of a tile, one per colour.
        static Texture2D Diamond(Color color)
        {
            if (_diamonds.TryGetValue(color.PackedValue, out Texture2D tex) && !tex.IsDisposed)
            {
                return tex;
            }

            var data = new Color[44 * 44];

            for (int y = 0; y < 44; y++)
            {
                for (int x = 0; x < 44; x++)
                {
                    bool inside = Math.Abs(x - 21.5f) + Math.Abs(y - 21.5f) <= 22f;
                    data[y * 44 + x] = inside ? color : Color.Transparent;
                }
            }

            tex = new Texture2D(Client.Game.GraphicsDevice, 44, 44);
            tex.SetData(data);
            _diamonds[color.PackedValue] = tex;
            return tex;
        }
    }
}
