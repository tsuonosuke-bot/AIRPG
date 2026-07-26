using GuildSimulator.Game.Data;
using Microsoft.JSInterop;

namespace GuildSimulator.Web;

/// <summary>ブラウザのlocalStorageへセーブする <see cref="ISaveStore"/>。</summary>
public sealed class LocalStorageSaveStore : ISaveStore
{
    const string StorageKey = "guildsim.save1";

    readonly IJSRuntime _js;

    public LocalStorageSaveStore(IJSRuntime js) => _js = js;

    public string Description => "ブラウザ内（この端末のこのブラウザにのみ保存されます）";

    public async Task<bool> ExistsAsync() => await ReadAsync() != null;

    public async Task<string?> ReadAsync() =>
        await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);

    public async Task WriteAsync(string json) =>
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
}
