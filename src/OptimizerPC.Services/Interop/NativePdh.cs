using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

[StructLayout(LayoutKind.Explicit)]
internal struct PDH_FMT_COUNTERVALUE
{
    [FieldOffset(0)]
    public uint CStatus;

    [FieldOffset(8)]
    public double DoubleValue;

    [FieldOffset(8)]
    public long LongValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PDH_FMT_COUNTERVALUE_ITEM
{
    public IntPtr Name;

    public PDH_FMT_COUNTERVALUE Value;
}

/// <summary>
/// Contadores de desempenho do Windows (PDH) usados para atividade de disco e de rede.
/// Quando o contador nao existe no idioma do sistema ou esta desabilitado, a leitura retorna indisponivel.
/// </summary>
internal sealed class PdhCounterSet : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint ERROR_SUCCESS = 0;

    private IntPtr _query;
    private IntPtr _diskTime;
    private IntPtr _diskRead;
    private IntPtr _diskWrite;
    private IntPtr _networkReceived;
    private IntPtr _networkSent;
    private IntPtr _processCount;
    private IntPtr _threadCount;
    private bool _disposed;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    internal bool IsAvailable => _query != IntPtr.Zero;

    public static PdhCounterSet? TryCreate()
    {
        var set = new PdhCounterSet();

        if (PdhOpenQuery(null, IntPtr.Zero, out set._query) != ERROR_SUCCESS || set._query == IntPtr.Zero)
        {
            return null;
        }

        TryAdd(set._query, @"\PhysicalDisk(_Total)\% Disk Time", out set._diskTime);
        TryAdd(set._query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", out set._diskRead);
        TryAdd(set._query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", out set._diskWrite);
        TryAdd(set._query, @"\Network Interface(*)\Bytes Received/sec", out set._networkReceived);
        TryAdd(set._query, @"\Network Interface(*)\Bytes Sent/sec", out set._networkSent);
        TryAdd(set._query, @"\System\Processes", out set._processCount);
        TryAdd(set._query, @"\System\Threads", out set._threadCount);

        // Primeira coleta serve de base para os contadores de taxa.
        PdhCollectQueryData(set._query);
        return set;
    }

    private static void TryAdd(IntPtr query, string path, out IntPtr counter)
    {
        counter = IntPtr.Zero;

        try
        {
            if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var handle) == ERROR_SUCCESS)
            {
                counter = handle;
            }
        }
        catch (DllNotFoundException)
        {
            counter = IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            counter = IntPtr.Zero;
        }
    }

    public bool TryReadDiskActivity(out double percent)
    {
        percent = 0;

        if (!TryCollect())
        {
            return false;
        }

        if (!TryReadSingle(_diskTime, out percent))
        {
            return false;
        }

        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    public bool TryReadDiskThroughput(out double readBytesPerSecond, out double writeBytesPerSecond)
    {
        readBytesPerSecond = 0;
        writeBytesPerSecond = 0;

        if (!TryCollect())
        {
            return false;
        }

        var readOk = TryReadSingle(_diskRead, out readBytesPerSecond);
        var writeOk = TryReadSingle(_diskWrite, out writeBytesPerSecond);
        return readOk || writeOk;
    }

    public bool TryReadNetworkThroughput(out double receivedBytesPerSecond, out double sentBytesPerSecond)
    {
        receivedBytesPerSecond = 0;
        sentBytesPerSecond = 0;

        if (!TryCollect())
        {
            return false;
        }

        var receiveOk = TryReadSum(_networkReceived, out receivedBytesPerSecond);
        var sendOk = TryReadSum(_networkSent, out sentBytesPerSecond);
        return receiveOk || sendOk;
    }

    /// <summary>Quantidade de processos e threads do sistema, sem enumerar processos individuais.</summary>
    public bool TryReadSystemCounts(out int processes, out int threads)
    {
        processes = 0;
        threads = 0;

        if (!TryCollect())
        {
            return false;
        }

        var processOk = TryReadSingle(_processCount, out var processValue);
        var threadOk = TryReadSingle(_threadCount, out var threadValue);

        if (processOk && processValue is >= 0 and < int.MaxValue)
        {
            processes = (int)processValue;
        }

        if (threadOk && threadValue is >= 0 and < int.MaxValue)
        {
            threads = (int)threadValue;
        }

        return processOk || threadOk;
    }

    private bool TryCollect()
    {        if (_query == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return PdhCollectQueryData(_query) == ERROR_SUCCESS;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadSingle(IntPtr counter, out double value)
    {
        value = 0;

        if (counter == IntPtr.Zero)
        {
            return false;
        }

        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var counterValue) != ERROR_SUCCESS)
        {
            return false;
        }

        value = counterValue.DoubleValue;
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TryReadSum(IntPtr counter, out double value)
    {
        value = 0;

        if (counter == IntPtr.Zero)
        {
            return false;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        var status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref bufferSize, out itemCount, IntPtr.Zero);

        if (status != PDH_MORE_DATA || bufferSize == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref bufferSize, out itemCount, buffer) != ERROR_SUCCESS)
            {
                return false;
            }

            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            var total = 0.0;
            var any = false;

            for (var i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(IntPtr.Add(buffer, i * itemSize));
                var itemValue = item.Value.DoubleValue;

                if (double.IsNaN(itemValue) || double.IsInfinity(itemValue))
                {
                    continue;
                }

                total += itemValue;
                any = true;
            }

            value = total;
            return any;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_query != IntPtr.Zero)
        {
            try
            {
                PdhCloseQuery(_query);
            }
            catch (Exception)
            {
                // Encerramento silencioso: a alca pertence ao processo e sera liberada no fim da execucao.
            }

            _query = IntPtr.Zero;
        }
    }
}
