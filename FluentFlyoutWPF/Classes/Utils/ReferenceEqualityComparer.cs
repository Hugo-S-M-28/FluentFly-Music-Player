using FluentFlyoutWPF.Models;
using System.Collections.Generic;

namespace FluentFlyoutWPF.Classes.Utils;

internal sealed class TrackReferenceEqualityComparer : IEqualityComparer<TrackModel>
{
    public static readonly TrackReferenceEqualityComparer Instance = new();

    private TrackReferenceEqualityComparer()
    {
    }

    public bool Equals(TrackModel? x, TrackModel? y) => ReferenceEquals(x, y);

    public int GetHashCode(TrackModel obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
