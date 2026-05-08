using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fraude.Tools;

public sealed unsafe class IvfSearchEngine : IDisposable
{
    private const uint Magic = 0x49564631;
    private const int Dims = 14;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly byte* _basePtr;
    private readonly int _n;
    private readonly int _k;
    private readonly sbyte*[] _dimData;
    private readonly byte* _fraudLabels;
    private readonly uint* _clusterOffsets;
    private readonly sbyte[][] _centroids;

    private IvfSearchEngine(
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor accessor,
        byte* basePtr,
        int n,
        int k,
        sbyte*[] dimData,
        byte* fraudLabels,
        uint* clusterOffsets,
        sbyte[][] centroids)
    {
        _mmf = mmf;
        _accessor = accessor;
        _basePtr = basePtr;
        _n = n;
        _k = k;
        _dimData = dimData;
        _fraudLabels = fraudLabels;
        _clusterOffsets = clusterOffsets;
        _centroids = centroids;
    }

    public static IvfSearchEngine Load(string path)
    {
        var fileSize = new FileInfo(path).Length;
        var mmf = MemoryMappedFile.CreateFromFile(
            path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);
        var handle = accessor.SafeMemoryMappedViewHandle;
        byte* basePtr = (byte*)handle.DangerousGetHandle();

        var headerSpan = new ReadOnlySpan<byte>(basePtr, 32);
        var magic = MemoryMarshal.Read<uint>(headerSpan);
        if (magic != Magic)
            throw new InvalidDataException($"Invalid magic: 0x{magic:X8}");

        var n = (int)MemoryMarshal.Read<uint>(headerSpan[4..]);
        var k = (int)MemoryMarshal.Read<uint>(headerSpan[8..]);
        var d = (int)MemoryMarshal.Read<uint>(headerSpan[12..]);

        if (d != Dims)
            throw new InvalidDataException($"Expected {Dims} dimensions, got {d}");

        var offset = 32;

        var centroids = new sbyte[k][];
        for (var c = 0; c < k; c++)
        {
            centroids[c] = new sbyte[Quantization.PaddedDimensions];
            new ReadOnlySpan<byte>(basePtr + offset, Quantization.PaddedDimensions)
                .CopyTo(MemoryMarshal.Cast<sbyte, byte>(centroids[c].AsSpan()));
            offset += Quantization.PaddedDimensions;
        }

        var clusterOffsets = (uint*)(basePtr + offset);
        offset += (k + 1) * 4;

        var dimData = new sbyte*[d];
        for (var j = 0; j < d; j++)
        {
            dimData[j] = (sbyte*)(basePtr + offset);
            offset += n;
        }

        var fraudLabels = basePtr + offset;

        return new IvfSearchEngine(
            mmf, accessor, basePtr, n, k,
            dimData, fraudLabels, clusterOffsets, centroids);
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();
    }

    [SkipLocalsInit]
    public (bool approved, float fraudScore) Search(ReadOnlySpan<float> queryFloat, int nProbe = 5)
    {
        Span<sbyte> queryInt8 = stackalloc sbyte[Quantization.PaddedDimensions];
        Quantization.QuantizeVector(queryFloat, queryInt8);

        Span<(float dist, bool isFraud)> topScores = stackalloc (float, bool)[5];
        var topLen = 0;

        Span<int> topClusters = stackalloc int[nProbe];
        FindTopClusters(queryInt8, topClusters);

        for (var ci = 0; ci < nProbe; ci++)
        {
            var c = topClusters[ci];
            var start = (int)_clusterOffsets[c];
            var end = (int)_clusterOffsets[c + 1];

            if (Sse41.IsSupported)
                ScanClusterSimd(queryInt8, start, end, topScores, ref topLen);
            else
                ScanClusterScalar(queryInt8, start, end, topScores, ref topLen);
        }

        return FraudManager.Detect(topScores, topLen);
    }

