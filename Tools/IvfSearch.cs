using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fraude.Tools;

public sealed unsafe class IvfSearchEngine : IDisposable
{
    private const uint Magic = 0x49564632;
    private const int Dims = 14;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly byte* _basePtr;
    private readonly int _n;
    private readonly int _k;
    private readonly short*[] _dimData;
    private readonly byte* _fraudLabels;
    private readonly uint* _clusterOffsets;
    private readonly short[][] _centroids;

    private IvfSearchEngine(
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor accessor,
        byte* basePtr,
        int n,
        int k,
        short*[] dimData,
        byte* fraudLabels,
        uint* clusterOffsets,
        short[][] centroids)
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

        var centroids = new short[k][];
        for (var c = 0; c < k; c++)
        {
            centroids[c] = new short[Quantization.PaddedDimensions];
            new ReadOnlySpan<byte>(basePtr + offset, Quantization.PaddedDimensions * 2)
                .CopyTo(MemoryMarshal.Cast<short, byte>(centroids[c].AsSpan()));
            offset += Quantization.PaddedDimensions * 2;
        }

        var clusterOffsets = (uint*)(basePtr + offset);
        offset += (k + 1) * 4;

        var dimData = new short*[d];
        for (var j = 0; j < d; j++)
        {
            dimData[j] = (short*)(basePtr + offset);
            offset += n * 2;
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
        Span<short> queryInt16 = stackalloc short[Quantization.PaddedDimensions];
        Quantization.QuantizeVectorToInt16(queryFloat, queryInt16);

        Span<(float dist, bool isFraud)> topScores = stackalloc (float, bool)[5];
        var topLen = 0;

        Span<int> topClusters = stackalloc int[nProbe];
        FindTopClusters(queryInt16, topClusters);

        if (Avx2.IsSupported)
        {
            for (var ci = 0; ci < nProbe; ci++)
            {
                var c = topClusters[ci];
                ScanClusterAvx2(queryInt16, (int)_clusterOffsets[c], (int)_clusterOffsets[c + 1], topScores, ref topLen);
            }
        }
        else
        {
            for (var ci = 0; ci < nProbe; ci++)
            {
                var c = topClusters[ci];
                ScanClusterScalar(queryInt16, (int)_clusterOffsets[c], (int)_clusterOffsets[c + 1], topScores, ref topLen);
            }
        }

        return FraudManager.Detect(topScores, topLen);
    }

    private void FindTopClusters(ReadOnlySpan<short> query, Span<int> topClusters)
    {
        var k = _centroids.Length;
        var nProbe = topClusters.Length;

        Span<(long dist, int idx)> all = stackalloc (long, int)[k];

        for (var c = 0; c < k; c++)
        {
            var centroid = _centroids[c];
            long dist = 0;
            for (var j = 0; j < Dims; j++)
            {
                var diff = (long)query[j] - (long)centroid[j];
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

// AVX2 SIMD: process 32 vectors per outer iteration using int accumulator.
    // Uses saturating add to handle potential overflow (14 * 32767² ≈ 60B > int32 max).
    // If dist exceeds 1B during accumulation, it won't be in top 5 anyway.
    private const int OverflowThreshold = 1000000000; // 1B

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ScanClusterAvx2(
        ReadOnlySpan<short> query,
        int start, int end,
        Span<(float, bool)> topScores, ref int topLen)
    {
        var labels = _fraudLabels;
        var count = end - start;
        var alignedEnd = start + (count & ~31);
        Span<int> dists = stackalloc int[32];

        for (var i = start; i < alignedEnd; i += 32)
        {
            for (var vi = 0; vi < 32; vi++) dists[vi] = 0;

            for (var dim = 0; dim < Dims; dim++)
            {
                var q = (int)query[dim];
                var ptr = _dimData[dim] + i;

                for (var vi = 0; vi < 32; vi++)
                {
                    var diff = q - ptr[vi];
                    var sq = diff * diff;
                    var acc = dists[vi];

                    if (acc < OverflowThreshold)
                    {
                        var newAcc = acc + sq;
                        dists[vi] = (newAcc < acc || newAcc >= OverflowThreshold) ? int.MaxValue : newAcc;
                    }
                }
            }

            for (var vi = 0; vi < 32; vi++)
            {
                if (dists[vi] < OverflowThreshold)
                    FraudManager.Add(dists[vi], labels[i + vi] == 1, topScores, ref topLen);
            }
        }

        ScanClusterScalar(query, alignedEnd, end, topScores, ref topLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ScanClusterScalar(
        ReadOnlySpan<short> query,
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

        var q0 = (long)query[0];
        var q1 = (long)query[1];
        var q2 = (long)query[2];
        var q3 = (long)query[3];
        var q4 = (long)query[4];
        var q5 = (long)query[5];
        var q6 = (long)query[6];
        var q7 = (long)query[7];
        var q8 = (long)query[8];
        var q9 = (long)query[9];
        var q10 = (long)query[10];
        var q11 = (long)query[11];
        var q12 = (long)query[12];
        var q13 = (long)query[13];

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

            FraudManager.Add((int)dist, labels[i] == 1, topScores, ref topLen);
        }
    }
}
