using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Native C# verifier for SoftKey signed update archives (.zip + PKCS#7 update.p7m).
static class SoftKeyUpdateArchive
{
    public const string TrustedThumbprint = "093405919B8DF0EF0F13D124D7DB22FE685979BB";
    const long MaxArchiveBytes = 64L * 1024 * 1024;
    const long MaxManifestBytes = 64L * 1024;
    const long MaxFileBytes = 32L * 1024 * 1024;
    const uint EncodingType = 0x00010001; // X509_ASN_ENCODING | PKCS_7_ASN_ENCODING

    static readonly Regex CommitPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static readonly string[] ExpectedFiles =
    {
        "SoftKey.exe", "SoftKey.dll", "SoftKey.deps.json", "SoftKey.runtimeconfig.json",
        "SoftKey.Logon.exe", "SoftKey.Logon.dll", "SoftKey.Logon.deps.json", "SoftKey.Logon.runtimeconfig.json",
        "SoftKey.Locked.exe", "SoftKey.Locked.dll", "SoftKey.Locked.deps.json", "SoftKey.Locked.runtimeconfig.json",
        "SoftKey.Startup.exe", "SoftKey.Startup.dll", "SoftKey.Startup.deps.json", "SoftKey.Startup.runtimeconfig.json"
    };

    internal sealed record FileRecord(string Name, long Size, string SHA256);
    internal sealed record Manifest(int Schema, long BuildNumber, string Commit, FileRecord[] Files);