    private void FindTopClusters(ReadOnlySpan<sbyte> query, Span<int> topClusters)
    {
        var k = _centroids.Length;
        var nProbe = topClusters.Length;

        Span<(int dist, int idx)> all = stackalloc (int, int)[k];

        for (var c = 0; c < k; c++)
        {
            var centroid = _centroids[c];
            var dist = 0;
            for (var j = 0; j < Dims; j++)
            {
                var diff = query[j] - centroid[j];
                dist += diff * diff;
            }
            all[c] = (dist, c);
        }

        for (var i = 0; i < nProbe; i++)
        {
            var bestIdx = i;
            var bestDist = all[i].dist;
            for (var j = i + 1; j < k; j++)
            {
                if (all[j].dist < bestDist)
                {
                    bestDist = all[j].dist;
                    bestIdx = j;
                }
            }
            topClusters[i] = all[bestIdx].idx;
            (all[i], all[bestIdx]) = (all[bestIdx], all[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ScanClusterSimd(
        ReadOnlySpan<sbyte> query,
        int start, int end,
        Span<(float, bool)> topScores, ref int topLen)
    {
        // SSE4.1 SIMD: process 4 vectors per inner iteration using Vector128<int>.
        // We load 16 sbytes, then use ConvertToVector128Int32 four times to process
        // four groups of 4 sbytes each, accumulating into four Vector128<int>.

        var labels = _fraudLabels;
        var count = end - start;
        var alignedEnd = start + (count & ~15); // multiple of 16
        var buf = stackalloc int[16];

        for (var i = start; i < alignedEnd; i += 16)
        {
            var acc0 = Vector128<int>.Zero;
            var acc1 = Vector128<int>.Zero;
            var acc2 = Vector128<int>.Zero;
            var acc3 = Vector128<int>.Zero;

            for (var dim = 0; dim < Dims; dim++)
            {
                var q = query[dim];
                var qVec = Vector128.Create((int)q);
                var ptr = _dimData[dim] + i;

                // Load 16 sbytes
                var bytes = Sse2.LoadVector128(ptr);

                // Group 0: bytes 0-3
                var g0 = Sse41.ConvertToVector128Int32(bytes);
                var d0 = Sse2.Subtract(qVec, g0);
                acc0 = Sse2.Add(acc0, Sse41.MultiplyLow(d0, d0));

                // Group 1: bytes 4-7
                var shifted1 = Sse2.ShiftRightLogical128BitLane(bytes, 4);
                var g1 = Sse41.ConvertToVector128Int32(shifted1);
                var d1 = Sse2.Subtract(qVec, g1);
                acc1 = Sse2.Add(acc1, Sse41.MultiplyLow(d1, d1));

                // Group 2: bytes 8-11
                var shifted2 = Sse2.ShiftRightLogical128BitLane(bytes, 8);
                var g2 = Sse41.ConvertToVector128Int32(shifted2);
                var d2 = Sse2.Subtract(qVec, g2);
                acc2 = Sse2.Add(acc2, Sse41.MultiplyLow(d2, d2));

                // Group 3: bytes 12-15
                var shifted3 = Sse2.ShiftRightLogical128BitLane(bytes, 12);
                var g3 = Sse41.ConvertToVector128Int32(shifted3);
                var d3 = Sse2.Subtract(qVec, g3);
                acc3 = Sse2.Add(acc3, Sse41.MultiplyLow(d3, d3));
            }

            // Extract 16 distances
            Sse2.Store(buf + 0, acc0);
            Sse2.Store(buf + 4, acc1);
            Sse2.Store(buf + 8, acc2);
            Sse2.Store(buf + 12, acc3);

            for (var j = 0; j < 16; j++)
            {
                FraudManager.Add(buf[j], labels[i + j] == 1, topScores, ref topLen);
            }
        }

        // Scalar tail for remaining 0-15 vectors
        ScanClusterScalar(query, alignedEnd, end, topScores, ref topLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ScanClusterScalar(
        ReadOnlySpan<sbyte> query,
        int start, int end,
        Span<(float, bool)> topScores, ref int topLen)
    {
        var d0 = _dimData[0];
        var d1 = _dimData[1];
        var d2 = _dimData[2];
        var d3 = _dimData[3];
        var d4 = _dimData[4];
        var d5 = _dimData[5];
        var d6 = _dimData[6];
        var d7 = _dimData[7];
        var d8 = _dimData[8];
        var d9 = _dimData[9];
        var d10 = _dimData[10];
        var d11 = _dimData[11];
        var d12 = _dimData[12];
        var d13 = _dimData[13];
        var labels = _fraudLabels;

        var q0 = query[0];
        var q1 = query[1];
        var q2 = query[2];
        var q3 = query[3];
        var q4 = query[4];
        var q5 = query[5];
        var q6 = query[6];
        var q7 = query[7];
        var q8 = query[8];
        var q9 = query[9];
        var q10 = query[10];
        var q11 = query[11];
        var q12 = query[12];
        var q13 = query[13];

        for (var i = start; i < end; i++)
        {
            var diff0 = q0 - d0[i];
            var diff1 = q1 - d1[i];
            var diff2 = q2 - d2[i];
            var diff3 = q3 - d3[i];
            var diff4 = q4 - d4[i];
            var diff5 = q5 - d5[i];
            var diff6 = q6 - d6[i];
            var diff7 = q7 - d7[i];
            var diff8 = q8 - d8[i];
            var diff9 = q9 - d9[i];
            var diff10 = q10 - d10[i];
            var diff11 = q11 - d11[i];
            var diff12 = q12 - d12[i];
            var diff13 = q13 - d13[i];

            var dist = diff0 * diff0 + diff1 * diff1 + diff2 * diff2 + diff3 * diff3
                     + diff4 * diff4 + diff5 * diff5 + diff6 * diff6 + diff7 * diff7
                     + diff8 * diff8 + diff9 * diff9 + diff10 * diff10 + diff11 * diff11
                     + diff12 * diff12 + diff13 * diff13;

            FraudManager.Add(dist, labels[i] == 1, topScores, ref topLen);
        }
    }
}
