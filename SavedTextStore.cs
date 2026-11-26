using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

// Keep persisted values stable, including the text entries from earlier versions.
enum SavedTextKind { Email = 0, Password = 1, Text = 2, Phone = 3 }

sealed record SavedTextEntry(Guid Id, string Name, SavedTextKind Kind, byte[] Encrypted)
{
    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        SavedTextKind.Email => "E-post",
        SavedTextKind.Password => "Lösenord",
        SavedTextKind.Phone => "Telefonnummer",
        _ => "Text"
    };
}

sealed class SavedTextStore(JsonFileStorage storage)
{
    const string FileName = "saved-text.json";

    SavedTextEntry[] ReadAll() => storage.Load(FileName, Array.Empty<SavedTextEntry>(), useBackup: false)
        .Where(entry => entry is { Name.Length: > 0, Encrypted.Length: > 0 }
            && entry.Id != Guid.Empty && Enum.IsDefined(entry.Kind))
        .OrderBy(entry => entry.Kind).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public SavedTextEntry[] Read() => ReadAll().Where(entry => IsSpecificKind(entry.Kind)).ToArray();

    public static bool IsSpecificKind(SavedTextKind kind)
        => kind is SavedTextKind.Email or SavedTextKind.Password or SavedTextKind.Phone;

    public static bool IsPhoneNumber(string input)
    {
        string dial = input.TrimStart('+');
        return input.Length - dial.Length <= 1 && dial.Any(char.IsDigit)
            && dial.All(c => char.IsDigit(c) || c is ' ' or '\u00a0' or '-' or '(' or ')' or '.' or '#' or '*');
    }

    public bool Save(Guid id, string name, SavedTextKind kind, byte[] value)
    {
        if (!IsSpecificKind(kind)) return false;
        try
        {
            byte[] encrypted = ProtectedData.Protect(value, Entropy(id, kind), DataProtectionScope.CurrentUser);
            return storage.Save(FileName, ReadAll().Where(entry => entry.Id != id)
                .Append(new SavedTextEntry(id, name, kind, encrypted)).ToArray(), keepBackup: false);
        }
        catch (CryptographicException) { return false; }
    }

    public bool Remove(Guid id) => storage.Save(FileName, ReadAll().Where(entry => entry.Id != id).ToArray(), keepBackup: false);

    public static byte[] Open(SavedTextEntry entry)
    {
        try { return ProtectedData.Unprotect(entry.Encrypted, Entropy(entry.Id, entry.Kind), DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return null; }
    }

    static byte[] Entropy(Guid id, SavedTextKind kind) => [.. id.ToByteArray(), (byte)kind];
}
