using System;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Diagnostics;

namespace HSIntel.Core.Interfaces
{
    public interface IStateValidator
    {
        StateValidationResult Validate(GameContext context, DateTimeOffset timestamp);
    }
}
