using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fraude.Tools;

public sealed unsafe class IvfSearchEngine : IDisposable
{
    private const uint Magic = 0x49564632;
    private const int Dims = 14;

    private readonly byte[] _data;
    private readonly GCHandle _dataHandle;
    private readonly byte* _basePtr;
    private readonly int _n;
    private readonly int _k;
    private readonly short*[] _dimData;
    private readonly byte* _fraudLabels;
    private readonly uint* _clusterOffsets;
    private readonly short[][] _centroids;

    private IvfSearchEngine(
        byte[] data,
        GCHandle dataHandle,
        byte* basePtr,
        int n,
        int k,
        short*[] dimData,
        byte* fraudLabels,
        uint* clusterOffsets,
        short[][] centroids)
    {
        _data = data;
        _dataHandle = dataHandle;
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
        var data = File.ReadAllBytes(path);
        var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

        try
        {
            byte* basePtr = (byte*)dataHandle.AddrOfPinnedObject();

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
                data, dataHandle, basePtr, n, k,
                dimData, fraudLabels, clusterOffsets, centroids);
        }
        catch
        {
            dataHandle.Free();
            throw;
        }
    }

    public void Dispose()
    {
        if (_dataHandle.IsAllocated)
            _dataHandle.Free();
    }

    [SkipLocalsInit]
    public (bool approved, float fraudScore) Search(ReadOnlySpan<float> queryFloat, int nProbe = 30)
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

        Span<long> topDistances = stackalloc long[nProbe];
        topDistances.Fill(long.MaxValue);
        topClusters.Fill(-1);

        for (var c = 0; c < k; c++)
        {
            var centroid = _centroids[c];
            long dist = 0;
            for (var j = 0; j < Dims; j++)
            {
                var diff = (long)query[j] - (long)centroid[j];
                dist += diff * diff;
            }

            if (dist >= topDistances[nProbe - 1])
                continue;

            var pos = nProbe - 1;
            while (pos > 0 && dist < topDistances[pos - 1])
            {
                topDistances[pos] = topDistances[pos - 1];
                topClusters[pos] = topClusters[pos - 1];
                pos--;
            }

            topDistances[pos] = dist;
            topClusters[pos] = c;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ScanClusterAvx2(
        ReadOnlySpan<short> query,
        int start, int end,
        Span<(float, bool)> topScores, ref int topLen)
    {
        var labels = _fraudLabels;
        var count = end - start;
        var alignedEnd = start + (count & ~7);
        long* evenDists = stackalloc long[4];
        long* oddDists = stackalloc long[4];
        var q0 = Vector256.Create((int)query[0]);
        var q1 = Vector256.Create((int)query[1]);
        var q2 = Vector256.Create((int)query[2]);
        var q3 = Vector256.Create((int)query[3]);
        var q4 = Vector256.Create((int)query[4]);
        var q5 = Vector256.Create((int)query[5]);
        var q6 = Vector256.Create((int)query[6]);
        var q7 = Vector256.Create((int)query[7]);
        var q8 = Vector256.Create((int)query[8]);
        var q9 = Vector256.Create((int)query[9]);
        var q10 = Vector256.Create((int)query[10]);
        var q11 = Vector256.Create((int)query[11]);
        var q12 = Vector256.Create((int)query[12]);
        var q13 = Vector256.Create((int)query[13]);

        for (var i = start; i < alignedEnd; i += 8)
        {
            var evenAcc = Vector256<long>.Zero;
            var oddAcc = Vector256<long>.Zero;

            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q0, _dimData[0] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q1, _dimData[1] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q2, _dimData[2] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q3, _dimData[3] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q4, _dimData[4] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q5, _dimData[5] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q6, _dimData[6] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q7, _dimData[7] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q8, _dimData[8] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q9, _dimData[9] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q10, _dimData[10] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q11, _dimData[11] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q12, _dimData[12] + i);
            AccumulateSquaredDiff8(ref evenAcc, ref oddAcc, q13, _dimData[13] + i);

            Avx.Store(evenDists, evenAcc);
            Avx.Store(oddDists, oddAcc);

            FraudManager.Add(evenDists[0], labels[i] == 1, topScores, ref topLen);
            FraudManager.Add(oddDists[0], labels[i + 1] == 1, topScores, ref topLen);
            FraudManager.Add(evenDists[1], labels[i + 2] == 1, topScores, ref topLen);
            FraudManager.Add(oddDists[1], labels[i + 3] == 1, topScores, ref topLen);
            FraudManager.Add(evenDists[2], labels[i + 4] == 1, topScores, ref topLen);
            FraudManager.Add(oddDists[2], labels[i + 5] == 1, topScores, ref topLen);
            FraudManager.Add(evenDists[3], labels[i + 6] == 1, topScores, ref topLen);
            FraudManager.Add(oddDists[3], labels[i + 7] == 1, topScores, ref topLen);
        }

        ScanClusterScalar(query, alignedEnd, end, topScores, ref topLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateSquaredDiff8(
        ref Vector256<long> evenAcc,
        ref Vector256<long> oddAcc,
        Vector256<int> q,
        short* values)
    {
        var data = Avx2.ConvertToVector256Int32(Sse2.LoadVector128(values));
        var diff = Avx2.Subtract(q, data);
        var evenSquares = Avx2.Multiply(diff, diff);
        var oddDiff = Avx2.ShiftRightLogical128BitLane(diff.AsByte(), 4).AsInt32();
        var oddSquares = Avx2.Multiply(oddDiff, oddDiff);

        evenAcc = Avx2.Add(evenAcc, evenSquares);
        oddAcc = Avx2.Add(oddAcc, oddSquares);
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

            FraudManager.Add(dist, labels[i] == 1, topScores, ref topLen);
        }
    }
}
