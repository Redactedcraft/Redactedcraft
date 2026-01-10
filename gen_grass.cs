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

            for(int x=0; x<3; x++) for(int y=0; y<2; y++) FillTile(x, y, dirt);
            
            Random globalRnd = new Random(42);
            for(int i=0; i < w * h / 5; i++) {
                bmp.SetPixel(globalRnd.Next(w), globalRnd.Next(h), dirtDark);
            }

            int txTop = 2, tyTop = 0;
            Random topRnd = new Random(123);
            for(int x=0; x<tileSize; x++) {
                for(int y=0; y<tileSize; y++) {
                    if (topRnd.NextDouble() < 0.4) {
                        bmp.SetPixel(txTop*tileSize + x, tyTop*tileSize + y, grass);
                        if (topRnd.NextDouble() < 0.3)
                            bmp.SetPixel(txTop*tileSize + x, tyTop*tileSize + y, grassDark);
                    }
                }
            }

            int[][] sideTiles = { new int[] {0,0}, new int[] {1,0}, new int[] {1,1}, new int[] {2,1} };
            foreach(var tile in sideTiles)
            {
                int tx = tile[0];
                int ty = tile[1];
                Random sideRnd = new Random(tx + ty * 7);
                for(int x=0; x<tileSize; x++) {
                    int grassHeight = 1 + (sideRnd.NextDouble() < 0.5 ? 1 : 0); 
                    for(int y=0; y<grassHeight; y++) {
                        bmp.SetPixel(tx*tileSize + x, ty*tileSize + y, grass);
                    }
                }
            }
        }
        bmp.Save("grass_final.png", ImageFormat.Png);
        bmp.Dispose();
    }
}
