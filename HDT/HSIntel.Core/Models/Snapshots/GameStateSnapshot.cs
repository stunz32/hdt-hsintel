using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HSIntel.Core.Models.Events;

namespace HSIntel.Core.Models.Snapshots
{
    /// <summary>
    /// Raw snapshot from HDT entities prior to transformation into HSIntel state models.
    /// </summary>
    public sealed class GameStateSnapshot
    {
        public GameStateSnapshot(
            HeroDescriptor friendlyHero,
            HeroDescriptor opponentHero,
            IEnumerable<MinionDescriptor>? friendlyMinions,
            IEnumerable<MinionDescriptor>? opponentMinions,
            IEnumerable<SecretDescriptor>? secrets,
            IEnumerable<HandCardDescriptor>? friendlyHand,
            IEnumerable<HandCardDescriptor>? opponentHand,
            ManaDescriptor friendlyMana,
            ManaDescriptor opponentMana,
            int? turnNumber,
            ParticipantSide? activeSide,
            DataQuality dataQuality,
            DateTimeOffset timestamp,
            string? gameId = null,
            int? friendlyHandCount = null,
            int? opponentHandCount = null)
        {
            FriendlyHero = friendlyHero ?? throw new ArgumentNullException(nameof(friendlyHero));
            OpponentHero = opponentHero ?? throw new ArgumentNullException(nameof(opponentHero));
            FriendlyMinions = ToReadOnlyList(friendlyMinions);
            OpponentMinions = ToReadOnlyList(opponentMinions);
            Secrets = ToReadOnlyList(secrets);
            FriendlyHand = ToReadOnlyList(friendlyHand);
            OpponentHand = ToReadOnlyList(opponentHand);
            FriendlyMana = friendlyMana ?? throw new ArgumentNullException(nameof(friendlyMana));
            OpponentMana = opponentMana ?? throw new ArgumentNullException(nameof(opponentMana));
            TurnNumber = turnNumber;
            ActiveSide = activeSide;
            DataQuality = dataQuality;
            Timestamp = timestamp;
            GameId = gameId;
            FriendlyHandCount = friendlyHandCount ?? FriendlyHand.Count;
            OpponentHandCount = opponentHandCount ?? OpponentHand.Count;
        }

        public HeroDescriptor FriendlyHero { get; }

        public HeroDescriptor OpponentHero { get; }

        public IReadOnlyList<MinionDescriptor> FriendlyMinions { get; }

        public IReadOnlyList<MinionDescriptor> OpponentMinions { get; }

        public IReadOnlyList<SecretDescriptor> Secrets { get; }

        public IReadOnlyList<HandCardDescriptor> FriendlyHand { get; }

        public IReadOnlyList<HandCardDescriptor> OpponentHand { get; }

        public ManaDescriptor FriendlyMana { get; }

        public ManaDescriptor OpponentMana { get; }

        public int? TurnNumber { get; }

        public ParticipantSide? ActiveSide { get; }

        public DataQuality DataQuality { get; }

        public DateTimeOffset Timestamp { get; }

        public string? GameId { get; }

        public int FriendlyHandCount { get; }

        public int OpponentHandCount { get; }

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

    public sealed class HeroDescriptor
    {
        public HeroDescriptor(
            ParticipantSide side,
            string? cardId,
            string? cardName,
            string? heroClass,
            string? heroPowerCardId,
            string? heroPowerCardName,
            int? health,
            int? armor,
            WeaponDescriptor? weapon,
            bool isImmune,
            bool isFrozen)
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

        public WeaponDescriptor? Weapon { get; }

        public bool IsImmune { get; }

        public bool IsFrozen { get; }
    }

    public sealed class WeaponDescriptor
    {
        public WeaponDescriptor(
            int? entityId,
            string? cardId,
            string? cardName,
            int? attack,
            int? durability,
            bool isPoisonous,
            bool hasWindfury)
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

