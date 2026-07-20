namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The two phases of bringing one day forward. Each is half of that day's slice of overall progress.
/// </summary>
public enum UpdatePhase { Download, Apply }
