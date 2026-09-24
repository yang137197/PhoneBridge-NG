using PhoneBridge.Desktop;

namespace PhoneBridge.Desktop.Tests;

[TestClass]
public sealed class AutoStartTests
{
    private const string Exe = @"C:\Program Files\PhoneBridge NG\PhoneBridge.Desktop.exe";

    [TestMethod]
    public void CommandQuotesExecutableAndUsesExactStartupArgument()
    {
        var manager = new AutoStartManager(new FakeStore(), Exe);
        Assert.AreEqual("\"C:\\Program Files\\PhoneBridge NG\\PhoneBridge.Desktop.exe\" --startup", manager.Command);
    }

    [TestMethod]
    public void MissingEntryIsDisabledAndEnableWritesThenVerifiesExactCommand()
    {
        var store = new FakeStore(); var manager = new AutoStartManager(store, Exe);
        Assert.AreEqual(AutoStartState.Disabled, manager.GetState());
        manager.SetEnabled(true);
        Assert.AreEqual(AutoStartState.Enabled, manager.GetState());
        Assert.AreEqual(manager.Command, store.Value.Command); Assert.AreEqual(1, store.Writes);
    }

    [TestMethod]
    public void EnableAndDisableAreIdempotentForOwnedEntry()
    {
        var store = new FakeStore(); var manager = new AutoStartManager(store, Exe);
        manager.SetEnabled(true); manager.SetEnabled(true);
        Assert.AreEqual(1, store.Writes);
        manager.SetEnabled(false); manager.SetEnabled(false);
        Assert.AreEqual(1, store.Deletes); Assert.AreEqual(AutoStartState.Disabled, manager.GetState());
    }

    [TestMethod]
    [DataRow(true, "different command")]
    [DataRow(false, null)]
    public void ConflictingEntryIsReportedAndNeverChanged(bool stringValue, string? command)
    {
        var store = new FakeStore { Value = new(true, command, stringValue) };
        var manager = new AutoStartManager(store, Exe);
        Assert.AreEqual(AutoStartState.Conflict, manager.GetState());
        foreach (bool enabled in new[] { true, false })
            Assert.AreEqual("autostart-entry-conflict", Assert.Throws<AutoStartException>(() => manager.SetEnabled(enabled)).Code);
        Assert.AreEqual(0, store.Writes); Assert.AreEqual(0, store.Deletes); Assert.IsTrue(store.Value.Exists);
    }

    [TestMethod]
    public void StartupStoreReadFailureIsMappedWithoutWriting()
    {
        var store = new FakeStore { ReadError = new IOException("synthetic") };
        var manager = new AutoStartManager(store, Exe);
        Assert.AreEqual("autostart-read-failed", Assert.Throws<AutoStartException>(() => manager.GetState()).Code);
        Assert.AreEqual(0, store.Writes); Assert.AreEqual(0, store.Deletes);
    }

    [TestMethod]
    public void StartupStoreWriteFailureIsMappedAndReadbackMismatchFailsClosed()
    {
        var failing = new FakeStore { WriteError = new UnauthorizedAccessException("synthetic") };
        var first = new AutoStartManager(failing, Exe);
        Assert.AreEqual("autostart-write-failed", Assert.Throws<AutoStartException>(() => first.SetEnabled(true)).Code);

        var discarded = new FakeStore { DiscardWrites = true };
        var second = new AutoStartManager(discarded, Exe);
        Assert.AreEqual("autostart-write-failed", Assert.Throws<AutoStartException>(() => second.SetEnabled(true)).Code);
        Assert.AreEqual(AutoStartState.Disabled, second.GetState());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("relative.exe")]
    [DataRow(@"C:\Program Files\PhoneBridge NG\PhoneBridge.txt")]
    public void InvalidExecutablePathIsRejected(string path) =>
        Assert.AreEqual("autostart-invalid-path", Assert.Throws<AutoStartException>(() => new AutoStartManager(new FakeStore(), path)).Code);

    [TestMethod]
    public void CommandLongerThanShortcutLimitIsRejected()
    {
        string path = @"C:\" + new string('a', 32760) + ".exe";
        Assert.AreEqual("autostart-command-too-long", Assert.Throws<AutoStartException>(() => new AutoStartManager(new FakeStore(), path)).Code);
    }

    [TestMethod]
    [DataRow(true, new[] { "--startup" })]
    [DataRow(false, new string[0])]
    [DataRow(false, new[] { "--STARTUP" })]
    [DataRow(false, new[] { "--startup", "extra" })]
    public void StartupLaunchRequiresTheSingleExactArgument(bool expected, string[] arguments) =>
        Assert.AreEqual(expected, AutoStartManager.IsStartupLaunch(arguments));

    [TestMethod]
    public void WindowsStartupFolderStoreCreatesReadsAndDeletesExactShortcut()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhoneBridge-AutoStart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WindowsAutoStartStore(root);
            var manager = new AutoStartManager(store, Exe);
            manager.SetEnabled(true);
            Assert.IsTrue(File.Exists(Path.Combine(root, WindowsAutoStartStore.ShortcutName)));
            Assert.AreEqual(AutoStartState.Enabled, new AutoStartManager(new WindowsAutoStartStore(root), Exe).GetState());
            manager.SetEnabled(false);
            Assert.IsFalse(File.Exists(Path.Combine(root, WindowsAutoStartStore.ShortcutName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeStore : IAutoStartStore
    {
        internal AutoStartValue Value { get; set; } = AutoStartValue.Missing;
        internal Exception? ReadError { get; init; }
        internal Exception? WriteError { get; init; }
        internal bool DiscardWrites { get; init; }
        internal int Writes { get; private set; }
        internal int Deletes { get; private set; }
        public AutoStartValue Read() { if (ReadError is not null) throw ReadError; return Value; }
        public void Write(string executablePath, string arguments)
        {
            Writes++; if (WriteError is not null) throw WriteError;
            if (!DiscardWrites) Value = new(true, WindowsAutoStartStore.BuildCommand(executablePath, arguments), true);
        }
        public void Delete() { Deletes++; Value = AutoStartValue.Missing; }
    }
}
