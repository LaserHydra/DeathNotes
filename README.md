**Death Notes** allows you to broadcast deaths of different variety to chat, including information like the weapon used and which body part was hit.

## Localization

```json
{
  "Distance Unit Singular": "meter",
  "Distance Unit Plural": "meters"
}
```

## Configuration

### Messages

The configurable messages consist of these message blocks:

```json
{
    "KillerType": "Player",
    "VictimType": "Player",
    "DamageType": "Bullet",
    "Messages": [
        "{killer} shot {victim} using their {weapon} over a distance of {distance}."
    ]
}
```

These message blocks can be edited, removed or added to your liking.
To disable a message, please just remove the respective message block entirely.
The plugin dynamically decides which message should be used in a certain situation.
Therefor you have to specify the `KillerType`, `VictimType` and `DamageType` describing the situation you want your message to appear in.

The message example above is used when a player kills another player doing bullet damage.
You can use the default configuration as an example on what it looks like when having multiple message blocks, doing this it is important you don't forget comma.

**`*` and `-` can also be used for KillerType, VictimType, or DamageType.**  
- **`*` matches any possible killer/victim/damage type, including a situation with no killer/victim/damage type**
- **`-` matches if there is no killer/victim/damage type. This usually only applies to the killer.**


### Available Killer/Victim Types

```yaml
- Helicopter
- Bradley
- Animal
- Murderer
- Scientist
- Player
- Trap
- Turret
- Barricade
- ExternalWall
- HeatSource
- Fire
- Lock
- ScientistSentry
```


### Available Damage Types

```yaml
- Generic 
- Hunger
- Thirst
- Cold
- Drowned
- Heat
- Bleeding
- Poison
- Suicide
- Bullet
- Slash
- Blunt
- Fall
- Radiation
- Bite
- Stab
- Explosion
- RadiationExposure
- ColdExposure
- Decay
- ElectricShock
- Arrow
```

### Available Variable Placeholders

Always available:
```yaml
- {victim} : Name of the victim
```

Available for deaths involving a killer:
```yaml
- {killer} : Name of the killer
- {bodypart} : Bodypart which was hit
- {distance} : Distance between killer and victim
```

Available for deaths involving a Lock, Trap, or Turret as the killer:
```yaml
- {owner} : Name of the lock/trap/turret owner
```

Available for deaths involving a Player as the killer:
```yaml
- {hp} : Remaining HP of the killer
- {weapon} : Weapon used by the killer
- {attachments} : Attachments used on the killers weapon
```

### Default Configuration

```json
{
  "Translations": {
    "Death Messages": [
      {
        "KillerType": "Player",
        "VictimType": "Player",
        "DamageType": "Bullet",
        "Messages": [
          "{killer} shot {victim} using their {weapon} over a distance of {distance}."
        ]
      },
      {
        "KillerType": "Player",
        "VictimType": "Player",
        "DamageType": "Arrow",
        "Messages": [
          "{victim} was shot by {killer} with their {weapon} over a distance of {distance}."
        ]
      }
      // More messages here
    ],
    "Names": {
      "Boar": "Boar",
      "Bear": "Bear",
      "Scientist": "Scientist"
    },
    "Bodyparts": {
      "Chest": "Chest",
      "Head": "Head",
      "Leg": "Leg"
    },
    "Weapons": {
      "M249": "M249",
      "Spas-12 Shotgun": "Spas-12 Shotgun",
      "LR-300 Assault Rifle": "LR-300 Assault Rifle"
    }
  },
  "Variable Formats": {
    "attachments": " ({value})"
  },
  "Variable Colors": {
    "killer": "#C4FF00",
    "victim": "#C4FF00",
    "weapon": "#C4FF00",
    "attachments": "#C4FF00",
    "distance": "#C4FF00",
    "owner": "#C4FF00"
  },
  "Chat Format": "<color=#838383>[<color=#80D000>DeathNotes</color>] {message}</color>",
  "Chat Icon (SteamID)": "76561198077847390",
  "Show Kills in Console": true,
  "Show Kills in Chat": true,
  "MessageRadius": -1,
  "Use Metric Distance": true
}
```

## For Developers

### Hooks

```csharp
object OnDeathNotice(Dictionary<string, object> data, string message)
// Return false to cancel death message
```