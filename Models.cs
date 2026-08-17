public enum GameLanguage
{
    English,
    Chinese
}

class GameSession
{
    public ulong ChannelId { get; set; }

    public string DungeonMaster { get; set; } = "";

    public bool Started { get; set; }

    public int Turn { get; set; }

    public string CurrentScene { get; set; } = "";

    public List<Player> Players { get; set; } = new();

    public GameLanguage Language { get; set; } = GameLanguage.English;

    public List<string> AdventureHistory { get; set; } = new();

    public int DurationMinutes { get; set; } = 60;

    public DateTime? StartedAt { get; set; }

    public bool Ended { get; set; }

    public bool VoiceEnabled { get; set; }

    public ulong? VoiceChannelId { get; set; }

    public bool IsTimeLimitReached =>
        StartedAt.HasValue &&
        !Ended &&
        DateTime.UtcNow >= StartedAt.Value.AddMinutes(DurationMinutes);
}

class Player
{
    public ulong PlayerId { get; set; }

    public string Username { get; set; } = "";

    public Character? Character { get; set; }
}

class Character
{
    public string Name { get; set; } = "";

    public string Class { get; set; } = "";

    public int Level { get; set; } = 1;

    public int HP { get; set; } = 20;

    public int MaxHP { get; set; } = 20;

    public int STR { get; set; } = 10;

    public int DEX { get; set; } = 10;

    public int CON { get; set; } = 10;

    public int INT { get; set; } = 10;

    public int WIS { get; set; } = 10;

    public int CHA { get; set; } = 10;

    public string PrimaryAbility { get; set; } = "INT";

    public string SecondaryAbility { get; set; } = "STR";

    public List<string> Inventory { get; set; } = new();
}

public class ClassAnalysis
{
    public string PrimaryAbility { get; set; } = "INT";

    public string SecondaryAbility { get; set; } = "STR";

    public string Description { get; set; } = "";
}

public class AdventureChoice
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public string Action { get; set; } = "";

    public bool Risky { get; set; }

    public string Ability { get; set; } = "";

    public int DC { get; set; }
}

public class AdventureResponse
{
    public bool RequiresRoll { get; set; }

    public string RollType { get; set; } = "";

    public string Ability { get; set; } = "";

    public int DC { get; set; }

    public string Narration { get; set; } = "";

    public List<AdventureChoice> Choices { get; set; } = new();
}

public static class GameText
{
    public static string NewAdventure(GameLanguage language)
    {
        return language == GameLanguage.Chinese
            ? "🏰 新冒险已创建！"
            : "🏰 New Adventure Created!";
    }

    public static string Language(GameLanguage language)
    {
        return language == GameLanguage.Chinese
            ? "🌐 语言：中文"
            : "🌐 Language: English";
    }

    public static string VoiceOn(GameLanguage language)
    {
        return language == GameLanguage.Chinese
            ? "🔈 语音叙事：关闭（先 `!voice join`，再 `!voice on`）"
            : "🔈 Voice narration: OFF (use `!voice join`, then `!voice on`)";
    }

    public static string Joined(
        GameLanguage language,
        string username)
    {
        return language == GameLanguage.Chinese
            ? $"**{username}** 加入了冒险！"
            : $"**{username}** joined the adventure!";
    }

    public static string CreateCharacter(
        GameLanguage language)
    {
        return language == GameLanguage.Chinese
            ? """
              **创建你的角色**

              请使用以下格式回复：

              `名字 | 职业`

              例如：
              `Aria | Rogue`

              职业可以自由选择，例如：
              Warrior / Rogue / Wizard / Paladin / Ranger / Bard / BattleMage
              """
            : """
              **Create Your Character**

              Please reply using:

              `Name | Class`

              Example:

              `Aria | Rogue`

              Classes can be anything you want:
              Warrior / Rogue / Wizard / Paladin / Ranger / Bard / BattleMage
              """;
    }

    public static string CharacterCreated(
        GameLanguage language,
        string name,
        string characterClass,
        int hp,
        int maxHp,
        int level)
    {
        return language == GameLanguage.Chinese
            ? $"""
              **角色创建成功！**

              名字：{name}
              职业：{characterClass}
              HP：{hp}/{maxHp}
              等级：{level}

              角色已经准备好了。
              所有人就绪后请输入：

              `!start`
              """
            : $"""
              **Character Created!**

              Name: {name}
              Class: {characterClass}
              HP: {hp}/{maxHp}
              Level: {level}

              Your adventure awaits.
              When everyone is ready, type:

              `!start`
              """;
    }
}
