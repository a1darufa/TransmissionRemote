namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class LogPathTests
{
    private const string Profile = @"C:\Users\Alice";

    [TestMethod]
    public void HidesTheUserFolder()
    {
        Assert.AreEqual(@"%USERPROFILE%\AppData\Local\TransmissionRemote\Logs",
            LogPath.Display(@"C:\Users\Alice\AppData\Local\TransmissionRemote\Logs", Profile));
        Assert.AreEqual("%USERPROFILE%", LogPath.Display(Profile, Profile));
    }

    [TestMethod]
    public void IgnoresCase()
    {
        Assert.AreEqual(@"%USERPROFILE%\Downloads", LogPath.Display(@"c:\users\alice\Downloads", Profile));
    }

    [TestMethod]
    public void LeavesOtherFoldersAlone()
    {
        Assert.AreEqual(@"D:\Torrents", LogPath.Display(@"D:\Torrents", Profile));
        Assert.AreEqual(@"\\nas\share", LogPath.Display(@"\\nas\share", Profile));
        // A sibling folder that only starts with the same letters isn't the user's folder.
        Assert.AreEqual(@"C:\Users\Alice2\Documents", LogPath.Display(@"C:\Users\Alice2\Documents", Profile));
    }

    [TestMethod]
    public void WithoutAUserFolderNothingChanges()
    {
        Assert.AreEqual(@"C:\Data", LogPath.Display(@"C:\Data", string.Empty));
    }
}
