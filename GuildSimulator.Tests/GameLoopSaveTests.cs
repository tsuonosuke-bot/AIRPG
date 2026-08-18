using GuildSimulator.Cli;
using GuildSimulator.Game;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class GameLoopSaveTests
{
    [Theory]
    [InlineData(WriteFailureKind.Custom)]
    [InlineData(WriteFailureKind.Io)]
    [InlineData(WriteFailureKind.Unauthorized)]
    public async Task NonFatalWriteExceptionsAreReportedAndGameLoopContinues(
        WriteFailureKind failureKind)
    {
        var store = new ThrowingSaveStore(CreateException(failureKind));

        string text = await CaptureConsoleAsync(
            "S\n\n0\ny\n",
            () => GameLoop.RunAsync(new GameMasterData(), store));

        Assert.Equal(1, store.WriteAttempts);
        Assert.Contains($"セーブに失敗しました: {store.Failure.Message}", text);
        Assert.Contains("ゲーム終了", text);
    }

    [Fact]
    public async Task FatalWriteExceptionIsNotSwallowed()
    {
        var store = new ThrowingSaveStore(new OutOfMemoryException("fatal save failure"));

        await Assert.ThrowsAsync<OutOfMemoryException>(() => CaptureConsoleAsync(
            "S\n",
            () => GameLoop.RunAsync(new GameMasterData(), store)));
    }

    static Exception CreateException(WriteFailureKind failureKind) => failureKind switch
    {
        WriteFailureKind.Io => new IOException("fake I/O failure"),
        WriteFailureKind.Unauthorized => new UnauthorizedAccessException("fake access failure"),
        _ => new FakeSaveStoreException("fake storage failure"),
    };

    static async Task<string> CaptureConsoleAsync(string inputText, Func<Task> action)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader(inputText);
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            await action();
            return output.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    public enum WriteFailureKind
    {
        Custom,
        Io,
        Unauthorized,
    }

    sealed class ThrowingSaveStore : ISaveStore
    {
        public ThrowingSaveStore(Exception failure) => Failure = failure;

        public Exception Failure { get; }
        public int WriteAttempts { get; private set; }
        public string Description => "throwing fake store";

        public Task<bool> ExistsAsync() => Task.FromResult(false);

        public Task<string?> ReadAsync() => Task.FromResult<string?>(null);

        public Task WriteAsync(string json)
        {
            WriteAttempts++;
            throw Failure;
        }
    }

    sealed class FakeSaveStoreException : Exception
    {
        public FakeSaveStoreException(string message) : base(message)
        {
        }
    }
}
