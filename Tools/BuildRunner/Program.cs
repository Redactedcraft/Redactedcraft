using System;
using System.Windows.Forms;

namespace BuildRunner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var root = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : AppContext.BaseDirectory;

        Application.Run(new MainForm(root));
    }
}
