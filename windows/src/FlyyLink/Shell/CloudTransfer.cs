using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.CldApi;

namespace FlyyLink.Shell;

/// One range of a placeholder file that Windows asked for, through the Cloud Files API.
internal sealed record CloudFetch(CF_CONNECTION_KEY Connection, CF_TRANSFER_KEY Transfer, CF_REQUEST_KEY Request, long Offset, long Length)
{
    private const int ChunkSize = 1024 * 1024;

    public static readonly NTStatus Unsuccessful = unchecked((NTStatus)0xC0000001);

    public static CloudFetch Of(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters) =>
        new(info.ConnectionKey, info.TransferKey, info.RequestKey, parameters.FetchData.RequiredFileOffset, parameters.FetchData.RequiredLength);

    /// Hands Windows the bytes it asked for from `file`, a chunk at a time. A null file fails the request.
    public void Deliver(string? file)
    {
        if (file is null)
        {
            Fail();
            return;
        }

        try
        {
            using var stream = File.OpenRead(file);
            var end = Math.Min(stream.Length, Offset + Length);
            var buffer = new byte[ChunkSize];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                stream.Position = Offset;
                for (var offset = Offset; offset < end;)
                {
                    var read = stream.Read(buffer, 0, (int)Math.Min(ChunkSize, end - offset));
                    if (read <= 0) break;

                    Send(NTStatus.STATUS_SUCCESS, handle.AddrOfPinnedObject(), offset, read);
                    offset += read;
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not hand over a phone file: {e.Message}");
            Fail();
        }
    }

    public void Fail() => Send(Unsuccessful, IntPtr.Zero, Offset, Length);

    private void Send(NTStatus status, IntPtr buffer, long offset, long length)
    {
        var operation = new CF_OPERATION_INFO
        {
            StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
            ConnectionKey = Connection,
            TransferKey = Transfer,
            RequestKey = Request,
        };
        var parameters = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERDATA
        {
            CompletionStatus = status,
            Buffer = buffer,
            Offset = offset,
            Length = length,
        });

        CfExecute(operation, ref parameters);
    }
}
