﻿using System;
using System.Linq;
using System.Numerics;

namespace Fetcho.Common
{
    /// <summary>
    /// Represents a range between two hashes and allows matehmatical operations to be performed on it
    /// </summary>
    public class HashRange
    {
        public MD5Hash MinHash { get; set; }
        public MD5Hash MaxHash { get; set; }

        public bool IsValid { get => MinHash < MaxHash; }

        public decimal CoverageRatio { get => GetCoverageRatio(this); }

        public HashRange() : this(MD5Hash.MinValue, MD5Hash.MaxValue) { }

        public HashRange(MD5Hash minHash, MD5Hash maxHash)
        {
            MinHash = minHash;
            MaxHash = maxHash;
        }

        public bool Contains(MD5Hash hash) => Contains(this, hash);

        public override string ToString() => string.Format("{0} to {1}", MinHash, MaxHash);

        public static decimal[] GetEqualPercentages(int count)
        {
            decimal[] percentages = new decimal[count];
            for (int i = 0; i < count; i++)
                percentages[i] = 1m / (decimal)count;
            return percentages;
        }

        public static decimal GetCoverageRatio(HashRange range)
        {
            if (range == null) throw new ArgumentNullException("range");

            var diff = GetBigIntegerFromBytes(range.MaxHash.Values) - GetBigIntegerFromBytes(range.MinHash.Values);
            var maxdiff = GetBigIntegerFromBytes(MD5Hash.MaxValue.Values) - GetBigIntegerFromBytes(MD5Hash.MinValue.Values);

            return (decimal)(diff / maxdiff);
        }

        public static HashRange[] SegmentRange(HashRange range, decimal[] segmentPercentages)
        {
            if (range == null) throw new ArgumentNullException("range");
            if (segmentPercentages == null) throw new ArgumentNullException("segmentPercentages");

            decimal sum = segmentPercentages.Sum();

            if (sum != 1.0m)
                throw new ArgumentException("SegmentPercentages must add up to 1.0", "segmentPercentages");

            var ranges = new HashRange[segmentPercentages.Length];
            for (int i = 0; i < segmentPercentages.Length; i++)
                ranges[i] = new HashRange();

            var min = GetBigIntegerFromBytes(range.MinHash.Values);
            var max = GetBigIntegerFromBytes(range.MaxHash.Values);

            var difference = max - min;
            var current = min;

            for (int i = 0; i < segmentPercentages.Length; i++)
            {
                ranges[i].MinHash = new MD5Hash(GetBytesFromBigInteger(current, MD5Hash.ExpectedByteLength));
                var segmentSize = (difference * new BigInteger(segmentPercentages[i] * 10000m)) /
                  new BigInteger(10000);
                current += segmentSize;
                ranges[i].MaxHash = new MD5Hash(GetBytesFromBigInteger(current, MD5Hash.ExpectedByteLength)); //.Substring(0, 32);
                current += 1;
            }

            return ranges;
        }

        public static byte[] GetBytesFromBigInteger(BigInteger bigInt, int number)
        {
            byte[] b = bigInt.ToByteArray();

            int extra_bytes = b.Length != number ? 1 : 0;
            b = b.Subset(0, b.Length - extra_bytes);
            b = b.PadLeft<byte>(0, number).Reverse();

            if (b.Length != number)
                throw new ArgumentException("Didnt end with the required number of bytes. Got " + b.Length);

            return b;
        }

        public static BigInteger GetBigIntegerFromBytes(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("bytes");

            return new BigInteger(value.Reverse().Append(new byte[] { 0 }));
        }

        public static bool Contains(HashRange range, MD5Hash hash)
        {
            if (range == null) throw new ArgumentNullException("range");
            if (hash == null) throw new ArgumentNullException("hash");

            return range.MinHash <= hash && range.MaxHash >= hash;
        }
    }
}