    public sealed class MinionDescriptor
    {
        public MinionDescriptor(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int? attack,
            int? health,
            int boardPosition,
            IEnumerable<string>? keywords,
            bool hasDivineShield,
            bool hasTaunt,
            bool isStealthed,
            bool isFrozen,
            bool isDormant)
        {
            Controller = controller;
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            Attack = attack;
            Health = health;
            BoardPosition = boardPosition;
            Keywords = ToReadOnlyList(keywords);
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

        public int BoardPosition { get; }

        public IReadOnlyCollection<string> Keywords { get; }

        public bool HasDivineShield { get; }

        public bool HasTaunt { get; }

        public bool IsStealthed { get; }

        public bool IsFrozen { get; }

        public bool IsDormant { get; }

        private static IReadOnlyCollection<string> ToReadOnlyList(IEnumerable<string>? source)
        {
            if(source == null)
                return Array.Empty<string>();

            if(source is IReadOnlyCollection<string> readOnly)
                return readOnly;

            var list = new List<string>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<string>(list);
        }
    }

    public sealed class SecretDescriptor
    {
        public SecretDescriptor(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int turnsInPlay,
            bool wasCreated)
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

    public sealed class HandCardDescriptor
    {
        public HandCardDescriptor(
            ParticipantSide controller,
            int? entityId,
            string? cardId,
            string? cardName,
            int? baseCost,
            int? effectiveCost,
            bool isKnown,
            bool isPlayable,
            int? zonePosition,
            IEnumerable<CreatedByDescriptor>? createdBy,
            bool wasGeneratedThisTurn)
        {
            Controller = controller;
            EntityId = entityId;
            CardId = cardId;
            CardName = cardName;
            BaseCost = baseCost;
            EffectiveCost = effectiveCost;
            IsKnown = isKnown;
            IsPlayable = isPlayable;
            ZonePosition = zonePosition;
            CreatedBy = ToReadOnlyList(createdBy);
            WasGeneratedThisTurn = wasGeneratedThisTurn;
        }

        public ParticipantSide Controller { get; }

        public int? EntityId { get; }

        public string? CardId { get; }

        public string? CardName { get; }

        public int? BaseCost { get; }

        public int? EffectiveCost { get; }

        public bool IsKnown { get; }

        public bool IsPlayable { get; }

        public int? ZonePosition { get; }

        public IReadOnlyList<CreatedByDescriptor> CreatedBy { get; }

        public bool WasGeneratedThisTurn { get; }

        private static IReadOnlyList<CreatedByDescriptor> ToReadOnlyList(IEnumerable<CreatedByDescriptor>? source)
        {
            if(source == null)
                return Array.Empty<CreatedByDescriptor>();

            if(source is IReadOnlyList<CreatedByDescriptor> readOnly)
                return readOnly;

            var list = new List<CreatedByDescriptor>();

            foreach(var item in source)
                list.Add(item);

            return new ReadOnlyCollection<CreatedByDescriptor>(list);
        }
    }

    public sealed class CreatedByDescriptor
    {
        public CreatedByDescriptor(string? sourceCardId, string? sourceCardName, int? sourceEntityId)
        {
            SourceCardId = sourceCardId;
            SourceCardName = sourceCardName;
            SourceEntityId = sourceEntityId;
        }

        public string? SourceCardId { get; }

        public string? SourceCardName { get; }

        public int? SourceEntityId { get; }
    }

    public sealed class ManaDescriptor
    {
        public ManaDescriptor(
            ParticipantSide owner,
            int? total,
            int? available,
            int? overloaded,
            int? locked,
            int? temporary)
        {
            Owner = owner;
            Total = total;
            Available = available;
            Overloaded = overloaded;
            Locked = locked;
            Temporary = temporary;
        }

        public ParticipantSide Owner { get; }

        public int? Total { get; }

        public int? Available { get; }

        public int? Overloaded { get; }

        public int? Locked { get; }

        public int? Temporary { get; }
    }
}
