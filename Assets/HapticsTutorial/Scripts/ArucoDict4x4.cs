using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// OpenCV DICT_4X4_50 IDs 0–5 (first six of DICT_4X4_1000). Print these on the cube faces.
    /// </summary>
    public static class ArucoDict4x4
    {
        public const int MarkerCount = 6;
        public const int Grid = 4;
        public const int MaxHamming = 2;

        // Rotation 0 only, row-major, 1 = black. Bytes from OpenCV DICT_4X4_1000.
        static readonly byte[,] Bytes =
        {
            { 181, 50 },
            { 15, 154 },
            { 51, 45 },
            { 153, 70 },
            { 84, 158 },
            { 121, 205 }
        };

        public static bool TryIdentify(int[,] bits, out int id, out int rotation)
        {
            id = -1;
            rotation = 0;
            var best = MaxHamming + 1;
            var rotated = new int[Grid, Grid];
            for (var rot = 0; rot < 4; rot++)
            {
                CopyRotateCw(bits, rot, rotated);
                for (var marker = 0; marker < MarkerCount; marker++)
                {
                    var dist = HammingIdentity(rotated, marker);
                    if (dist >= best)
                        continue;
                    best = dist;
                    id = marker;
                    rotation = rot;
                    if (dist == 0)
                        return best <= MaxHamming;
                }
            }

            return best <= MaxHamming;
        }

        public static int Bit(int id, int row, int col)
        {
            var index = row * Grid + col;
            var b = Bytes[id, index / 8];
            var bit = 7 - (index % 8);
            return (b >> bit) & 1;
        }

        public static Texture2D MakeTexture(int id, int pixelsPerModule = 64)
        {
            const int border = 1;
            const int quiet = 1;
            var modules = Grid + border * 2 + quiet * 2;
            var size = modules * pixelsPerModule;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            for (var y = 0; y < size; y++)
            {
                var my = y / pixelsPerModule;
                for (var x = 0; x < size; x++)
                {
                    var mx = x / pixelsPerModule;
                    tex.SetPixel(x, y, ModuleIsBlack(id, mx, my, modules) ? Color.black : Color.white);
                }
            }

            tex.Apply(false, false);
            return tex;
        }

        static bool ModuleIsBlack(int id, int mx, int my, int modules)
        {
            const int quiet = 1;
            const int border = 1;
            if (mx < quiet || my < quiet || mx >= modules - quiet || my >= modules - quiet)
                return false;

            var cx = mx - quiet;
            var cy = my - quiet;
            var inner = Grid + border * 2;
            if (cx == 0 || cy == 0 || cx == inner - 1 || cy == inner - 1)
                return true;

            var col = cx - border;
            var rowFromTop = cy - border;
            var row = Grid - 1 - rowFromTop;
            return Bit(id, row, col) == 1;
        }

        static int HammingIdentity(int[,] bits, int id)
        {
            var dist = 0;
            for (var row = 0; row < Grid; row++)
            {
                for (var col = 0; col < Grid; col++)
                {
                    if (bits[row, col] != Bit(id, row, col))
                        dist++;
                }
            }

            return dist;
        }

        static void CopyRotateCw(int[,] src, int times, int[,] dst)
        {
            times &= 3;
            for (var row = 0; row < Grid; row++)
            {
                for (var col = 0; col < Grid; col++)
                {
                    var r = row;
                    var c = col;
                    for (var i = 0; i < times; i++)
                    {
                        var nr = c;
                        var nc = Grid - 1 - r;
                        r = nr;
                        c = nc;
                    }

                    dst[r, c] = src[row, col];
                }
            }
        }
    }
}
