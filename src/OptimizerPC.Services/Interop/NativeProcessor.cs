using System.Numerics;
using System.Runtime.InteropServices;

namespace OptimizerPC.Services.Interop;

internal enum LOGICAL_PROCESSOR_RELATIONSHIP
{
    RelationProcessorCore = 0,
    RelationNumaNode = 1,
    RelationCache = 2,
    RelationProcessorPackage = 3,
    RelationGroup = 4,
    RelationProcessorDie = 5
}

internal enum PROCESSOR_CACHE_TYPE
{
    CacheUnified = 0,
    CacheInstruction = 1,
    CacheData = 2,
    CacheTrace = 3
}

/// <summary>Leitura da topologia do processador (nucleos fisicos, logicos e caches).</summary>
internal static class NativeProcessor
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    internal sealed class ProcessorTopology
    {
        public int PhysicalCores { get; set; }

        public int LogicalProcessors { get; set; }

        public int Packages { get; set; }

        public int PerformanceCores { get; set; }

        public int EfficiencyCores { get; set; }

        public long L2Bytes { get; set; }

        public long L3Bytes { get; set; }

        public bool IsHybrid => PerformanceCores > 0 && EfficiencyCores > 0;
    }

    internal static ProcessorTopology Query()
    {
        var topology = new ProcessorTopology();
        var cores = ReadRecords(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore);
        foreach (var record in cores)
        {
            topology.PhysicalCores++;

            // Flags (byte) + EfficiencyClass (byte) + 20 bytes reservados + GroupCount (word).
            var efficiencyClass = record.Payload[1];
            if (efficiencyClass == 0)
            {
                topology.PerformanceCores++;
            }
            else
            {
                topology.EfficiencyCores++;
            }

            var groupCount = BitConverter.ToUInt16(record.Payload, 22);
            var offset = 24;
            for (var group = 0; group < groupCount && offset + 16 <= record.Payload.Length; group++)
            {
                var mask = BitConverter.ToUInt64(record.Payload, offset + 8);
                topology.LogicalProcessors += BitOperations.PopCount(mask);
                offset += 16;
            }
        }

        foreach (var record in ReadRecords(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorPackage))
        {
            topology.Packages++;
        }

        foreach (var record in ReadRecords(LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache))
        {
            // Level (byte), Associativity (byte), LineSize (word), CacheSize (dword), Type (dword).
            var level = record.Payload[0];
            var cacheSize = BitConverter.ToUInt32(record.Payload, 4);
            var cacheType = BitConverter.ToInt32(record.Payload, 8);

            if (cacheType is (int)PROCESSOR_CACHE_TYPE.CacheInstruction or (int)PROCESSOR_CACHE_TYPE.CacheTrace)
            {
                continue;
            }

            if (level == 2)
            {
                topology.L2Bytes += cacheSize;
            }
            else if (level >= 3)
            {
                topology.L3Bytes += cacheSize;
            }
        }

        if (topology.LogicalProcessors == 0)
        {
            topology.LogicalProcessors = Environment.ProcessorCount;
        }

        if (topology.PhysicalCores == 0)
        {
            topology.PhysicalCores = topology.LogicalProcessors;
        }

        return topology;
    }

    private readonly record struct ProcessorRecord(byte[] Payload);

    private static List<ProcessorRecord> ReadRecords(LOGICAL_PROCESSOR_RELATIONSHIP relationship)
    {
        var results = new List<ProcessorRecord>();
        uint length = 0;

        if (GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref length))
        {
            return results;
        }

        if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return results;
        }

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(relationship, buffer, ref length))
            {
                return results;
            }

            var offset = 0;
            var total = (int)length;

            while (offset + 8 <= total)
            {
                // STRUCTURE: Relationship (dword) + Size (dword) + payload.
                var recordRelationship = Marshal.ReadInt32(buffer, offset);
                var recordSize = Marshal.ReadInt32(buffer, offset + 4);

                if (recordSize < 8 || offset + recordSize > total)
                {
                    break;
                }

                if (recordRelationship == (int)relationship)
                {
                    var payload = new byte[recordSize - 8];
                    Marshal.Copy(IntPtr.Add(buffer, offset + 8), payload, 0, payload.Length);
                    results.Add(new ProcessorRecord(payload));
                }

                offset += recordSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }
}
