namespace HdrPilot.Tests.Processes;

/// <summary>Tests, die prozessweite Handle-Zahlen messen, dürfen nicht parallel zu anderen Tests laufen.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessHandleCollection
{
    public const string Name = "Prozess-Handles";
}