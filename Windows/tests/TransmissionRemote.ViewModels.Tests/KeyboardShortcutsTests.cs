namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class KeyboardShortcutsTests
{
    private static IEnumerable<KeyChord> AllChords => KeyboardShortcuts.All.SelectMany(shortcuts => shortcuts.All);

    [TestMethod]
    public void EveryCommandHasOneEntry()
    {
        var commands = KeyboardShortcuts.All.Select(shortcuts => shortcuts.Command).ToList();

        CollectionAssert.AreEquivalent(Enum.GetValues<AppCommand>(), commands);
    }

    [TestMethod]
    public void NoKeyDoesTwoThings()
    {
        var duplicates = AllChords.GroupBy(chord => chord).Where(group => group.Count() > 1).Select(group => group.Key);

        Assert.IsEmpty(duplicates);
    }

    [TestMethod]
    public void NoShortcutUsesCtrlAltWhichIsAltGrOnManyLayouts()
    {
        const ShortcutModifiers CtrlAlt = ShortcutModifiers.Control | ShortcutModifiers.Alt;

        Assert.IsEmpty(AllChords.Where(chord => (chord.Modifiers & CtrlAlt) == CtrlAlt));
    }

    [TestMethod]
    public void NoShortcutTakesF10WhichOpensTheMenuBar()
    {
        Assert.IsFalse(Enum.IsDefined((ShortcutKey)0x79), "F10 isn't even a key the table can use");
    }

    [TestMethod]
    public void KeysTextFieldsUseOnlyWorkInTheList()
    {
        Assert.IsTrue(KeyboardShortcuts.For(AppCommand.Remove).Shown!.IsListOnly);
        Assert.IsTrue(KeyboardShortcuts.For(AppCommand.Paste).All.All(chord => chord.IsListOnly));
        Assert.IsTrue(KeyboardShortcuts.For(AppCommand.SelectAllTorrents).Shown!.IsListOnly);
        Assert.IsTrue(KeyboardShortcuts.For(AppCommand.Rename).Shown!.IsListOnly);

        var addLink = KeyboardShortcuts.For(AppCommand.AddTorrentLink);
        Assert.IsFalse(addLink.Shown!.IsListOnly, "Ctrl+U works everywhere");
        Assert.IsTrue(addLink.AlsoAccepted.Single().IsListOnly, "Ctrl+Insert copies in a text field");
    }

    [TestMethod]
    public void ExitIsAcceptedButNotShown()
    {
        var exit = KeyboardShortcuts.For(AppCommand.Exit);

        Assert.IsNull(exit.Shown);
        Assert.AreEqual(new KeyChord(ShortcutKey.X, ShortcutModifiers.Alt), exit.All.Single());
    }

    [TestMethod]
    public void TheShownKeyComesFirst()
    {
        var moveData = KeyboardShortcuts.For(AppCommand.MoveData).All.ToList();

        CollectionAssert.AreEqual(
            new[] { new KeyChord(ShortcutKey.F6), new KeyChord(ShortcutKey.M, ShortcutModifiers.Control | ShortcutModifiers.Shift) },
            moveData);
    }
}
