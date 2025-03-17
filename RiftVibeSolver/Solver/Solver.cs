﻿using System;
using System.Collections.Generic;

namespace RiftVibeSolver;

public static class Solver {
    private const double VIBE_LENGTH = 5d;

    public static List<Activation> GetActivations(SolverData data, int vibesUsed) {
        var activations = new List<Activation>();
        int fromIndex = data.GetNextVibe(0);

        if (vibesUsed == 2)
            fromIndex = data.GetNextVibe(fromIndex + 1);

        if (fromIndex == data.HitCount)
            return activations;

        var hits = data.Hits;
        var spans = new List<ActivationSpan>(2 * hits.Count);

        for (int i = fromIndex; i < hits.Count; i++)
            spans.Add(GetSpanStartingAfterHit(data, i, vibesUsed));

        for (int i = fromIndex; i < hits.Count; i++)
            GetSpansEndingOnHit(spans, data, i, vibesUsed);

        GetSpansEndingOnBeats(spans, data, vibesUsed);

        if (spans.Count == 0)
            return activations;

        spans.Sort();

        int currentStartIndex = spans[0].StartIndex;
        int currentEndIndex = spans[0].StartIndex;
        int score = 0;

        foreach (var span in spans) {
            if (span.StartIndex <= fromIndex)
                continue;

            while (currentStartIndex < span.StartIndex) {
                score -= hits[currentStartIndex].Score;
                currentStartIndex++;
            }

            while (currentEndIndex < span.EndIndex) {
                score += hits[currentEndIndex].Score;
                currentEndIndex++;
            }

            while (currentEndIndex > span.EndIndex) {
                currentEndIndex--;
                score -= hits[currentEndIndex].Score;
            }

            if (activations.Count > 0 && activations[activations.Count - 1].MinStartTime == span.StartTime) {
                if (activations[activations.Count - 1].EndIndex >= span.EndIndex)
                    continue;

                activations.RemoveAt(activations.Count - 1);
            }

            activations.Add(new Activation(span.StartTime, span.StartIndex, span.EndIndex, score));
        }

        return activations;
    }

    public static List<ActivationRange> GetBestActivations(SolverData data, List<Activation> singleVibeActivations, List<Activation> doubleVibeActivations, out int totalScore) {
        var ranges = new List<ActivationRange>();

        totalScore = 0;

        if (data.HitCount == 0)
            return ranges;

        var bestNextActivationsByFirstVibeIndex = GetBestNextActivationsByFirstVibeIndex(data, singleVibeActivations, doubleVibeActivations, out totalScore);
        var (optimalSingleVibeActivations, optimalDoubleVibeActivations) = GetOptimalActivations(data, singleVibeActivations, doubleVibeActivations, bestNextActivationsByFirstVibeIndex);

        foreach (int index in optimalSingleVibeActivations) {
            var activation = singleVibeActivations[index];

            ranges.Add(new ActivationRange(
                activation.MinStartTime,
                index < singleVibeActivations.Count ? singleVibeActivations[index + 1].MinStartTime : activation.MinStartTime + 1d,
                activation.Score,
                1));
        }

        foreach (int index in optimalDoubleVibeActivations) {
            var activation = doubleVibeActivations[index];

            ranges.Add(new ActivationRange(
                activation.MinStartTime,
                index < doubleVibeActivations.Count ? doubleVibeActivations[index + 1].MinStartTime : activation.MinStartTime + 1d,
                activation.Score,
                2));
        }

        ranges.Sort();

        return ranges;
    }

    public static ActivationSpan GetSpanStartingAt(SolverData data, double startTime, int vibesUsed) => GetSpanStartingAt(data, startTime, data.GetFirstIndexAfter(startTime), vibesUsed);

    private static ActivationSpan GetSpanStartingAt(SolverData data, double startTime, int firstHitIndex, int vibesUsed) {
        var beatData = data.BeatData;
        double currentTime = startTime;
        double vibeRemaining = vibesUsed * VIBE_LENGTH;
        double vibeZeroTime = currentTime + vibeRemaining;
        double endTime = GetEndTime();
        int nextVibeIndex = data.GetNextVibe(firstHitIndex);

        while (nextVibeIndex < data.HitCount) {
            double nextVibeTime = data.GetHitTime(nextVibeIndex);

            if (nextVibeTime > endTime)
                break;

            vibeRemaining = Math.Max(VIBE_LENGTH, Math.Min(vibeRemaining - (nextVibeTime - currentTime) + VIBE_LENGTH, 2d * VIBE_LENGTH));
            currentTime = nextVibeTime;
            vibeZeroTime = currentTime + vibeRemaining;
            endTime = GetEndTime();
            nextVibeIndex = data.GetNextVibe(nextVibeIndex + 1);
        }

        return new ActivationSpan(startTime, firstHitIndex, data.GetFirstIndexAfter(endTime));

        double GetEndTime() {
            double maxEndTime = beatData.GetTimeFromBeat(beatData.GetBeatFromTime(vibeZeroTime) + GetVibeExtensionForBeatLength(beatData.GetBeatLengthAtTime(vibeZeroTime), beatData.BeatDivisions));

            return Math.Min(maxEndTime, data.GetHitTime(data.GetFirstIndexAfter(vibeZeroTime)));
        }
    }

