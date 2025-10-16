using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HSIntel.Core.Interfaces;
using HSIntel.Core.Models;
using HSIntel.Core.Models.Events;
using HSIntel.Core.Models.Snapshots;

namespace Hearthstone_Deck_Tracker.HSIntel
{
	internal sealed class HdtGameStateSource : IGameStateSource
	{
		public GameStateSnapshot CaptureSnapshot()
		{
			var timestamp = DateTimeOffset.UtcNow;
			var game = Core.Game;
			if(game == null || !game.IsRunning)
				return CreateEmptySnapshot(timestamp, DataQuality.Degraded);

			var friendlyPlayer = game.Player;
			var opponentPlayer = game.Opponent;

			var friendlyHero = BuildHeroDescriptor(friendlyPlayer, ParticipantSide.Friendly);
			var opponentHero = BuildHeroDescriptor(opponentPlayer, ParticipantSide.Opponent);

			var friendlyMinions = BuildMinions(friendlyPlayer, ParticipantSide.Friendly);
			var opponentMinions = BuildMinions(opponentPlayer, ParticipantSide.Opponent);
			var secrets = BuildSecrets(opponentPlayer);

			var friendlyHand = BuildHand(friendlyPlayer, ParticipantSide.Friendly, game);
			var opponentHand = BuildHand(opponentPlayer, ParticipantSide.Opponent, game);

			var friendlyMana = BuildManaDescriptor(game.PlayerEntity, ParticipantSide.Friendly);
			var opponentMana = BuildManaDescriptor(game.OpponentEntity, ParticipantSide.Opponent);

			var turnNumber = game.GetTurnNumber();
			var activeSide = DetermineActiveSide(game);

			var dataQuality = friendlyHero == null || opponentHero == null
				? DataQuality.Degraded
				: DataQuality.Normal;

			return new GameStateSnapshot(
				friendlyHero ?? CreateFallbackHero(ParticipantSide.Friendly),
				opponentHero ?? CreateFallbackHero(ParticipantSide.Opponent),
				friendlyMinions,
				opponentMinions,
				secrets,
				friendlyHand,
				opponentHand,
				friendlyMana,
				opponentMana,
				turnNumber == 0 ? (int?)null : turnNumber,
				activeSide,
				dataQuality,
				timestamp,
				game.CurrentGameStats?.GameId?.ToString(),
				friendlyPlayer?.HandCount,
				opponentPlayer?.HandCount);
		}

		private static HeroDescriptor? BuildHeroDescriptor(Player? player, ParticipantSide side)
		{
			if(player == null)
				return null;

			var heroEntity = player.Hero;
			if(heroEntity == null)
				return null;

			var health = heroEntity.Health;
			var armor = heroEntity.GetTag(GameTag.ARMOR);
			var weapon = FindWeaponDescriptor(player);
			var isImmune = heroEntity.HasTag(GameTag.IMMUNE) || heroEntity.GetTag(GameTag.CANT_BE_TARGETED_BY_SPELLS) == 1;
			var isFrozen = heroEntity.HasTag(GameTag.FROZEN);

			return new HeroDescriptor(
				side,
				health,
				armor,
				weapon,
				isImmune,
				isFrozen);
		}

		private static WeaponDescriptor? FindWeaponDescriptor(Player player)
		{
			var weaponEntity = player.PlayerEntities.FirstOrDefault(e => e.IsWeapon && e.IsInPlay);
			if(weaponEntity == null)
				return null;

			var durability = weaponEntity.GetTag(GameTag.DURABILITY);
			var damage = weaponEntity.GetTag(GameTag.DAMAGE);
			var remainingDurability = Math.Max(0, durability - damage);

			return new WeaponDescriptor(
				weaponEntity.Id,
				weaponEntity.CardId,
				weaponEntity.LocalizedName ?? weaponEntity.CardId,
				weaponEntity.Attack,
				remainingDurability,
				weaponEntity.HasTag(GameTag.POISONOUS),
				weaponEntity.HasTag(GameTag.WINDFURY) || weaponEntity.HasTag(GameTag.MEGA_WINDFURY));
		}

		private static IReadOnlyList<MinionDescriptor> BuildMinions(Player? player, ParticipantSide side)
		{
			if(player == null)
				return Array.Empty<MinionDescriptor>();

			var minions = new List<MinionDescriptor>();
			foreach(var entity in player.Board.Where(e => e.IsMinion).OrderBy(e => e.ZonePosition))
			{
				var keywords = BuildKeywords(entity);
				var descriptor = new MinionDescriptor(
					side,
					entity.Id,
					entity.CardId,
					entity.LocalizedName ?? entity.CardId,
					entity.Attack,
					entity.Health,
					Math.Max(0, entity.ZonePosition - 1),
					keywords,
					entity.HasTag(GameTag.DIVINE_SHIELD),
					entity.HasTag(GameTag.TAUNT),
					entity.HasTag(GameTag.STEALTH),
					entity.HasTag(GameTag.FROZEN),
					entity.HasTag(GameTag.DORMANT));
				minions.Add(descriptor);
			}

			return minions;
		}

