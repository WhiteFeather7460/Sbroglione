using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sbroglione.Services;

/// <summary>Connessione UNC autenticata via mpr.dll (WNetAddConnection2).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkCredentialConnector : INetworkCredentialConnector
{
    private const int ResourceTypeDisk = 1;
    private const int ConnectUpdateProfile = 0x00000001;

    // CONNECT_UPDATE_PROFILE da solo non persiste una connessione "deviceless" (senza lettera
    // di unità) oltre il logon: CONNECT_CMD_SAVECRED salva la credenziale nel credential
    // manager dell'utente, che è ciò che rende il "ricorda" davvero persistente.
    private const int ConnectCmdSaveCred = 0x00001000;

    public bool IsSupported => true;

    public int Connect(string uncRoot, string username, string password, bool persist)
    {
        var resource = new NetResource
        {
            dwType = ResourceTypeDisk,
            lpRemoteName = uncRoot
        };
        int flags = persist ? (ConnectUpdateProfile | ConnectCmdSaveCred) : 0;
        return WNetAddConnection2W(ref resource, password, username, flags);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetAddConnection2W")]
    private static extern int WNetAddConnection2W(
        ref NetResource netResource, string? password, string? username, int flags);
}
