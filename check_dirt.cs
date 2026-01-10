using System;
using System.Drawing;

class Program
{
    static void Main()
    {
        using (Bitmap bmp = new Bitmap("exported_blocks/dirt.png"))
        {
            Color c = bmp.GetPixel(0, 0);
            Console.WriteLine("DIRT_BASE:" + c.R + "," + c.G + "," + c.B);
            Color c2 = bmp.GetPixel(1, 1);
            Console.WriteLine("DIRT_NOISE:" + c2.R + "," + c2.G + "," + c2.B);
        }
    }
}
