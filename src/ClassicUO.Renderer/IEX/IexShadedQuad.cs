// A flat four-cornered shape with its own strength at each corner, blended smoothly between them:
// the rolling swells on water (ClassicUO.Client IEX/IexWater.cs). Neighbouring tiles that share
// corners share values, so the bands run across tile edges without seams. With a source rect the
// art's own shape is the mask (the shore's water pieces), recoloured by the hue vector.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer
{
    public sealed unsafe partial class UltimaBatcher2D
    {
        /// <summary>
        /// Draws a solid-texture quad with corners top, right, left, bottom (a tile's diamond) and
        /// an alpha per corner. hue is the usual hue vector; its Z is replaced by each corner's alpha.
        /// </summary>
        public void DrawIexShadedQuad(Texture2D texture, Vector2 top, Vector2 right, Vector2 left, Vector2 bottom, Vector4 alphas, Vector3 hue, float depth)
        {
            if (texture == null || texture.IsDisposed)
            {
                return;
            }

            EnsureSize();

            ref PositionNormalTextureColor4 v = ref _vertexInfo[_numSprites];
            SetIexCorners(ref v, top, right, left, bottom, alphas, hue, depth);

            v.TextureCoordinate0 = new Vector3(0, 0, 0);
            v.TextureCoordinate1 = new Vector3(1, 0, 0);
            v.TextureCoordinate2 = new Vector3(0, 1, 0);
            v.TextureCoordinate3 = new Vector3(1, 1, 0);

            PushSprite(texture);
        }

        /// <summary>
        /// Draws part of a texture as a quad with corners topLeft, topRight, bottomLeft, bottomRight
        /// and an alpha per corner.
        /// </summary>
        public void DrawIexShadedQuad(Texture2D texture, Rectangle sourceRect, Vector2 topLeft, Vector2 topRight, Vector2 bottomLeft, Vector2 bottomRight, Vector4 alphas, Vector3 hue, float depth)
        {
            if (texture == null || texture.IsDisposed)
            {
                return;
            }

            EnsureSize();

            ref PositionNormalTextureColor4 v = ref _vertexInfo[_numSprites];
            SetIexCorners(ref v, topLeft, topRight, bottomLeft, bottomRight, alphas, hue, depth);

            CalculateHalfPixelUVs(sourceRect, texture.Width, texture.Height, out float sx, out float sy, out float sw, out float sh);

            v.TextureCoordinate0 = new Vector3(sx, sy, 0);
            v.TextureCoordinate1 = new Vector3(sx + sw, sy, 0);
            v.TextureCoordinate2 = new Vector3(sx, sy + sh, 0);
            v.TextureCoordinate3 = new Vector3(sx + sw, sy + sh, 0);

            PushSprite(texture);
        }

        private static void SetIexCorners(ref PositionNormalTextureColor4 v, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector4 alphas, Vector3 hue, float depth)
        {
            v.Position0.X = p0.X;
            v.Position0.Y = p0.Y;
            v.Position1.X = p1.X;
            v.Position1.Y = p1.Y;
            v.Position2.X = p2.X;
            v.Position2.Y = p2.Y;
            v.Position3.X = p3.X;
            v.Position3.Y = p3.Y;
            v.Position0.Z = v.Position1.Z = v.Position2.Z = v.Position3.Z = depth;

            SetDefaultNormals(ref v);

            v.Hue0 = new Vector3(hue.X, hue.Y, alphas.X);
            v.Hue1 = new Vector3(hue.X, hue.Y, alphas.Y);
            v.Hue2 = new Vector3(hue.X, hue.Y, alphas.Z);
            v.Hue3 = new Vector3(hue.X, hue.Y, alphas.W);
        }
    }
}
