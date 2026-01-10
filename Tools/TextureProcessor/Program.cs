using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

class Program
{
    static void Main(string[] args)
    {
        var baseSource = @"C:\Users\Redacted\.gemini\tmp\97801cf5f85f26b1d9986d2f4d9b0abdc627f3aa8c6f912fd8a1966d350ae9ae\AssetsUnzipped\Assets\textures";
        var baseOutput = @"C:\Users\Redacted\.gemini\tmp\97801cf5f85f26b1d9986d2f4d9b0abdc627f3aa8c6f912fd8a1966d350ae9ae\AssetsProcessed\Assets\textures";

        if (args.Length >= 2) {
            baseSource = args[0];
            baseOutput = args[1];
        }

        // Process Blocks (Standard: 16x16 per face -> 48x32)
        ProcessDirectory(Path.Combine(baseSource, "blocks"), Path.Combine(baseOutput, "blocks"), 48, 32);

        // Process Blocks Low (Low: 8x8 per face -> 24x16)
        ProcessDirectory(Path.Combine(baseSource, "blocks"), Path.Combine(baseOutput, "blocks_low"), 24, 16);

        // RESTORE MENU ASSETS: Just copy from source, ensuring no downscaling happened.
        CopyDirectory(Path.Combine(baseSource, "menu"), Path.Combine(baseOutput, "menu"));

        Console.WriteLine("All Done!");
    }

    static void ProcessDirectory(string sourceDir, string outputDir, int? fixedW = null, int? fixedH = null, float scale = 1.0f)
    {
        if (!Directory.Exists(sourceDir)) {
            Console.WriteLine("Source directory not found: " + sourceDir);
            return;
        }

        Directory.CreateDirectory(outputDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*.png"))
        {
            Console.WriteLine($"Processing {Path.GetFileName(file)} in {Path.GetFileName(sourceDir)}...");
            try {
                using (var bitmap = new Bitmap(file))
                {
                    int newWidth = fixedW ?? (int)Math.Max(1, bitmap.Width * scale);
                    int newHeight = fixedH ?? (int)Math.Max(1, bitmap.Height * scale);
                    
                    using (var resized = new Bitmap(newWidth, newHeight))
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.None;
                        g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
                        resized.Save(Path.Combine(outputDir, Path.GetFileName(file)), ImageFormat.Png);
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Failed to process {file}: {{ex.Message}}");
            }
        }
    }

    static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }

        foreach (string newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourceDir, destinationDir), true);
        }
    }
}