    internal static X509Certificate2 LoadTrustedCertificate()
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, TrustedThumbprint, validOnly: false);
        if (matches.Count == 0)
            throw new CryptographicException("SoftKeys betrodda signeringscertifikat hittades inte.");
        return new X509Certificate2(matches[0]);
    }

    internal static Manifest ReadVerified(string archivePath, X509Certificate2 trustedCertificate, long installedBuildNumber = 0)
    {
        if (trustedCertificate == null) throw new ArgumentNullException(nameof(trustedCertificate));
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > MaxArchiveBytes)
            throw new InvalidDataException("Uppdateringspaketets storlek är ogiltig.");

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var expectedEntries = new HashSet<string>(ExpectedFiles, StringComparer.Ordinal) { "update.p7m" };
        if (zip.Entries.Count != expectedEntries.Count
            || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != expectedEntries.Count
            || zip.Entries.Any(e => !expectedEntries.Contains(e.FullName)))
        {
            throw new InvalidDataException("Uppdateringspaketet innehåller saknade, dubbla eller oväntade filer.");
        }

        var manifestEntry = zip.GetEntry("update.p7m")
            ?? throw new InvalidDataException("Signerat manifest saknas i uppdateringspaketet.");
        if (manifestEntry.Length < 1 || manifestEntry.Length > MaxManifestBytes)
            throw new InvalidDataException("Det signerade manifestets storlek är ogiltig.");

        byte[] encoded = ReadExactBytes(manifestEntry, MaxManifestBytes);
        byte[] verifiedJson = VerifySignedCms(encoded, trustedCertificate);
        var manifest = JsonSerializer.Deserialize<Manifest>(Encoding.UTF8.GetString(verifiedJson), JsonOptions);

        if (manifest == null || manifest.Schema != 1 || string.IsNullOrEmpty(manifest.Commit)
            || !CommitPattern.IsMatch(manifest.Commit) || manifest.BuildNumber <= Math.Max(0L, installedBuildNumber)
            || manifest.Files == null)
        {
            throw new InvalidDataException("Det signerade manifestet är ogiltigt eller inte nyare än installerad version.");
        }

        var expectedSet = new HashSet<string>(ExpectedFiles, StringComparer.Ordinal);
        if (manifest.Files.Length != expectedSet.Count
            || manifest.Files.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != expectedSet.Count
            || manifest.Files.Any(f => !expectedSet.Contains(f.Name)))
        {
            throw new InvalidDataException("Det signerade manifestet beskriver inte förväntade programfiler.");
        }

        foreach (var record in manifest.Files)
        {
            var entry = zip.GetEntry(record.Name)
                ?? throw new InvalidDataException($"Programfil saknas i paketet: {record.Name}");
            if (entry.Length < 1 || entry.Length > MaxFileBytes || entry.Length != record.Size)
                throw new InvalidDataException($"Ogiltig filstorlek i paketet: {record.Name}");
            byte[] fileBytes = ReadExactBytes(entry, MaxFileBytes);
            if (fileBytes.Length != record.Size)
                throw new InvalidDataException($"Ogiltig filstorlek i paketet: {record.Name}");
            string actualHash = Convert.ToHexString(SHA256.HashData(fileBytes));
            if (!string.Equals(actualHash, record.SHA256, StringComparison.Ordinal))
                throw new CryptographicException($"Filens kontrollsumma stämmer inte: {record.Name}");
        }

        return manifest;
    }

    internal static byte[] VerifySignedCms(byte[] encoded, X509Certificate2 trustedCertificate)
    {
        var para = new CryptVerifyMessagePara
        {
            Size = (uint)Marshal.SizeOf<CryptVerifyMessagePara>(),
            EncodingType = EncodingType
        };

        uint decodedSize = 0;
        IntPtr signerCertPtr = IntPtr.Zero;
        try
        {
            if (!CryptVerifyMessageSignature(ref para, 0, encoded, (uint)encoded.Length, null, ref decodedSize, out signerCertPtr)
                || signerCertPtr == IntPtr.Zero || decodedSize < 1 || decodedSize > MaxManifestBytes)
            {
                throw new CryptographicException("Uppdateringens signatur kunde inte verifieras.");
            }

            // Ensure there is exactly one signer in the PKCS#7 payload.
            uint extraSize = 0;
            IntPtr extraSignerPtr = IntPtr.Zero;
            try
            {
                if (CryptVerifyMessageSignature(ref para, 1, encoded, (uint)encoded.Length, null, ref extraSize, out extraSignerPtr))
                    throw new CryptographicException("Uppdateringsmanifestet får bara ha en signerare.");
            }
            finally
            {
                if (extraSignerPtr != IntPtr.Zero) CertFreeCertificateContext(extraSignerPtr);
            }

            using var signerCert = new X509Certificate2(signerCertPtr);
            if (!signerCert.RawData.SequenceEqual(trustedCertificate.RawData))
                throw new CryptographicException("Uppdateringsmanifestet är inte signerat med SoftKeys betrodda certifikat.");

            byte[] decoded = new byte[decodedSize];
            IntPtr secondCertPtr = IntPtr.Zero;
            try
            {
                if (!CryptVerifyMessageSignature(ref para, 0, encoded, (uint)encoded.Length, decoded, ref decodedSize, out secondCertPtr)
                    || decodedSize != decoded.Length)
                {
                    throw new CryptographicException("Det verifierade manifestet kunde inte läsas ut.");
                }
            }
            finally
            {
                if (secondCertPtr != IntPtr.Zero) CertFreeCertificateContext(secondCertPtr);
            }

            return decoded;
        }
        finally
        {
            if (signerCertPtr != IntPtr.Zero) CertFreeCertificateContext(signerCertPtr);
        }
    }

    static byte[] ReadExactBytes(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length < 1 || entry.Length > maxBytes)
            throw new InvalidDataException("Arkivpostens storlek är ogiltig.");
        using var source = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        byte[] chunk = new byte[16384];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > entry.Length)
                throw new InvalidDataException("Arkivposten är större än angiven längd.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != entry.Length)
            throw new InvalidDataException("Arkivposten kunde inte läsas fullständigt.");
        return buffer.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CryptVerifyMessagePara
    {
        public uint Size;
        public uint EncodingType;
        public IntPtr CryptProv;
        public IntPtr GetSignerCertificateCallback;
        public IntPtr CallbackArg;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptVerifyMessageSignature(
        ref CryptVerifyMessagePara verifyPara,
        uint signerIndex,
        byte[] signedBlob,
        uint signedBlobSize,
        byte[] decoded,
        ref uint decodedSize,
        out IntPtr signerCertContext);

    [DllImport("crypt32.dll")]
    static extern bool CertFreeCertificateContext(IntPtr certContext);
}