    private static ActivationSpan GetSpanStartingAfterHit(SolverData data, int hitIndex, int vibesUsed) => GetSpanStartingAt(data, data.GetHitTime(hitIndex), hitIndex + 1, vibesUsed);

    private static void GetSpansEndingOnHit(List<ActivationSpan> spans, SolverData data, int hitIndex, int vibesUsed) {
        var beatData = data.BeatData;
        double endTime = data.GetHitTime(hitIndex);
        double endBeat = beatData.GetBeatFromTime(endTime);
        double previousHitTime = data.GetHitTime(hitIndex - 1);
        int endIndex;

        if (data.Hits[hitIndex].GivesVibe)
            endIndex = GetSpanStartingAfterHit(data, hitIndex, 1).EndIndex;
        else
            endIndex = hitIndex + 1;

        if (beatData.BeatTimings.Count <= 1) {
            double vibeZeroTime = beatData.GetTimeFromBeat(endBeat - GetVibeExtensionForBeatLength(beatData.GetBeatLengthForBeat((int) endBeat), beatData.BeatDivisions));

            vibeZeroTime = Math.Max(vibeZeroTime, previousHitTime);
            GetSpansWhereVibeHitsZeroAt(spans, data, vibeZeroTime, endIndex, vibesUsed);

            return;
        }

        for (int i = (int) endBeat; i >= 1; i--) {
            double beatTime = beatData.GetTimeFromBeat(i);
            double nextBeatTime = beatData.GetTimeFromBeat(i + 1);
            double vibeZeroTime = beatData.GetTimeFromBeat(endBeat - GetVibeExtensionForBeatLength(nextBeatTime - beatTime, beatData.BeatDivisions));

            vibeZeroTime = Math.Max(vibeZeroTime, previousHitTime);

            if (vibeZeroTime > beatTime && vibeZeroTime < nextBeatTime)
                GetSpansWhereVibeHitsZeroAt(spans, data, vibeZeroTime, endIndex, vibesUsed);

            if (beatTime <= previousHitTime)
                break;
        }
    }

    private static void GetSpansEndingOnBeats(List<ActivationSpan> spans, SolverData data, int vibesUsed) {
        var beatData = data.BeatData;
        var beatTimings = beatData.BeatTimings;

        if (beatTimings.Count <= 1)
            return;

        for (int i = 1; i < beatTimings.Count - 1; i++) {
            double beatTime = beatTimings[i];
            double endTimeWithPreviousBeatLength = beatData.GetTimeFromBeat(i + 1 + GetVibeExtensionForBeatLength(beatTime - beatTimings[i - 1], beatData.BeatDivisions));
            double endTimeWithNextBeatLength = beatData.GetTimeFromBeat(i + 1 + GetVibeExtensionForBeatLength(beatTimings[i + 1] - beatTime, beatData.BeatDivisions));
            int nextHitIndex = data.GetFirstIndexAfter(beatTime);
            double nextHitTime = data.GetHitTime(nextHitIndex);

            if (!(endTimeWithPreviousBeatLength >= nextHitTime ^ endTimeWithNextBeatLength >= nextHitTime))
                continue;

            int hitIndex = nextHitIndex;

            if (endTimeWithNextBeatLength < nextHitTime)
                hitIndex--;

            int endIndex;

            if (hitIndex < data.HitCount && data.Hits[hitIndex].GivesVibe)
                endIndex = GetSpanStartingAfterHit(data, hitIndex, 1).EndIndex;
            else
                endIndex = hitIndex + 1;

            GetSpansWhereVibeHitsZeroAt(spans, data, beatTime, endIndex, vibesUsed);
        }
    }

    private static void GetSpansWhereVibeHitsZeroAt(List<ActivationSpan> spans, SolverData data, double vibeZeroTime, int endIndex, int vibesUsed) {
        double currentTime = vibeZeroTime;
        double vibeNeeded = 0d;
        int previousVibeIndex = data.GetPreviousVibe(data.GetFirstIndexAfter(currentTime));

        do {
            double previousVibeTime = data.GetHitTime(previousVibeIndex);
            double possibleStartTime = currentTime - (vibesUsed * VIBE_LENGTH - vibeNeeded);
            double needFullVibeAt = currentTime - (2d * VIBE_LENGTH - vibeNeeded);

            if (possibleStartTime > previousVibeTime)
                spans.Add(new ActivationSpan(possibleStartTime, data.GetFirstIndexAfter(possibleStartTime), endIndex));

            if (previousVibeTime < needFullVibeAt)
                break;

            vibeNeeded = Math.Min(vibeNeeded + (currentTime - previousVibeTime) - VIBE_LENGTH, 2d * VIBE_LENGTH);

            if (vibeNeeded <= 0d)
                break;

            currentTime = previousVibeTime;
            previousVibeIndex = data.GetPreviousVibe(previousVibeIndex);
        } while (previousVibeIndex >= 0);
    }

