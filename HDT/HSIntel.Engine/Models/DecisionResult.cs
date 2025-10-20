using System;
using HSIntel.Core.Models;

namespace HSIntel.Engine.Models
{
    public sealed class DecisionComputedEventArgs : EventArgs
    {
        public DecisionComputedEventArgs(GameContext context, SearchResult result)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public GameContext Context { get; }

        public SearchResult Result { get; }
    }

    public sealed class DecisionComputationFailedEventArgs : EventArgs
    {
        public DecisionComputationFailedEventArgs(GameContext context, Exception exception)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public GameContext Context { get; }

        public Exception Exception { get; }
    }
}
