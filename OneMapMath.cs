using System;
using System.Collections.Generic;

namespace SCPSLAlert
{
    /// <summary>
    /// 一图流的抽稀与标注选点。纯函数，离屏可测。
    /// 规则（需求 九.4）：每条折线独立 LTTB，>1000 点抽到 1000；
    /// 每条曲线最多 10 个数字标注，必须含最大 / 最小，其余取极端值并避开拥挤。
    /// </summary>
    internal static class OneMapMath
    {
        public const int DefaultLttb = 1000;
        public const int DefaultLabels = 10;

        public static List<int> ValidIndices(IList<MatchSample> s, Func<MatchSample, bool> ok)
        {
            List<int> list = new List<int>(s.Count);
            for (int i = 0; i < s.Count; i++)
                if (ok(s[i])) list.Add(i);
            return list;
        }

        /// <summary>LTTB 抽稀，返回保留的原始下标（含首尾）。</summary>
        public static List<int> Lttb(IList<MatchSample> s, Func<MatchSample, double> value,
            Func<MatchSample, bool> ok, int max)
        {
            List<int> idx = ValidIndices(s, ok);
            if (max < 3) max = 3;
            if (idx.Count <= max) return idx;

            List<int> keep = new List<int>(max);
            double bucket = (double)(idx.Count - 2) / (max - 2);
            keep.Add(idx[0]);

            int a = 0;
            for (int i = 0; i < max - 2; i++)
            {
                int start = (int)Math.Floor((i + 1) * bucket) + 1;
                int end = (int)Math.Floor((i + 2) * bucket) + 1;
                if (end > idx.Count - 1) end = idx.Count - 1;
                if (start >= end) start = end - 1;
                if (start < 1) start = 1;

                // 下一个桶的平均点
                double avgX = 0, avgY = 0;
                int avgN = 0;
                for (int k = start; k < end; k++)
                {
                    avgX += k;
                    avgY += value(s[idx[k]]);
                    avgN++;
                }
                if (avgN == 0) { avgX = idx[idx.Count - 1]; avgY = value(s[idx[idx.Count - 1]]); }
                else { avgX /= avgN; avgY /= avgN; }

                double ax = a, ay = value(s[idx[a]]);
                double bestArea = -1;
                int best = start;
                for (int k = start; k < end; k++)
                {
                    double area = Math.Abs((ax - avgX) * (value(s[idx[k]]) - ay) - (ax - k) * (avgY - ay));
                    if (area > bestArea) { bestArea = area; best = k; }
                }
                keep.Add(idx[best]);
                a = best;
            }
            keep.Add(idx[idx.Count - 1]);
            return keep;
        }

        /// <summary>
        /// 选数字标注：必含最大/最小，其余取离均值远的局部极值，彼此至少隔开一段。
        /// 返回升序下标；minIndex / maxIndex 是最大最小值的位置（可能不在返回列表里，由调用方决定是否强制加）。
        /// </summary>
        public static List<int> Labels(IList<MatchSample> s, Func<MatchSample, double> value,
            Func<MatchSample, bool> ok, int max, out int minIndex, out int maxIndex)
        {
            return Labels(s, value, ok, max, 24, out minIndex, out maxIndex);
        }

        public static List<int> Labels(IList<MatchSample> s, Func<MatchSample, double> value,
            Func<MatchSample, bool> ok, int max, int minSecondsApart, out int minIndex, out int maxIndex)
        {
            minIndex = -1;
            maxIndex = -1;
            List<int> idx = ValidIndices(s, ok);
            List<int> keep = new List<int>();
            if (idx.Count == 0) return keep;
            if (max <= 0) return keep;

            double lo = double.MaxValue, hi = double.MinValue, sum = 0;
            for (int i = 0; i < idx.Count; i++)
            {
                double v = value(s[idx[i]]);
                if (v < lo) { lo = v; minIndex = idx[i]; }
                if (v > hi) { hi = v; maxIndex = idx[i]; }
                sum += v;
            }
            double mean = sum / idx.Count;
            double range = Math.Max(0.0001, hi - lo);

            AddUnique(keep, minIndex);
            AddUnique(keep, maxIndex);

            // 局部极值候选
            List<int> cand = new List<int>();
            for (int i = 1; i < idx.Count - 1; i++)
            {
                double v = value(s[idx[i]]);
                double prev = value(s[idx[i - 1]]);
                double next = value(s[idx[i + 1]]);
                bool peak = (v >= prev && v >= next) || (v <= prev && v <= next);
                if (!peak) continue;
                if (Math.Abs(v - mean) < range * 0.18) continue;
                cand.Add(idx[i]);
            }
            cand.Sort(delegate(int x, int y)
            {
                double dx = Math.Abs(value(s[x]) - mean);
                double dy = Math.Abs(value(s[y]) - mean);
                return dy.CompareTo(dx);
            });

            for (int i = 0; i < cand.Count && keep.Count < max; i++)
            {
                int at = cand[i];
                bool far = true;
                for (int k = 0; k < keep.Count; k++)
                {
                    if (Math.Abs(keep[k] - at) < minSecondsApart) { far = false; break; }
                }
                if (far) keep.Add(at);
            }
            keep.Sort();
            return keep;
        }

        private static void AddUnique(List<int> list, int v)
        {
            if (v < 0) return;
            for (int i = 0; i < list.Count; i++) if (list[i] == v) return;
            list.Add(v);
        }

        /// <summary>把 0..n-1 的下标转成秒（用于横向定位）。</summary>
        public static double SecondsAt(MatchRound r, int index)
        {
            if (r == null || index < 0 || index >= r.Samples.Count) return 0;
            return Math.Max(0, (r.Samples[index].At - r.Start).TotalSeconds);
        }
    }
}
