using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models
{
    /// <summary>
    /// Snapshot of the observable board for both players at a single point in time.
    /// </summary>
    public sealed class BoardState
    {
        public static BoardState Empty { get; } = new BoardState(
            HeroState.Unknown(ParticipantSide.Friendly),
            HeroState.Unknown(ParticipantSide.Opponent),
            Array.Empty<MinionState>(),
            Array.Empty<MinionState>(),
            Array.Empty<SecretState>(),
            DataQuality.Normal,
            null);

        public BoardState(
            HeroState friendlyHero,
            HeroState opponentHero,
            IEnumerable<MinionState>? friendlyMinions = null,
            IEnumerable<MinionState>? opponentMinions = null,
            IEnumerable<SecretState>? secrets = null,
            DataQuality dataQuality = DataQuality.Normal,
            DateTimeOffset? lastUpdated = null)
        {
            FriendlyHero = friendlyHero ?? throw new ArgumentNullException(nameof(friendlyHero));
            OpponentHero = opponentHero ?? throw new ArgumentNullException(nameof(opponentHero));
            FriendlyMinions = ToReadOnlyList(friendlyMinions);
            OpponentMinions = ToReadOnlyList(opponentMinions);
            Secrets = ToReadOnlyList(secrets);
            DataQuality = dataQuality;
            LastUpdated = lastUpdated;
        }

        public HeroState FriendlyHero { get; }

        public HeroState OpponentHero { get; }

        public IReadOnlyList<MinionState> FriendlyMinions { get; }

        public IReadOnlyList<MinionState> OpponentMinions { get; }

        /// <summary>
        /// Combined list of secrets currently known to be in play.
        /// Consumer may filter by <see cref="SecretState.Controller"/>.
        /// </summary>
        public IReadOnlyList<SecretState> Secrets { get; }

        public DataQuality DataQuality { get; }

        public DateTimeOffset? LastUpdated { get; }

        public BoardState With(
            HeroState? friendlyHero = null,
            HeroState? opponentHero = null,
            IEnumerable<MinionState>? friendlyMinions = null,
            IEnumerable<MinionState>? opponentMinions = null,
            IEnumerable<SecretState>? secrets = null,
            DataQuality? dataQuality = null,
            DateTimeOffset? lastUpdated = null)
        {
            return new BoardState(
                friendlyHero ?? FriendlyHero,
                opponentHero ?? OpponentHero,
                friendlyMinions ?? FriendlyMinions,
                opponentMinions ?? OpponentMinions,
                secrets ?? Secrets,
                dataQuality ?? DataQuality,
                lastUpdated ?? LastUpdated);
        }

        private static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T>? source)
        {
            if(source == null)
                return Array.Empty<T>();

            if(source is IReadOnlyList<T> readOnly)
                return readOnly;

            var list = new List<T>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<T>(list);
        }
    }

    /// <summary>
    /// Immutable representation of a hero on the board.
    /// </summary>
    public sealed class HeroState
    {
        public HeroState(
            ParticipantSide side,
            string? cardId,
            string? cardName,
            string? heroClass,
            string? heroPowerCardId,
            string? heroPowerCardName,
            int? health,
            int? armor,
            WeaponState? weapon = null,
            bool isImmune = false,
            bool isFrozen = false)
        {
            Side = side;
            CardId = cardId;
            CardName = cardName;
            HeroClass = heroClass;
            HeroPowerCardId = heroPowerCardId;
            HeroPowerCardName = heroPowerCardName;
            Health = health;
            Armor = armor;
            Weapon = weapon;
            IsImmune = isImmune;
            IsFrozen = isFrozen;
        }

        public ParticipantSide Side { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public string? HeroClass { get; }

        public string? HeroPowerCardId { get; }

        public string? HeroPowerCardName { get; }

        public int? Health { get; }

        public int? Armor { get; }

        public WeaponState? Weapon { get; }

        public bool IsImmune { get; }

        public bool IsFrozen { get; }

        public static HeroState Unknown(ParticipantSide side) =>
            new HeroState(side, null, null, null, null, null, null, null, null);

        public HeroState With(
            string? cardId = null,
            string? cardName = null,
            string? heroClass = null,
            string? heroPowerCardId = null,
            string? heroPowerCardName = null,
            int? health = null,
            int? armor = null,
            WeaponState? weapon = null,
            bool? isImmune = null,
            bool? isFrozen = null)
        {
            return new HeroState(
                Side,
                cardId ?? CardId,
                cardName ?? CardName,
                heroClass ?? HeroClass,
                heroPowerCardId ?? HeroPowerCardId,
                heroPowerCardName ?? HeroPowerCardName,
                health ?? Health,
                armor ?? Armor,
                weapon ?? Weapon,
                isImmune ?? IsImmune,
                isFrozen ?? IsFrozen);
        }
    }

    /// <summary>
    /// Immutable snapshot of a weapon attached to a hero.
    /// </summary>
    public sealed class WeaponState
    {
        public WeaponState(
            int? entityId,
            string? cardId,
            string? cardName,
            int? attack,
            int? durability,
            bool isPoisonous = false,
            bool hasWindfury = false)
        {
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            Attack = attack;
            Durability = durability;
            IsPoisonous = isPoisonous;
            HasWindfury = hasWindfury;
        }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int? Attack { get; }

        public int? Durability { get; }

        public bool IsPoisonous { get; }

        public bool HasWindfury { get; }
    }

    /// <summary>
    /// Immutable snapshot of a minion positioned on the board.
    /// </summary>
    public sealed class MinionState
    {
        public MinionState(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int? attack,
            int? health,
            int position,
            IReadOnlyCollection<string>? keywords = null,
            bool hasDivineShield = false,
            bool hasTaunt = false,
            bool isStealthed = false,
            bool isFrozen = false,
            bool isDormant = false)
        {
            Controller = controller;
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            Attack = attack;
            Health = health;
            Position = position;
            Keywords = keywords ?? Array.Empty<string>();
            HasDivineShield = hasDivineShield;
            HasTaunt = hasTaunt;
            IsStealthed = isStealthed;
            IsFrozen = isFrozen;
            IsDormant = isDormant;
        }

        public ParticipantSide Controller { get; }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int? Attack { get; }

        public int? Health { get; }

        /// <summary>
        /// Zero-based position from the controller's left-most slot.
        /// </summary>
        public int Position { get; }

        public IReadOnlyCollection<string> Keywords { get; }

        public bool HasDivineShield { get; }

        public bool HasTaunt { get; }

        public bool IsStealthed { get; }

        public bool IsFrozen { get; }

        public bool IsDormant { get; }
    }

    /// <summary>
    /// Immutable snapshot of a revealed or inferred secret.
    /// </summary>
    public sealed class SecretState
    {
        public SecretState(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int turnsInPlay = 0,
            bool wasCreated = false)
        {
            Controller = controller;
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            TurnsInPlay = turnsInPlay;
            WasCreated = wasCreated;
        }

        public ParticipantSide Controller { get; }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int TurnsInPlay { get; }

        public bool WasCreated { get; }
    }
}
