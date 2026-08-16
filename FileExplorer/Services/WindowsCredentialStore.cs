using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace FileExplorer.Services;

/// <summary>Keyring Windows via Credential Manager (advapi32, credenziali generiche).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public bool IsAvailable => true;

    private static string TargetName(Guid profileId) => $"FileExplorer/{profileId:N}";

    public Task<string?> GetPasswordAsync(Guid profileId) => Task.Run(() =>
    {
        if (!CredRead(TargetName(profileId), CredTypeGeneric, 0, out IntPtr credentialPtr))
            return (string?)null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;

            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                // Azzera la copia gestita: la password non deve restare nell'heap.
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(credentialPtr);
        }
    });

    public Task SetPasswordAsync(Guid profileId, string password) => Task.Run(() =>
    {
        byte[] blob = Encoding.UTF8.GetBytes(password);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName(profileId),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException(
                    $"Scrittura nel Credential Manager fallita (errore {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            // Azzera il buffer non gestito prima di liberarlo e la copia gestita subito dopo:
            // la password non deve sopravvivere in memoria oltre la scrittura.
            for (int i = 0; i < blob.Length; i++)
                Marshal.WriteByte(blobPtr, i, 0);
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    });

    public Task DeletePasswordAsync(Guid profileId) => Task.Run(() =>
    {
        CredDelete(TargetName(profileId), CredTypeGeneric, 0);
    });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
