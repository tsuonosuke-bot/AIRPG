namespace GuildSimulator.Game.Data;

/// <summary>
/// セーブJSONの置き場所。コンソール版はファイル、ブラウザ版はlocalStorageを使う。
/// </summary>
public interface ISaveStore
{
    /// <summary>保存先の説明（画面表示用）。</summary>
    string Description { get; }

    Task<bool> ExistsAsync();

    /// <summary>保存済みJSONを返す。無ければ null。</summary>
    Task<string?> ReadAsync();

    Task WriteAsync(string json);
}

/// <summary>ファイルへ保存する <see cref="ISaveStore"/>。</summary>
public sealed class FileSaveStore : ISaveStore
{
    readonly string _path;

    public FileSaveStore(string path) => _path = path;

    public string Description => _path;

    public Task<bool> ExistsAsync() => Task.FromResult(File.Exists(_path));

    public Task<string?> ReadAsync() =>
        Task.FromResult<string?>(File.Exists(_path) ? File.ReadAllText(_path) : null);

    public Task WriteAsync(string json)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, json);
        return Task.CompletedTask;
    }
}
