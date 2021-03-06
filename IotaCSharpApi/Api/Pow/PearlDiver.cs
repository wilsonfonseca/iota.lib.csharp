﻿using System;
using System.Threading.Tasks;

namespace Iota.Lib.CSharp.Api.Pow
{
    /// <summary>
    ///     (c) 2016 Come-from-Beyond
    ///     See https://github.com/iotaledger/iri/blob/dev/src/main/java/com/iota/iri/hash/PearlDiver.java
    /// </summary>
    public class PearlDiver
    {
        private const int TransactionLength = 8019;
        private const int NumberOfRoundsp81 = 81;

        private const int CurlHashLength = 243;
        private const int CurlStateLength = CurlHashLength * 3;

        private const long HighBits = -1;
        private const long LowBits = 0;
        private static readonly object SyncObj = new object();

        private State _state;

        /// <summary>
        /// </summary>
        /// <param name="transactionTrits"></param>
        /// <param name="minWeightMagnitude"></param>
        /// <param name="numberOfThreads"></param>
        /// <returns></returns>
        // ReSharper disable once RedundantAssignment
        public bool Search(int[] transactionTrits, int minWeightMagnitude, int numberOfThreads)
        {
            if (transactionTrits.Length != TransactionLength)
                throw new ArgumentException($"Invalid transaction trits length: {transactionTrits.Length}");

            if (minWeightMagnitude < 0 || minWeightMagnitude > CurlHashLength)
                throw new ArgumentException($"Invalid min weight magnitude: {minWeightMagnitude}");

            lock (SyncObj)
            {
                _state = State.Running;
            }

            var midCurlStateLow = new long[CurlStateLength];
            var midCurlStateHigh = new long[CurlStateLength];

            // step1
            {
                for (var i = CurlHashLength; i < CurlStateLength; i++)
                {
                    midCurlStateLow[i] = HighBits;
                    midCurlStateHigh[i] = HighBits;
                }

                var offset = 0;
                var curlScratchpadLowStep1 = new long[CurlStateLength];
                var curlScratchpadHighStep1 = new long[CurlStateLength];

                for (var i = (TransactionLength - CurlHashLength) / CurlHashLength; i-- > 0;)
                {
                    for (var j = 0; j < CurlHashLength; j++)
                        switch (transactionTrits[offset++])
                        {
                            case 0:
                                midCurlStateLow[j] = HighBits;
                                midCurlStateHigh[j] = HighBits;
                                break;
                            case 1:
                                midCurlStateLow[j] = LowBits;
                                midCurlStateHigh[j] = HighBits;
                                break;
                            default:
                                midCurlStateLow[j] = HighBits;
                                midCurlStateHigh[j] = LowBits;
                                break;
                        }

                    Transform(midCurlStateLow, midCurlStateHigh,
                        curlScratchpadLowStep1, curlScratchpadHighStep1);
                }

                for (var i = 0; i < 162; i++)
                    switch (transactionTrits[offset++])
                    {
                        case 0:
                            midCurlStateLow[i] = HighBits;
                            midCurlStateHigh[i] = HighBits;
                            break;
                        case 1:
                            midCurlStateLow[i] = LowBits;
                            midCurlStateHigh[i] = HighBits;
                            break;
                        default:
                            midCurlStateLow[i] = HighBits;
                            midCurlStateHigh[i] = LowBits;
                            break;
                    }

                unchecked
                {
                    midCurlStateLow[162 + 0] =
                        (long) 0b1101101101101101101101101101101101101101101101101101101101101101L;
                    midCurlStateHigh[162 + 0] =
                        (long) 0b1011011011011011011011011011011011011011011011011011011011011011L;
                    midCurlStateLow[162 + 1] =
                        (long) 0b1111000111111000111111000111111000111111000111111000111111000111L;
                    midCurlStateHigh[162 + 1] =
                        (long) 0b1000111111000111111000111111000111111000111111000111111000111111L;
                    midCurlStateLow[162 + 2] =
                        0b0111111111111111111000000000111111111111111111000000000111111111L;
                    midCurlStateHigh[162 + 2] =
                        (long) 0b1111111111000000000111111111111111111000000000111111111111111111L;
                    midCurlStateLow[162 + 3] =
                        (long) 0b1111111111000000000000000000000000000111111111111111111111111111L;
                    midCurlStateHigh[162 + 3] =
                        0b0000000000111111111111111111111111111111111111111111111111111111L;
                }
            }

            // step2
            if (numberOfThreads <= 0) numberOfThreads = Math.Max(Environment.ProcessorCount - 1, 1);

            Parallel.For(0, numberOfThreads, threadIndex =>
            {
                var midCurlStateCopyLow = new long[CurlStateLength];
                var midCurlStateCopyHigh = new long[CurlStateLength];
                Array.Copy(midCurlStateLow, 0, midCurlStateCopyLow, 0, CurlStateLength);
                Array.Copy(midCurlStateHigh, 0, midCurlStateCopyHigh, 0, CurlStateLength);
                for (var i = threadIndex; i > 0; i--)
                    Increment(midCurlStateCopyLow, midCurlStateCopyHigh, 162 + CurlHashLength / 9,
                        162 + CurlHashLength / 9 * 2);

                var curlStateLow = new long[CurlStateLength];
                var curlStateHigh = new long[CurlStateLength];
                var curlScratchpadLowStep2 = new long[CurlStateLength];
                var curlScratchpadHighStep2 = new long[CurlStateLength];
                long outMask = 1;

                while (_state == State.Running)
                {
                    Increment(midCurlStateCopyLow, midCurlStateCopyHigh, 162 + CurlHashLength / 9 * 2,
                        CurlHashLength);

                    Array.Copy(midCurlStateCopyLow, 0, curlStateLow, 0, CurlStateLength);
                    Array.Copy(midCurlStateCopyHigh, 0, curlStateHigh, 0, CurlStateLength);

                    Transform(curlStateLow, curlStateHigh, curlScratchpadLowStep2, curlScratchpadHighStep2);

                    var mask = HighBits;
                    for (var i = minWeightMagnitude; i-- > 0;)
                    {
                        mask &= ~(curlStateLow[CurlHashLength - 1 - i] ^ curlStateHigh[CurlHashLength - 1 - i]);
                        if (mask == 0) break;
                    }

                    if (mask == 0) continue;

                    //sync
                    lock (SyncObj)
                    {
                        if (_state == State.Running)
                        {
                            _state = State.Completed;
                            while ((outMask & mask) == 0) outMask <<= 1;

                            for (var i = 0; i < CurlHashLength; i++)
                                transactionTrits[TransactionLength - CurlHashLength + i] =
                                    (midCurlStateCopyLow[i] & outMask) == 0 ? 1
                                    : (midCurlStateCopyHigh[i] & outMask) == 0 ? -1 : 0;
                        }
                    }

                    break;
                }
            });

            return _state == State.Completed;
        }

