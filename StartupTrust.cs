using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

static class StartupTrust
{
    const string Certificate = "093405919B8DF0EF0F13D124D7DB22FE685979BB";
    public static void ProtectedDirectory(string directory)
    {
        string path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar);
        if (!path.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Startup kan ändras från den installerade versionen av SoftKey.");
        for (var current = new DirectoryInfo(path); current != null && current.FullName.Length >= programFiles.Length; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Installationsmappen kan inte vara en länk.");
        using var identity = WindowsIdentity.GetCurrent();
        var acl = new DirectoryInfo(path).GetAccessControl();
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            string sid = rule.IdentityReference.Value;
            if (rule.AccessControlType == AccessControlType.Allow && (sid is "S-1-1-0" or "S-1-5-11" or "S-1-5-32-545" || sid == identity.User.Value)
                && ((int)rule.FileSystemRights & 0x116) != 0) throw new InvalidOperationException("Installationsmappen är inte skrivskyddad för vanliga användare.");
        }
    }
    public static void Verify(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("En programfil har ersatts av en länk.");
        // X509CertificateLoader cannot extract the Authenticode signer from an EXE or DLL.
#pragma warning disable SYSLIB0057
        using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        if (!string.Equals(cert.GetCertHashString(), Certificate, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Programfilens signering kunde inte bekräftas.");
        IntPtr filePath = Marshal.StringToCoTaskMemUni(path), fileInfo = IntPtr.Zero;
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, StateAction = 1, Flags = 0x1010 };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = filePath };
            fileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<TrustFile>()); Marshal.StructureToPtr(info, fileInfo, false);
            data.File = fileInfo;
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) throw new InvalidOperationException("Windows kunde inte verifiera programfilens signatur.");
        }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (fileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfo);
            Marshal.FreeCoTaskMem(filePath);
        }
    }
    public static string ProcessUser(int processId, string expectedExecutable)
    {
        using var process = Process.GetProcessById(processId);
        if (!string.Equals(process.MainModule.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SoftKeys startfönster kunde inte bekräftas.");
        Verify(expectedExecutable);
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new InvalidOperationException("SoftKeys användare kunde inte bekräftas.");
        using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle())) return identity.User.Value;
    }
    [StructLayout(LayoutKind.Sequential)] struct TrustFile { public uint Size; public IntPtr Path, Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint UiChoice, Revocation, UnionChoice; public IntPtr File;
        public uint StateAction; public IntPtr State, Url; public uint Flags, Context; public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
}
