namespace FlowPack.Core;

/// <summary>Progress within a real stage; a null total means discovery is still in progress.</summary>
public sealed record OperationProgress(string Stage, long Completed = 0, long? Total = null, string Unit = "项");