        /// <summary>
        /// </summary>
        public void Cancel()
        {
            lock (SyncObj)
            {
                _state = State.Cancelled;
            }
        }

        private static void Transform(long[] curlStateLow, long[] curlStateHigh,
            long[] curlScratchpadLow, long[] curlScratchpadHigh)
        {
            var curlScratchpadIndex = 0;
            for (var round = 0; round < NumberOfRoundsp81; round++)
            {
                Array.Copy(curlStateLow, 0, curlScratchpadLow, 0, CurlStateLength);
                Array.Copy(curlStateHigh, 0, curlScratchpadHigh, 0, CurlStateLength);

                for (var curlStateIndex = 0; curlStateIndex < CurlStateLength; curlStateIndex++)
                {
                    var alpha = curlScratchpadLow[curlScratchpadIndex];
                    var beta = curlScratchpadHigh[curlScratchpadIndex];
                    if (curlScratchpadIndex < 365)
                        curlScratchpadIndex += 364;
                    else
                        curlScratchpadIndex -= 365;

                    var gamma = curlScratchpadHigh[curlScratchpadIndex];
                    var delta = (alpha | (~gamma)) & (curlScratchpadLow[curlScratchpadIndex] ^ beta);

                    curlStateLow[curlStateIndex] = ~delta;
                    curlStateHigh[curlStateIndex] = (alpha ^ gamma) | delta;
                }
            }
        }

        private static void Increment(long[] midCurlStateCopyLow,
            long[] midCurlStateCopyHigh, int fromIndex, int toIndex)
        {
            for (var i = fromIndex; i < toIndex; i++)
                if (midCurlStateCopyLow[i] == LowBits)
                {
                    midCurlStateCopyLow[i] = HighBits;
                    midCurlStateCopyHigh[i] = LowBits;
                }
                else
                {
                    if (midCurlStateCopyHigh[i] == LowBits)
                        midCurlStateCopyHigh[i] = HighBits;
                    else
                        midCurlStateCopyLow[i] = LowBits;

                    break;
                }
        }

        private enum State
        {
            Running,
            Cancelled,
            Completed
        }
    }
}