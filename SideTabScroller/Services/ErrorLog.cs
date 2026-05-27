using System.IO;
using System.Text;

namespace SideTabScroller.Services;

internal static class ErrorLog
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SideTabScroller");

    public static string LogPath => Path.Combine(LogDirectory, "error.log");

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(LogPath, $"{DateTime.Now:O}{Environment.NewLine}{Format(exception)}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never become the startup failure.
        }
    }

    public static string Format(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"Inner exception {depth}:");
            }

            builder.AppendLine($"{current.GetType().FullName}: {current.Message}");
            builder.AppendLine(current.StackTrace);
            current = current.InnerException;
            depth++;
        }

        builder.AppendLine();
        builder.AppendLine($"Log: {LogPath}");
        return builder.ToString();
    }
}
