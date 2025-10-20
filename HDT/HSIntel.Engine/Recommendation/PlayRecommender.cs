using System;
using HSIntel.Engine.Models;

namespace HSIntel.Engine.Recommendation
{
    /// <summary>
    /// Thin adapter over the internal recommendation flow.
    /// Exists to satisfy documentation checkpoint; not used directly by public APIs.
    /// </summary>
    internal static class PlayRecommender
    {
        public static void Publish(SearchResult result)
        {
            if(result == null)
                throw new ArgumentNullException(nameof(result));
            Services.RecommendationService.UpdateFromResult(result);
        }
    }
}

