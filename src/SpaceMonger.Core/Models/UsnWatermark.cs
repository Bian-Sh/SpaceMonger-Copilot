namespace SpaceMonger.Core.Models;

public record UsnWatermark(ulong JournalId, long NextUsn, string VolumeRoot);