		private static IReadOnlyList<string> BuildKeywords(Entity entity)
		{
			var keywords = new List<string>();

			if(entity.HasTag(GameTag.WINDFURY) || entity.HasTag(GameTag.MEGA_WINDFURY))
				keywords.Add("Windfury");
			if(entity.HasTag(GameTag.CHARGE))
				keywords.Add("Charge");
			if(entity.HasTag(GameTag.RUSH))
				keywords.Add("Rush");
			if(entity.HasTag(GameTag.LIFESTEAL))
				keywords.Add("Lifesteal");
			if(entity.HasTag(GameTag.POISONOUS))
				keywords.Add("Poisonous");
			if(entity.HasTag(GameTag.REBORN))
				keywords.Add("Reborn");

			var spellDamage = entity.GetTag(GameTag.SPELLPOWER);
			if(spellDamage > 0)
				keywords.Add($"SpellDamage+{spellDamage}");

			return keywords;
		}

		private static IReadOnlyList<SecretDescriptor> BuildSecrets(Player? opponent)
		{
			if(opponent == null)
				return Array.Empty<SecretDescriptor>();

			var secrets = new List<SecretDescriptor>();
			foreach(var entity in opponent.Secrets)
			{
				var turnsInPlay = Math.Max(0, Core.Game.GetTurnNumber() - entity.Info.Turn);
				var descriptor = new SecretDescriptor(
					ParticipantSide.Opponent,
					entity.Id,
					entity.CardId,
					entity.LocalizedName ?? entity.CardId,
					turnsInPlay,
					entity.Info.Created);
				secrets.Add(descriptor);
			}

			return secrets;
		}

		private static IReadOnlyList<HandCardDescriptor> BuildHand(Player? player, ParticipantSide side, GameV2 game)
		{
			if(player == null)
				return Array.Empty<HandCardDescriptor>();

			var descriptors = new List<HandCardDescriptor>();
			foreach(var entity in player.Hand.OrderBy(e => e.ZonePosition))
			{
				var baseCost = entity.Card?.Cost;
				var effectiveCost = entity.Cost;
				var isKnown = !entity.Info.Hidden && entity.HasCardId;
				var createdBy = BuildCreatedByDescriptors(entity, game);

				var descriptor = new HandCardDescriptor(
					side,
					entity.Id,
					entity.CardId,
					entity.LocalizedName ?? entity.CardId,
					baseCost,
					effectiveCost,
					isKnown,
					entity.IsPlayableCard,
					entity.ZonePosition > 0 ? entity.ZonePosition - 1 : (int?)null,
					createdBy,
					entity.Info.Created && entity.Info.Turn == game.GetTurnNumber());

				descriptors.Add(descriptor);
			}

			return descriptors;
		}

		private static IReadOnlyList<CreatedByDescriptor> BuildCreatedByDescriptors(Entity entity, GameV2 game)
		{
			var list = new List<CreatedByDescriptor>();
			var creatorId = entity.Info.GetCreatorId();
			if(creatorId <= 0)
				return list;

			if(game.Entities.TryGetValue(creatorId, out var creator))
			{
				list.Add(new CreatedByDescriptor(
					creator.CardId,
					creator.LocalizedName ?? creator.CardId,
					creator.Id));
			}

			return list;
		}

		private static ManaDescriptor BuildManaDescriptor(Entity? entity, ParticipantSide side)
		{
			if(entity == null)
				return new ManaDescriptor(side, null, null, null, null, null);

			var total = entity.GetTag(GameTag.RESOURCES);
			var used = entity.GetTag(GameTag.RESOURCES_USED);
			var temp = entity.GetTag(GameTag.TEMP_RESOURCES);
			var available = Math.Max(0, total + temp - used);
			var overloaded = entity.GetTag(GameTag.OVERLOAD_LOCKED);
			var locked = entity.GetTag(GameTag.LOCKED_RESOURCES);

			return new ManaDescriptor(
				side,
				total,
				available,
				overloaded,
				locked,
				temp);
		}

		private static ParticipantSide? DetermineActiveSide(GameV2 game)
		{
			if(game.PlayerEntity?.HasTag(GameTag.CURRENT_PLAYER) == true)
				return ParticipantSide.Friendly;
			if(game.OpponentEntity?.HasTag(GameTag.CURRENT_PLAYER) == true)
				return ParticipantSide.Opponent;
			return null;
		}

		private static HeroDescriptor CreateFallbackHero(ParticipantSide side)
		{
			return new HeroDescriptor(
				side,
				null,
				null,
				null,
				false,
				false);
		}

		private static GameStateSnapshot CreateEmptySnapshot(DateTimeOffset timestamp, DataQuality quality)
		{
			return new GameStateSnapshot(
				CreateFallbackHero(ParticipantSide.Friendly),
				CreateFallbackHero(ParticipantSide.Opponent),
				Array.Empty<MinionDescriptor>(),
				Array.Empty<MinionDescriptor>(),
				Array.Empty<SecretDescriptor>(),
				Array.Empty<HandCardDescriptor>(),
				Array.Empty<HandCardDescriptor>(),
				new ManaDescriptor(ParticipantSide.Friendly, null, null, null, null, null),
				new ManaDescriptor(ParticipantSide.Opponent, null, null, null, null, null),
				null,
				null,
				quality,
				timestamp);
		}
	}
}
