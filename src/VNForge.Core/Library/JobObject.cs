using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace VNForge.Core.Library;

/// <summary>
/// Um job object do Windows: agrupa um processo e todos os que ele abrir depois (filhos, netos), mesmo quando o pai sai antes. É assim que o VNForge sabe que o jogo
/// acabou quando um lançador abre outro executável e fecha. O job não limita nem encerra ninguém: fechar o identificador deixa os processos como estavam.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class JobObject : IDisposable
{
    // JOBOBJECTINFOCLASS.JobObjectBasicProcessIdList
    private const int BasicProcessIdList = 3;
    private const int ErrorMoreData = 234;
    private const int Capacity = 256;
    private const int HeaderSize = 8;

    private readonly SafeJobHandle _handle;

    private JobObject(SafeJobHandle handle) => _handle = handle;

    /// <summary>O job vazio, ou <see langword="null"/> quando o sistema não deixa criar.</summary>
    public static JobObject? TryCreate()
    {
        var handle = CreateJobObjectW(0, null);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new JobObject(handle);
    }

    /// <summary>Põe o processo no job. Falha (e devolve <see langword="false"/>) quando o processo já saiu ou roda com mais privilégio que o VNForge.</summary>
    public bool TryAssign(nint processHandle) => AssignProcessToJobObject(_handle, processHandle);

    /// <summary>Os processos vivos do job. Falso quando o sistema não respondeu.</summary>
    public bool TryListProcesses(out IReadOnlyList<int> processIds)
    {
        // JOBOBJECT_BASIC_PROCESS_ID_LIST: dois DWORD (atribuídos, na lista) e depois os ULONG_PTR dos ids.
        var buffer = new byte[HeaderSize + (Capacity * nint.Size)];
        if (!QueryInformationJobObject(_handle, BasicProcessIdList, buffer, (uint)buffer.Length, out _) && Marshal.GetLastPInvokeError() != ErrorMoreData)
        {
            processIds = [];
            return false;
        }

        var count = (int)BitConverter.ToUInt32(buffer, 4);
        var ids = new int[count];
        for (var i = 0; i < count; i++)
        {
            var offset = HeaderSize + (i * nint.Size);
            ids[i] = nint.Size == sizeof(long) ? (int)BitConverter.ToInt64(buffer, offset) : BitConverter.ToInt32(buffer, offset);
        }

        processIds = ids;
        return true;
    }

    public void Dispose() => _handle.Dispose();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeJobHandle CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeJobHandle job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(SafeJobHandle job, int infoClass, [Out] byte[] buffer, uint length, out uint returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
}
