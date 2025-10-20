using HSIntel.Core.Models.Snapshots;

namespace HSIntel.Core.Interfaces
{
    /// <summary>
    /// Provides raw HDT-derived snapshots for consumption by the StateBuilder.
    /// </summary>
    public interface IGameStateSource
    {
        GameStateSnapshot CaptureSnapshot();
    }
}
