using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using Naad.History;

namespace Naad.Window;

/// <summary>A history record's Export button. Use on the UI thread.</summary>
internal static class HistoryExport
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // On request only, after a warning: the record, its prompt and its recording, unencrypted, in a
    // ZIP the user chooses. Screenshots are never kept, so there is none to add.
    public static async Task ExportAsync(HistoryRecord record, HistoryStore store)
    {
        if (System.Windows.MessageBox.Show(
            "The ZIP file isn't encrypted: anyone who has it can read the text and hear the recording. Export?",
            "Naad", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"naad-dictation-{record.Time.ToLocalTime():yyyy-MM-dd-HHmmss}.zip",
            Filter = "ZIP archive|*.zip",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;
        try
        {
            await Task.Run(() => WriteZip(path, record, store.Audio(record.Id)));
        }
        catch (Exception e)
        {
            // Any failure, not only the disk's: an exception escaping a click would end the app.
            Log.Error(LogEvent.HistoryExportFailed, e);

            // A half-written ZIP would look like a good one.
            try
            {
                File.Delete(path);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Log.Error(LogEvent.HistoryExportFailed, cleanup);
            }

            System.Windows.MessageBox.Show("The dictation couldn't be exported there.", "Naad");
        }
    }

    private static void WriteZip(string path, HistoryRecord record, byte[]? wav)
    {
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        using (var entry = zip.CreateEntry("dictation.json").Open())
        {
            JsonSerializer.Serialize(entry, record with { Prompt = null }, Indented);
        }

        if (record.Prompt is { } prompt)
        {
            using var writer = new StreamWriter(zip.CreateEntry("prompt.txt").Open());
            writer.Write(prompt);
        }

        if (wav is not null)
        {
            using var audio = zip.CreateEntry("recording.wav").Open();
            audio.Write(wav);
        }
    }
}
