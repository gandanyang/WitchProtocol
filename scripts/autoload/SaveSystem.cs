using Godot;
using Godot.Collections;

namespace MagicThunder.Autoload;

/// <summary>
/// 存档契约（红线，改动前必读 docs/dev/SaveSystem.md）：
///  1) <see cref="SaveVersion"/> 是存档格式契约，只有破坏性结构变更才递增；
///  2) 新增字段必须"可选 + 默认值兜底"，严禁仅因加字段就升版本号；
///  3) 清档/迁移前必须先把旧档备份到 user://save_backup_{版本}.json；
///  4) 读取任何字段都先走 <see cref="Get"/> 拿默认值，拒绝在业务里出现 null 断言。
/// 存档文件：user://save.json（JSON）。正式系统在 SaveSystem 之上再挂业务归档。
/// </summary>
public partial class SaveSystem : Node
{
    public static SaveSystem I { get; private set; } = null!;

    public const int SaveVersion = 1;
    private const string SavePath = "user://save.json";

    private readonly Dictionary<string, Variant> _data = new();

    public override void _EnterTree() { I = this; }
    public override void _ExitTree() { if (I == this) I = null!; }

    public bool HasSave() => FileAccess.FileExists(SavePath);

    public bool Save()
    {
        _data["save_version"] = SaveVersion;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"[SaveSystem] 无法写入存档：{FileAccess.GetOpenError()}");
            return false;
        }
        file.StoreString(Json.Stringify((Variant)_data));
        file.Flush();
        GD.Print($"[SaveSystem] save → {SavePath}");
        return true;
    }

    public bool Load()
    {
        if (!HasSave()) return false;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null) return false;

        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PushError("[SaveSystem] 存档损坏（非对象），拒绝读取");
            return false;
        }
        _data.Clear();
        foreach (var (k, v) in parsed.AsGodotDictionary())
            _data[k.AsString()] = v;

        EnsureDefaults();
        return true;
    }

    /// 兜底表：所有存档字段必须在此给默认值，保证读档永远拿到完整结构。
    private void EnsureDefaults()
    {
        if (!_data.ContainsKey("chapter")) _data["chapter"] = 0;
        if (!_data.ContainsKey("player_depth")) _data["player_depth"] = 0.0;
        if (!_data.ContainsKey("fired_events")) _data["fired_events"] = new Array<string>();
    }

    /// 清档（开发/测试用）。正式迁移前必须备份旧档。
    public void Reset()
    {
        if (HasSave())
        {
            var backup = $"user://save_backup_{SaveVersion}.json";
            if (FileAccess.FileExists(backup)) DirAccess.RemoveAbsolute(backup);
            DirAccess.CopyAbsolute(SavePath, backup);
        }
        _data.Clear();
        EnsureDefaults();
        Save();
    }

    public bool Contains(string key) => _data.ContainsKey(key);

    /// 读字段；不存在的返回 <paramref name="fallback"/>（Override 必须是默认值）。
    public Variant Get(string key, Variant fallback = default)
    {
        return _data.TryGetValue(key, out var v) ? v : fallback;
    }

    public void Set(string key, Variant value)
    {
        _data[key] = value;
    }
}