    private static double GetVibeExtensionForBeatLength(double beatLength, int beatDivisions) {
        double hitWindowInBeats = 0.175d / beatLength;

        return hitWindowInBeats - Math.Floor(hitWindowInBeats * beatDivisions) / beatDivisions;
    }

    private static Dictionary<int, BestNextActivations> GetBestNextActivationsByFirstVibeIndex(SolverData data, List<Activation> singleVibeActivations, List<Activation> doubleVibeActivations, out int totalScore) {
        var bestNextActivationsByFirstVibeIndex = new Dictionary<int, BestNextActivations>();

        totalScore = GetBestNextActivations(0).BestNextValue;

        return bestNextActivationsByFirstVibeIndex;

        BestNextActivations GetBestNextActivations(int fromIndex) {
            int firstVibeIndex = data.GetNextVibe(fromIndex);

            if (bestNextActivationsByFirstVibeIndex.TryGetValue(firstVibeIndex, out var bestNextActivations))
                return bestNextActivations;

            int secondVibeIndex = data.GetNextVibe(firstVibeIndex + 1);
            int bestNextValue = 0;
            var bestNextSingleVibeActivations = new List<int>();
            var bestNextDoubleVibeActivations = new List<int>();

            for (int i = 0; i < singleVibeActivations.Count; i++) {
                var nextActivation = singleVibeActivations[i];

                if (nextActivation.StartIndex <= firstVibeIndex)
                    continue;

                if (nextActivation.StartIndex > secondVibeIndex)
                    break;

                int nextActivationValue = nextActivation.Score + GetBestNextActivations(nextActivation.EndIndex).BestNextValue;

                if (nextActivationValue > bestNextValue) {
                    bestNextValue = nextActivationValue;
                    bestNextSingleVibeActivations.Clear();
                }

                if (nextActivationValue == bestNextValue)
                    bestNextSingleVibeActivations.Add(i);
            }

            for (int i = 0; i < doubleVibeActivations.Count; i++) {
                var nextActivation = doubleVibeActivations[i];

                if (nextActivation.StartIndex <= secondVibeIndex)
                    continue;

                int nextActivationValue = nextActivation.Score + GetBestNextActivations(nextActivation.EndIndex).BestNextValue;

                if (nextActivationValue > bestNextValue) {
                    bestNextValue = nextActivationValue;
                    bestNextSingleVibeActivations.Clear();
                    bestNextDoubleVibeActivations.Clear();
                }

                if (nextActivationValue == bestNextValue)
                    bestNextDoubleVibeActivations.Add(i);
            }

            bestNextActivations = new BestNextActivations(bestNextValue, bestNextSingleVibeActivations, bestNextDoubleVibeActivations);
            bestNextActivationsByFirstVibeIndex.Add(firstVibeIndex, bestNextActivations);

            return bestNextActivations;
        }
    }

    private static (HashSet<int>, HashSet<int>) GetOptimalActivations(SolverData data, List<Activation> singleVibeActivations, List<Activation> doubleVibeActivations, Dictionary<int, BestNextActivations> bestNextActivationsByFirstVibeIndex) {
        var optimalSingleVibeActivations = new HashSet<int>();
        var optimalDoubleVibeActivations = new HashSet<int>();

        Traverse(0);

        return (optimalSingleVibeActivations, optimalDoubleVibeActivations);

        void Traverse(int fromIndex) {
            var bestNextActivations = bestNextActivationsByFirstVibeIndex[data.GetNextVibe(fromIndex)];

            foreach (int index in bestNextActivations.BestNextSingleVibeActivations) {
                if (optimalSingleVibeActivations.Contains(index))
                    continue;

                optimalSingleVibeActivations.Add(index);
                Traverse(singleVibeActivations[index].EndIndex);
            }

            foreach (int index in bestNextActivations.BestNextDoubleVibeActivations) {
                if (optimalDoubleVibeActivations.Contains(index))
                    continue;

                optimalDoubleVibeActivations.Add(index);
                Traverse(doubleVibeActivations[index].EndIndex);
            }
        }
    }

    public static List<VibePathSegment> GetVibePath(SolverData data, ActivationSpan span, int vibesUsed) {
        var hits = data.Hits;
        var segments = new List<VibePathSegment>();
        double currentTime = span.StartTime;
        double vibeRemaining = vibesUsed * VIBE_LENGTH;

        for (int i = span.StartIndex; i < span.EndIndex; i++) {
            var hit = hits[i];

            if (!hit.GivesVibe)
                continue;

            segments.Add(new VibePathSegment(currentTime, hit.Time, vibeRemaining, Math.Max(0d, vibeRemaining - (hit.Time - currentTime))));
            vibeRemaining = Math.Max(VIBE_LENGTH, Math.Min(vibeRemaining - (hit.Time - currentTime) + VIBE_LENGTH, 2d * VIBE_LENGTH));
            currentTime = hit.Time;
        }

        segments.Add(new VibePathSegment(currentTime, currentTime + vibeRemaining, vibeRemaining, 0d));

        return segments;
    }
}