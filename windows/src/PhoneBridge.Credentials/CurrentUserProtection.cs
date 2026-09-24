using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PhoneBridge.Credentials;

internal static class CurrentUserProtection
{
    internal static byte[] Protect(byte[] plaintext,string deviceId) => Transform(plaintext,deviceId,true);
    internal static byte[] Unprotect(byte[] ciphertext,string deviceId) => Transform(ciphertext,deviceId,false);
    private static byte[] Transform(byte[] input,string deviceId,bool protect)
    {
        if (input.Length is < 1 or > RecordCodec.MaxFileBytes) throw new CredentialStoreException(StoreError.NeedsRepair);
        _=RecordRules.HashFromDeviceId(deviceId);
        var entropy=Encoding.ASCII.GetBytes("PhoneBridge NG|windows-record|v1|"+deviceId);
        GCHandle inputPin=default,entropyPin=default;
        DataBlob output=default;
        try
        {
            inputPin=GCHandle.Alloc(input,GCHandleType.Pinned);
            entropyPin=GCHandle.Alloc(entropy,GCHandleType.Pinned);
            var data=new DataBlob { Size=input.Length,Data=inputPin.AddrOfPinnedObject() };
            var extra=new DataBlob { Size=entropy.Length,Data=entropyPin.AddrOfPinnedObject() };
            // UI_FORBIDDEN only; never LOCAL_MACHINE, AUDIT, prompt or human-readable description.
            bool success=protect?CryptProtectData(ref data,0,ref extra,0,0,1,out output):CryptUnprotectData(ref data,0,ref extra,0,0,1,out output);
            if (!success || output.Data==0 || output.Size is < 1 or > RecordCodec.MaxFileBytes) throw new CredentialStoreException(StoreError.NeedsRepair);
            var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            if (output.Data!=0)
            {
                // Avoid allocating another managed buffer while releasing plaintext native memory.
                if (output.Size is > 0 and <= RecordCodec.MaxFileBytes)
                    for(int i=0;i<output.Size;i++)Marshal.WriteByte(output.Data,i,0);
                _=LocalFree(output.Data);
            }
            if(entropyPin.IsAllocated)entropyPin.Free();
            if(inputPin.IsAllocated)inputPin.Free();
            CryptographicOperations.ZeroMemory(entropy);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { internal int Size;internal nint Data; }
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob data,nint description,ref DataBlob entropy,nint reserved,nint prompt,uint flags,out DataBlob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob data,nint description,ref DataBlob entropy,nint reserved,nint prompt,uint flags,out DataBlob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
