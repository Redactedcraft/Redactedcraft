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
            
            Color dirt = Color.FromArgb(101, 67, 33);
            Color dirtDark = Color.FromArgb(80, 50, 20);
            Color grass = Color.FromArgb(78, 154, 6);
            Color grassDark = Color.FromArgb(60, 120, 4);

            // Tiles:
            // (0,0) PosX (Side) | (1,0) PosZ (Side) | (2,0) PosY (Top)
            // (0,1) NegY (Bottom) | (1,1) NegX (Side) | (2,1) NegZ (Side)

            // 1. Bottom Tile (0,1) - All Dirt
            FillTile(0, 1, dirt);
            for(int i=0; i<30; i++) {
                Random rnd = new Random(i);
                bmp.SetPixel(0*tileSize + rnd.Next(tileSize), 1*tileSize + rnd.Next(tileSize), dirtDark);
            }

            // 2. Top Tile (2,0) - All Grass
            FillTile(2, 0, grass);
            for(int i=0; i<40; i++) {
                Random rnd = new Random(i + 100);
                bmp.SetPixel(2*tileSize + rnd.Next(tileSize), 0*tileSize + rnd.Next(tileSize), grassDark);
            }

            // 3. Side Tiles (0,0), (1,0), (1,1), (2,1)
            int[][] sideTiles = { new int[] {0,0}, new int[] {1,0}, new int[] {1,1}, new int[] {2,1} };
            foreach(var tile in sideTiles)
            {
                int tx = tile[0];
                int ty = tile[1];
                
                // Fill with dirt first
                for(int x=0; x<tileSize; x++) {
                    for(int y=0; y<tileSize; y++) {
                        bmp.SetPixel(tx*tileSize + x, ty*tileSize + y, dirt);
                    }
                }
                
                // Add dirt noise
                Random rnd = new Random(tx + ty * 10);
                for(int i=0; i<20; i++) {
                    bmp.SetPixel(tx*tileSize + rnd.Next(tileSize), ty*tileSize + rnd.Next(tileSize), dirtDark);
                }

                // Add thin grass top (2-3 pixels)
                for(int x=0; x<tileSize; x++) {
                    int grassHeight = 2 + (x % 2 == 0 ? 1 : 0); // Slight variation
                    for(int y=0; y<grassHeight; y++) {
                        bmp.SetPixel(tx*tileSize + x, ty*tileSize + y, grass);
                    }
                    // Drip effect
                    if (x % 4 == 0) bmp.SetPixel(tx*tileSize + x, ty*tileSize + grassHeight, grass);
                }
            }
        }
        bmp.Save("grass_new.png", ImageFormat.Png);
        bmp.Dispose();
    }
}
