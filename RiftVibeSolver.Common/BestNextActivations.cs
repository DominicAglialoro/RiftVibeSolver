﻿using System.Collections.Generic;

namespace RiftVibeSolver.Common;

internal readonly struct BestNextActivations {
    public readonly int BestNextValue;
    public readonly List<int> BestNextSingleVibeActivations;
    public readonly List<int> BestNextDoubleVibeActivations;

    public BestNextActivations(int bestNextValue, List<int> bestNextSingleVibeActivations, List<int> bestNextDoubleVibeActivations) {
        BestNextValue = bestNextValue;
        BestNextSingleVibeActivations = bestNextSingleVibeActivations;
        BestNextDoubleVibeActivations = bestNextDoubleVibeActivations;
    }
}