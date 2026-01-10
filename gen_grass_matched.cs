using System;
using System.Drawing;
using System.Drawing.Imaging;

class Program
{
    static int tileSize = 16;
    static Bitmap bmp;

    static void FillTile(int tx, int ty, Color c)
    {
        for(int x = 0; x < tileSize; x++)
            for(int y = 0; y < tileSize; y++)
                bmp.SetPixel(tx * tileSize + x, ty * tileSize + y, c);
    }

    static void Main()
    {
        int w = tileSize * 3;
        int h = tileSize * 2;
        
        bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            
            // Colors matched from dirt.png
            Color dirtBase = Color.FromArgb(104, 69, 39);
            Color dirtNoise = Color.FromArgb(129, 94, 64);
            
            Color grass = Color.FromArgb(78, 154, 6);
            Color grassDark = Color.FromArgb(60, 120, 4);

            // 1. Fill all with Dirt Base
            for(int x=0; x<3; x++) for(int y=0; y<2; y++) FillTile(x, y, dirtBase);
            
            // Add dirt noise to all tiles
            Random rnd = new Random(888);
            for(int i=0; i < 300; i++) {
                bmp.SetPixel(rnd.Next(w), rnd.Next(h), dirtNoise);
            }

            // 2. Top Tile (2,0) - Solid Green with some variance
            FillTile(2, 0, grass);
            for(int i=0; i<40; i++) {
                bmp.SetPixel(2*tileSize + rnd.Next(tileSize), 0*tileSize + rnd.Next(tileSize), grassDark);
            }

            // 3. Side Tiles - Minimal green top edge
            int[][] sideTiles = { new int[] {0,0}, new int[] {1,0}, new int[] {1,1}, new int[] {2,1} };
            foreach(var tile in sideTiles)
            {
                int tx = tile[0];
                int ty = tile[1];
                for(int x=0; x<tileSize; x++) {
                    // Constant 1 pixel green top
                    bmp.SetPixel(tx*tileSize + x, ty*tileSize, grass);
                    // 40% chance for a second pixel
                    if (rnd.NextDouble() < 0.4) {
                        bmp.SetPixel(tx*tileSize + x, ty*tileSize + 1, grass);
                    }
                }
            }
        }
        bmp.Save("grass_matched.png", ImageFormat.Png);
        bmp.Dispose();
    }
}
