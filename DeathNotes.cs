﻿// #define DEBUG

using Newtonsoft.Json;
using Oxide.Core;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Death Notes", "LaserHydra", "6.0.0")]
	public class DeathNotes : RustPlugin
	{
		#region Fields

		private const string WildcardCharacter = "*";

		private static DeathNotes _instance;

		private PluginConfiguration _configuration;

		private readonly RemoteConfiguration<Dictionary<string, string>> _enemyPrefabs = new RemoteConfiguration<Dictionary<string, string>>("EnemyPrefabs.json");
		private readonly RemoteConfiguration<Dictionary<string, string>> _weaponPrefabs = new RemoteConfiguration<Dictionary<string, string>>("WeaponPrefabs.json");
		private readonly RemoteConfiguration<Dictionary<string, CombatEntityType>> _combatEntityTypes = new RemoteConfiguration<Dictionary<string, CombatEntityType>>("CombatEntityTypes.json");

		private readonly Regex _colorTagRegex = new Regex(@"<color=.{0,7}>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private readonly Regex _sizeTagRegex = new Regex(@"<size=\d*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private readonly List<string> _richTextLiterals = new List<string>
		{
			"</color>", "</size>", "<b>", "</b>", "<i>", "</i>"
		};

		private readonly Dictionary<ulong, AttackInfo> _previousAttack = new Dictionary<ulong, AttackInfo>();

		private readonly Func<PluginConfiguration.DeathMessage, DeathData, bool>[] _messageMatchingStages =
		{
			(m, d) => MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter
		};

		#endregion

		#region Hooks

		private void Init()
		{
			_instance = this;

			_configuration = Config.ReadObject<PluginConfiguration>();
			_configuration.LoadDefaults();
			Config.WriteObject(_configuration);

			_enemyPrefabs.Load();
			_weaponPrefabs.Load();
			_combatEntityTypes.Load();
		}

		private void OnEntityTakeDamage(BaseCombatEntity victimEntity, HitInfo hitInfo)
		{
			if (!(victimEntity is BasePlayer))
				return;

			// Don't track bleeding
			if (victimEntity.lastDamage == DamageType.Bleeding)
				return;

			var userId = victimEntity.ToPlayer().userID;

			_previousAttack[userId] = new AttackInfo
			{
				HitInfo = hitInfo,
				Attacker = victimEntity.lastAttacker ?? hitInfo?.Initiator,
				DamageType = victimEntity.lastDamage
			};
		}

		private void OnEntityDeath(BaseCombatEntity victimEntity, HitInfo hitInfo)
		{
			// Ignore - there is no victim for some reason
			if (victimEntity == null)
				return;

			var data = new DeathData
			{
				VictimEntity = victimEntity,
				KillerEntity = victimEntity.lastAttacker ?? hitInfo?.Initiator,
				VictimEntityType = GetCombatEntityType(victimEntity),
				KillerEntityType = GetCombatEntityType(victimEntity.lastAttacker),
				DamageType = victimEntity.lastDamage,
				HitInfo = hitInfo
			};

#if DEBUG
			LogDebug("[DEATHNOTES DEBUG]");
			LogDebug($"VictimEntity: {data.VictimEntity?.GetType().Name ?? "NULL"} / {data.VictimEntity?.name ?? "NULL"}");
			LogDebug($"KillerEntity: {data.KillerEntity?.GetType().Name ?? "NULL"} / {data.KillerEntity?.name ?? "NULL"}");
			LogDebug($"VictimEntityType: {data.VictimEntityType}");
			LogDebug($"KillerEntityType: {data.KillerEntityType}");
			LogDebug($"DamageType: {data.DamageType}");
			LogDebug($"Bodypart: {GetCustomizedBodypartName(data.HitInfo)}");
#endif

			// Handle inconsistencies/exceptions
			HandleExceptions(ref data);

			// Ignore deaths of other entities
			if (data.KillerEntityType == CombatEntityType.Other || data.VictimEntityType == CombatEntityType.Other)
				return;

			// Ignore deaths which don't involve players
			if (data.VictimEntityType != CombatEntityType.Player && data.KillerEntityType != CombatEntityType.Player)
				return;

			// Populate the variables in the message
			string message = PopulateMessageVariables(
				// Find the best matching death message for this death
				GetDeathMessage(data),
				data
			);

			if (message == null)
				return;

			Interface.Call("OnDeathNotice", data.ToDictionary(), message);

			if (_configuration.ShowInChat)
				Server.Broadcast(_configuration.ChatFormat.Replace("{message}", message), null, ulong.Parse(_configuration.ChatIcon));

			if (_configuration.ShowInConsole)
				Puts(StripRichText(message));
		}

		private void OnFlameThrowerBurn(FlameThrower flameThrower, BaseEntity baseEntity)
		{
			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.Flamethrower;
			flame.SourceEntity = flameThrower;
			flame.Initiator = flameThrower.GetOwnerPlayer();
		}

		private void OnFlameExplosion(FlameExplosive explosive, BaseEntity baseEntity)
		{
			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.IncendiaryProjectile;
			flame.SourceEntity = explosive;
			flame.Initiator = explosive.creatorEntity;
		}

		private void OnFireBallSpread(FireBall fireBall, BaseEntity newFire)
		{
			var flame = fireBall.GetComponent<Flame>();
			if (flame != null)
			{
				var newFlame = newFire.gameObject.AddComponent<Flame>();
				newFlame.Source = flame.Source;
				newFlame.SourceEntity = flame.SourceEntity;
				newFlame.Initiator = flame.Initiator;
			}
		}

		private void OnFireBallDamage(FireBall fireBall, BaseCombatEntity target, HitInfo hitInfo) => hitInfo.Initiator = fireBall;

		#endregion

		#region Death Messages

		private string GetDeathMessage(DeathData data)
		{
			foreach (var matchingStage in _messageMatchingStages)
			{
				var match = _configuration.Translations.Messages.Find(m => matchingStage.Invoke(m, data));

				if (match != null && match.Messages.Length != 0)
					return match.Messages.GetRandom((uint) DateTime.UtcNow.Millisecond);
			}

			return null;
		}

		private string PopulateMessageVariables(string message, DeathData data)
		{
			if (string.IsNullOrEmpty(message))
				return null;

			var replacements = new Dictionary<string, string>
			{
				["killer"] = GetCustomizedEntityName(data.KillerEntity, data.KillerEntityType),
				["victim"] = GetCustomizedEntityName(data.VictimEntity, data.VictimEntityType),
				["bodypart"] = GetCustomizedBodypartName(data.HitInfo)
			};

			if (data.KillerEntity != null)
			{
				var distance = data.KillerEntity.Distance(data.VictimEntity);
				replacements.Add("distance", GetDistance(distance, _configuration.UseMetricDistance));

				if (data.KillerEntityType == CombatEntityType.Turret)
				{
					replacements.Add("owner",
						covalence.Players.FindPlayerById(data.KillerEntity.OwnerID.ToString())?.Name ?? "unknown owner"
					); // TODO: Work on the potential unknown owner case
				}
				else if (data.KillerEntityType == CombatEntityType.Lock)
				{
					replacements.Add("owner",
						covalence.Players.FindPlayerById(data.KillerEntity.OwnerID.ToString())?.Name ?? "unknown owner"
					); // TODO: Work on the potential unknown owner case
				}
			}

			if (data.KillerEntityType == CombatEntityType.Player)
			{
				replacements.Add("weapon", GetCustomizedWeaponName(data.HitInfo));
				replacements.Add("attachments", string.Join(", ", GetWeaponAttachments(data.HitInfo).ToArray()));
			}
			
			message = InsertPlaceholderValues(message, replacements);

			replacements = null;
			return message;
		}

		private struct DeathData
		{
			public CombatEntityType VictimEntityType { get; set; }
			public BaseCombatEntity VictimEntity { get; set; }

			public CombatEntityType KillerEntityType { get; set; }
			public BaseEntity KillerEntity { get; set; }

			public DamageType DamageType { get; set; }
			public HitInfo HitInfo { get; set; }

			public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
			{
				["VictimEntityType"] = VictimEntityType,
				["VictimEntity"] = VictimEntity,
				["KillerEntityType"] = KillerEntityType,
				["KillerEntity"] = KillerEntity,
				["DamageType"] = DamageType,
				["HitInfo"] = HitInfo
			};
		}

		#endregion

		#region Entity Identification

		private CombatEntityType GetCombatEntityType(BaseEntity entity)
		{
			if (entity == null)
				return CombatEntityType.None;

			if (_combatEntityTypes.Contents != null && _combatEntityTypes.Contents.ContainsKey(entity.GetType().Name))
				return _combatEntityTypes.Contents[entity.GetType().Name];

			if (entity is Scientist)
				return CombatEntityType.Scientist;

			if (entity is BaseOven)
				return CombatEntityType.HeatSource;

			if (entity is SimpleBuildingBlock)
				return CombatEntityType.ExternalWall;

			if (entity is BaseAnimalNPC)
				return CombatEntityType.Animal;

			if (entity is BaseTrap)
				return CombatEntityType.Trap;

			if (entity is Barricade)
				return CombatEntityType.Barricade;

			return CombatEntityType.Other;
		}

		private string GetCustomizedEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			var name = GetEntityName(entity, combatEntityType);

			if (string.IsNullOrEmpty(name))
				return null;

			// Don't load player names into config
			if (combatEntityType == CombatEntityType.Player)
				return name;

			if (!_configuration.Translations.Names.ContainsKey(name))
			{
				_configuration.Translations.Names.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Names[name];
		}

		private string GetEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			switch (combatEntityType)
			{
				case CombatEntityType.Player:
					return StripRichText(entity.ToPlayer().displayName);

				case CombatEntityType.Helicopter:
					return "Helicopter";

				case CombatEntityType.Murderer:
					return "Murderer";

				case CombatEntityType.Scientist:
					return "Scientist";

				case CombatEntityType.Bradley:
					return "Bradley APC";

				case CombatEntityType.Fire:
					return entity.creatorEntity?.ToPlayer()?.displayName ?? "Fire";
			}

			if (entity == null)
				return null;

			if (_enemyPrefabs.Contents.ContainsKey(entity.ShortPrefabName))
				return _enemyPrefabs.Contents[entity.ShortPrefabName];

			return HumanizePascalCase(entity.GetType().Name);
		}

		private enum CombatEntityType
		{
			Helicopter = 0,
			Bradley = 1,
			Animal = 2,
			Murderer = 3,
			Scientist = 4,
			Player = 5,
			Trap = 6,
			Turret = 7,
			Barricade = 8,
			ExternalWall = 9,
			HeatSource = 10,
			Fire = 11,
			Lock = 12,
			Other = 13,
			None = 14
		}

		#endregion

		#region Workarounds and Inconsistency/Exception Handling

		private void HandleExceptions(ref DeathData data)
		{
			if (data.KillerEntity is FireBall)
				data.DamageType = DamageType.Heat;

			// Get previous attacker when bleeding out
			if (data.VictimEntityType == CombatEntityType.Player && (data.DamageType == DamageType.Bleeding || data.HitInfo == null))
			{
				var userId = data.VictimEntity.ToPlayer().userID;

				if (_previousAttack.ContainsKey(userId))
				{
					var attack = _previousAttack[userId];
					data.KillerEntity = attack.Attacker;
					data.KillerEntityType = GetCombatEntityType(data.KillerEntity);

					// Restore previous hitInfo for weapon determination
					if (attack.HitInfo != null)
						data.HitInfo = attack.HitInfo;

					// Use previous damagetype if this is a selfinflicted death,
					// so falling to death etc. is also shown when wounded and bleeding out
					if (data.KillerEntity == null || data.KillerEntity == data.VictimEntity)
						data.DamageType = attack.DamageType;
					else
						data.DamageType = DamageType.Bleeding;
				}
			}

			// Workaround for deaths caused by flamethrower or rocket fire 
			var flame = data.KillerEntity?.gameObject?.GetComponent<Flame>();
			if (flame != null)
			{
				data.KillerEntity = flame.Initiator;
				data.KillerEntityType = CombatEntityType.Player;
				return;
			}

			// Bradley kill with main cannon
			if (data.HitInfo?.WeaponPrefab?.ShortPrefabName == "maincannonshell")
			{
				data.KillerEntityType = CombatEntityType.Bradley;
				return;
			}
		}

		private struct AttackInfo
		{
			public HitInfo HitInfo { get; set; }
			public DamageType DamageType { get; set; }
			public BaseEntity Attacker { get; set; }
		}

		private class Flame : MonoBehaviour
		{
			public FlameSource Source { get; set; }
			public BaseEntity SourceEntity { get; set; }
			public BaseEntity Initiator { get; set; }

			public enum FlameSource
			{
				Flamethrower,
				IncendiaryProjectile
			}

			public override string ToString()
			{
				return $"{Initiator} {SourceEntity} ({Source})";
			}
		}

		#endregion

		#region Weapons

		private string GetCustomizedWeaponName(HitInfo hitInfo)
		{
			var name = GetWeaponName(hitInfo);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Weapons.ContainsKey(name))
			{
				_configuration.Translations.Weapons.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Weapons[name];
		}

		private string GetWeaponName(HitInfo hitInfo)
		{
			if (hitInfo == null)
				return null;

			var item = hitInfo.Weapon?.GetItem()?.info;

			if (item != null)
				return item.displayName.english;

			var prefab = hitInfo.Initiator?.gameObject?.GetComponent<Flame>()?.SourceEntity?.ShortPrefabName ??
			             hitInfo.WeaponPrefab?.ShortPrefabName;

			if (prefab != null)
			{
				if (_weaponPrefabs.Contents.ContainsKey(prefab))
					return _weaponPrefabs.Contents[prefab];

				return prefab;
			}

			return null;
		}

		private static List<string> GetWeaponAttachments(HitInfo info)
		{
			var item = info?.Weapon?.GetItem();
			return item?.contents?.itemList?.Select(i => i.info.displayName.english).ToList() ?? new List<string>();
		}

		#endregion

		#region Bodyparts

		private string GetCustomizedBodypartName(HitInfo hitInfo)
		{
			var name = GetBodypartName(hitInfo);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Bodyparts.ContainsKey(name))
			{
				_configuration.Translations.Bodyparts.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Bodyparts[name];
		}

		private string GetBodypartName(HitInfo hitInfo)
		{
			var hitArea = hitInfo.boneArea;
			return (int) hitArea == -1 ? "Body" : hitArea.ToString();
		}

		#endregion

		#region Helper

		private void LogDebug(string text)
		{
			if (BasePlayer.activePlayerList.Count >= 1)
			{
				BasePlayer.activePlayerList[0].ConsoleMessage($"<color=orange>{text}</color>");
			}
		}

		private static string GetDistance(float meters, bool useMetric)
		{
			double value = Math.Round(useMetric ? meters : meters * 3.28f, 1);
			string unit = value == 1
				? _instance.lang.GetMessage("Distance Unit Singular", _instance)
				: _instance.lang.GetMessage("Distance Unit Plural", _instance);
			
			return $"{value} {unit}";
		}

		private static string ApplyVariableFormat(string text, string variableName)
		{
			if (_instance._configuration.VariableFormats.ContainsKey(variableName))
			{
				var format = _instance._configuration.VariableFormats[variableName];
				text = format.Replace("{value}", text);
			}

			return text;
		}

		private static string InsertPlaceholderValues(string text, Dictionary<string, string> values)
		{
			foreach (var kvp in values)
			{
				string value = ApplyVariableFormat(kvp.Value, kvp.Key);
				if (string.IsNullOrEmpty(kvp.Value))
				{
					text = text.Replace($"{{{kvp.Key}}}", string.Empty);
				}
				else if (_instance._configuration.VariableColors.ContainsKey(kvp.Key))
				{
					var color = _instance._configuration.VariableColors[kvp.Key];
					text = text.Replace($"{{{kvp.Key}}}", $"<color={color}>{value}</color>");
					color = null;
				}
				else
				{
					text = text.Replace($"{{{kvp.Key}}}", value);
				}
			}

			return text;
		}

		private static string HumanizePascalCase(string text)
		{
			var sb = new StringBuilder();

			foreach (char c in text)
			{
				if (char.IsUpper(c) && sb.Length != 0 && !char.IsUpper(sb[sb.Length - 1]))
					sb.Append(" ");

				sb.Append(c);
			}

			return sb.ToString();
		}

		private string StripRichText(string text)
		{
			text = _colorTagRegex.Replace(text, string.Empty);
			text = _sizeTagRegex.Replace(text, string.Empty);

			foreach (var richTextLiteral in _richTextLiterals)
				text = text.Replace(richTextLiteral, string.Empty, StringComparison.InvariantCulture);

			return text;
		}

		private static bool MatchesCombatEntityType(CombatEntityType combatEntityType, string text)
		{
			if (combatEntityType == CombatEntityType.None && text == "-")
				return true;

			return combatEntityType.ToString().Equals(text, StringComparison.InvariantCulture);
		}

		private static bool MatchesDamageType(DamageType damageType, string text)
		{
			return damageType.ToString().Equals(text, StringComparison.InvariantCulture);
		}

		#endregion

		#region Configuration

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Distance Unit Singular"] = "meter",
				["Distance Unit Plural"] = "meters"
			}, this);
		}

		protected override void LoadDefaultConfig() => PrintWarning("Generating new configuration file...");

		private sealed class PluginConfiguration
		{
			[JsonProperty("Translations")]
			public Translation Translations = new Translation();

			[JsonProperty("Variable Formats")]
			public Dictionary<string, string> VariableFormats = new Dictionary<string, string>
			{
				["attachments"] = " ({value})"
			};

			[JsonProperty("Variable Colors")]
			public Dictionary<string, string> VariableColors = new Dictionary<string, string>
			{
				["killer"] = "#C4FF00",
				["victim"] = "#C4FF00",
				["weapon"] = "#C4FF00",
				["attachments"] = "#C4FF00",
				["distance"] = "#C4FF00",
				["owner"] = "#C4FF00"
			};

			[JsonProperty("Chat Format")]
			public string ChatFormat = "<color=#838383>[<color=#80D000>DeathNotes</color>] {message}</color>";

			[JsonProperty("Chat Icon (SteamID)")]
			public string ChatIcon = "76561198077847390";

			[JsonProperty("Show Kills in Console")]
			public bool ShowInConsole = true;

			[JsonProperty("Show Kills in Chat")]
			public bool ShowInChat = true;

			[JsonProperty("Use Metric Distance")]
			public bool UseMetricDistance = true;

			public void LoadDefaults()
			{
				if (Translations.Messages == null)
				{
					var defaults = new RemoteConfiguration<List<DeathMessage>>("DefaultMessages.json");
					defaults.Load(success =>
					{
						if (success)
						{
							Translations.Messages = defaults.Contents;
							_instance.Config.WriteObject(this);
						}
					});
				}
			}

			public class DeathMessage
			{
				public string KillerType { get; set; }
				public string VictimType { get; set; }
				public string DamageType { get; set; }

				public string[] Messages { get; set; }

				protected bool Equals(DeathMessage other) => string.Equals(KillerType, other.KillerType) &&
				                                             string.Equals(VictimType, other.VictimType) &&
				                                             string.Equals(DamageType, other.DamageType);
			}

			public class Translation
			{
				[JsonProperty("Death Messages")]
				public List<DeathMessage> Messages;

				[JsonProperty("Names")]
				public Dictionary<string, string> Names = new Dictionary<string, string>();
				
				[JsonProperty("Bodyparts")]
				public Dictionary<string, string> Bodyparts = new Dictionary<string, string>();

				[JsonProperty("Weapons")]
				public Dictionary<string, string> Weapons = new Dictionary<string, string>();
			}
		}

		private sealed class RemoteConfiguration<T>
		{
			private const string Host = "https://gitlab.com/laserhydra/RemotePluginConfigurations/raw/master/DeathNotes/";

			private readonly string _file;

			public RemoteConfiguration(string file)
			{
				_file = file;
			}

			public T Contents { get; private set; }
			private string ExactUrl => Host + _file;

			public void Load(Action<bool> callback = null)
			{
				_instance.webrequest.Enqueue(ExactUrl, string.Empty, (code, response) =>
				{
					try
					{
						Contents = JsonConvert.DeserializeObject<T>(response);
						callback?.Invoke(true);
					}
					catch (Exception)
					{
						_instance.PrintError($"Could not load remote config '{_file}'. Please report the issue to the plugin author if this is happening frequently.");
						callback?.Invoke(false);
					}
				}, _instance);
			}
		}

		#endregion
	}
}