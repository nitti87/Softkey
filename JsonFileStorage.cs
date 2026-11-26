using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

// Keep the previous readable generation. An invalid primary must never replace its good backup.
sealed class JsonFileStorage
{
    readonly string directory;
    enum ReadResult { Missing, Valid, Invalid, Unavailable }

    public JsonFileStorage(string directory) => this.directory = directory;

    static ReadResult Read<T>(string path, out T value)
    {
        value = default;
        try
        {
            using var stream = File.OpenRead(path);
            value = JsonSerializer.Deserialize<T>(stream);
            return value is null ? ReadResult.Invalid : ReadResult.Valid;
        }
        catch (FileNotFoundException) { return ReadResult.Missing; }
        catch (DirectoryNotFoundException) { return ReadResult.Missing; }
        catch (JsonException)
        {
            // Do not put personal words or malformed file contents in diagnostic logs.
            Trace.WriteLine($"Invalid JSON file: {Path.GetFileName(path)}");
            return ReadResult.Invalid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Cannot read {Path.GetFileName(path)}: {ex.Message}");
            return ReadResult.Unavailable;
        }
    }

    public T Load<T>(string name, T fallback)
        => Load(name, fallback, useBackup: true);

    public T Load<T>(string name, T fallback, bool useBackup)
    {
        string path = Path.Combine(directory, name);
        if (Read<T>(path, out var value) == ReadResult.Valid) return value;
        if (useBackup && Read<T>(path + ".bak", out value) == ReadResult.Valid)
        {
            Trace.WriteLine($"Recovered {name} from backup.");
            return value;
        }
        return fallback;
    }

    public bool Save<T>(string name, T value)
        => Save(name, value, keepBackup: true);

    public bool Save<T>(string name, T value, bool keepBackup)
    {
        if (value is null) return false;
        string destination = Path.Combine(directory, name);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }

            var existing = Read<T>(destination, out _);
            if (existing == ReadResult.Unavailable || (!keepBackup && existing == ReadResult.Invalid)) return false;
            if (existing == ReadResult.Missing) File.Move(temporary, destination);
            else
            {
                if (existing == ReadResult.Invalid)
                    File.Copy(destination, destination + $".corrupt-{Guid.NewGuid():N}");
                File.Replace(temporary, destination, keepBackup && existing == ReadResult.Valid ? destination + ".bak" : null);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Trace.WriteLine($"Cannot save {name}: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"Cannot remove temporary file: {ex.Message}");
            }
        }
    }
}
