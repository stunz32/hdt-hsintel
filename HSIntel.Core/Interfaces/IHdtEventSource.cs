using System;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Models.Events.Raw;

namespace HSIntel.Core.Interfaces
{
    public interface IHdtEventSource : IDisposable
    {
        void RegisterGameStarted(Action handler);

        void RegisterGameEnded(Action handler);

        void RegisterTurnStarted(Action<RawTurnStartEvent> handler);

        void RegisterPlayerCardDrawn(Action<RawCardEvent> handler);

        void RegisterOpponentCardDrawn(Action<RawCardEvent> handler);

        void RegisterPlayerCardPlayed(Action<RawCardEvent> handler);

        void RegisterOpponentCardPlayed(Action<RawCardEvent> handler);

        void RegisterPlayerCardDiscarded(Action<RawCardEvent> handler);

        void RegisterOpponentCardDiscarded(Action<RawCardEvent> handler);

        void RegisterAttackOccurred(Action<RawAttackEvent> handler);

        void RegisterHeroPowerUsed(Action<RawHeroPowerEvent> handler);

        void RegisterDeckSelected(Action<RawDeckSelectedEvent> handler);

        void RegisterUpdateTick(Action<RawUpdateTickEvent> handler);

        void RegisterOpponentMulliganSummary(Action<RawOpponentMulliganSummaryEvent> handler);

        void RegisterOpponentCardCreated(Action<RawCardCreatedEvent> handler);

        RawSecretsSnapshot CaptureOpponentSecretsSnapshot();

        RawHandOddsSnapshot CaptureOpponentHandOddsSnapshot(ParticipantSide activeSide);

        void RegisterSecretsChanged(Action handler);
    }
}
