// A sprite drawn as horizontal bands, each band's edges pushed sideways by its own amount: trees
// and grass bending in the wind (ClassicUO.Client IEX/IexSway.cs). At zero shift it lands on the
// same pixels as a plain Draw.

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer
{
    public sealed unsafe partial class UltimaBatcher2D
    {
        /// <summary>
        /// Draws the sprite in shifts.Length - 1 bands, top to bottom. shifts[i] is how far (pixels,
        /// + is right) the edge between band i - 1 and band i moves; shifts[0] is the top edge.
        /// </summary>
        public void DrawIexBent(Texture2D texture, Vector2 position, Rectangle sourceRect, Vector3 hue, float depth, ReadOnlySpan<float> shifts)
        {
            int bands = shifts.Length - 1;

            if (bands < 1 || texture == null || texture.IsDisposed)
            {
                return;
            }

            CalculateHalfPixelUVs(sourceRect, texture.Width, texture.Height, out float sourceX, out float sourceY, out float sourceW, out float sourceH);

            float width = sourceRect.Width;
            float height = sourceRect.Height;

            for (int i = 0; i < bands; i++)
            {
                int r0 = sourceRect.Height * i / bands;
                int r1 = sourceRect.Height * (i + 1) / bands;

                if (r1 <= r0)
                {
                    continue;
                }

                EnsureSize();

                ref PositionNormalTextureColor4 v = ref _vertexInfo[_numSprites];

                float y0 = position.Y + r0, y1 = position.Y + r1;
                float x0 = position.X + shifts[i], x1 = position.X + shifts[i + 1];

                v.Position0.X = x0;
                v.Position0.Y = y0;
                v.Position1.X = x0 + width;
                v.Position1.Y = y0;
                v.Position2.X = x1;
                v.Position2.Y = y1;
                v.Position3.X = x1 + width;
                v.Position3.Y = y1;

                v.Position0.Z = v.Position1.Z = v.Position2.Z = v.Position3.Z = depth;

                float v0 = sourceY + sourceH * (r0 / height);
                float v1 = sourceY + sourceH * (r1 / height);

                v.TextureCoordinate0.X = sourceX;
                v.TextureCoordinate0.Y = v0;
                v.TextureCoordinate1.X = sourceX + sourceW;
                v.TextureCoordinate1.Y = v0;
                v.TextureCoordinate2.X = sourceX;
                v.TextureCoordinate2.Y = v1;
                v.TextureCoordinate3.X = sourceX + sourceW;
                v.TextureCoordinate3.Y = v1;
                v.TextureCoordinate0.Z = v.TextureCoordinate1.Z = v.TextureCoordinate2.Z = v.TextureCoordinate3.Z = 0;

                SetDefaultNormals(ref v);

                v.Hue0 = v.Hue1 = v.Hue2 = v.Hue3 = hue;

                PushSprite(texture);
            }
        }
    }
}
