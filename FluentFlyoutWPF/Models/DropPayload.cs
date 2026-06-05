using System.Collections.Generic;

namespace FluentFlyoutWPF.Models;

public enum DropOperation
{
    None,
    FileImport,
    Reorder
}

public class DropPayload
{
    public IReadOnlyList<string>? Files { get; set; }
    public IReadOnlyList<int>? MovedIndices { get; set; }
    public int? TargetIndex { get; set; }
    public DropOperation Operation { get; set; }
}
