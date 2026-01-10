using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

static int PrintUsage()
{
    Console.WriteLine("AssetPackBuilder - Creates a compliant Assets.zip for RedactedCraft");
    Console.WriteLine("Usage:");
    Console.WriteLine("  AssetPackBuilder [--source <AssetsDir>] [--output <OutputZip>]");
    Console.WriteLine();
    Console.WriteLine("Details:");
    Console.WriteLine("  Creates an Assets.zip containing the 'Assets' folder.");
    Console.WriteLine("  This zip can be uploaded to GitHub Releases for the launcher to download.");
    return 1;
}

string? outputZip = null;
string? sourceDir = null;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
        outputZip = args[++i];
    else if ((arg == "--source" || arg == "-s") && i + 1 < args.Length)
        sourceDir = args[++i];
    else if (arg == "--help" || arg == "-h")
        return PrintUsage();
}

var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var defaultSource = Path.Combine(documents, "RedactedCraft", "Assets");

sourceDir ??= defaultSource;
sourceDir = Path.GetFullPath(sourceDir);

if (!Directory.Exists(sourceDir))
{
    Console.Error.WriteLine($"ERROR: Source directory not found: {sourceDir}");
    Console.WriteLine($"Expected to find the 'Assets' folder here.");
    return 2;
}

// Verify minimal structure
var texturesDir = Path.Combine(sourceDir, "textures");
if (!Directory.Exists(texturesDir))
{
    Console.Error.WriteLine($"ERROR: Invalid structure. '{sourceDir}' must contain a 'textures' folder.");
    return 3;
}

outputZip ??= Path.Combine(Directory.GetCurrentDirectory(), "Assets.zip");
outputZip = Path.GetFullPath(outputZip);

try
{
    if (File.Exists(outputZip))
        File.Delete(outputZip);

    Console.WriteLine($"Zipping '{sourceDir}' to '{outputZip}'...");

    // We want the zip to contain "Assets/textures/..."
    // ZipFile.CreateFromDirectory with includeBaseDirectory=true uses the folder name.
    // If sourceDir ends in "Assets", it works. If not, we might need a temp folder or manual zipping.
    
    var dirName = Path.GetFileName(sourceDir);
    if (!string.Equals(dirName, "Assets", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Warning: Source folder name is '{dirName}', expected 'Assets'. Zip will contain '{dirName}/...'.");
    }

    ZipFile.CreateFromDirectory(sourceDir, outputZip, CompressionLevel.Optimal, includeBaseDirectory: true);

    Console.WriteLine("----------------------------------------------------------------");
    Console.WriteLine($"SUCCESS! Assets.zip created at:");
    Console.WriteLine($"  {outputZip}");
    Console.WriteLine("----------------------------------------------------------------");
    Console.WriteLine("Upload this file to your GitHub Release as 'Assets.zip'.");
    
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FATAL: Failed to create zip. {ex.Message}");
    return 4;
}
