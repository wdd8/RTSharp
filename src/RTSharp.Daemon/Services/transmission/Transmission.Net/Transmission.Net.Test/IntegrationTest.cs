using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Transmission.Net.Api;
using Transmission.Net.Api.Entity;
using Transmission.Net.Arguments;

namespace Transmission.Net.Test;

[TestClass]
public class IntegrationTest
{
    private readonly string FILE_PATH = AppDomain.CurrentDomain.BaseDirectory
                                        + "./Data/ubuntu-21.10-desktop-amd64.iso.torrent";
    private const string HOST = "http://localhost:9091/transmission/rpc";
    private const string SESSION_ID = "";
    private readonly TransmissionClient client = new(HOST, SESSION_ID);

    public async Task<NewTorrentInfo> TorrentAddAsync()
    {
        if (!File.Exists(FILE_PATH))
        {
            throw new System.Exception("Torrent file not found");
        }

        var fstream = File.OpenRead(FILE_PATH);
        byte[] filebytes = new byte[fstream.Length];
        fstream.Read(filebytes, 0, Convert.ToInt32(fstream.Length));

        string encodedData = Convert.ToBase64String(filebytes);

        var torrent = new NewTorrent
        {
            //Filename = filename,
            Metainfo = encodedData,
            Paused = true
        };

        return await client.TorrentAddAsync(torrent);
    }

    #region Torrent Test

    [TestMethod]
    public async Task AddTorrent_Test()
    {

        var newTorrentInfo = await TorrentAddAsync();

        Assert.IsNotNull(newTorrentInfo);
        Assert.IsTrue(newTorrentInfo.Id != 0);
    }

    [TestMethod]
    public async Task AddTorrent_Magnet_TestAsync()
    {
        var torrent = new NewTorrent
        {
            Filename = "magnet:?xt=urn:btih:5bcb7e72aec774997622af7de5471d71e17f1db8&dn=ubuntu-12.04.5-desktop-amd64.iso&tr=http%3A%2F%2Ftorrent.ubuntu.com%3A6969%2Fannounce&tr=http%3A%2F%2Fipv6.torrent.ubuntu.com%3A6969%2Fannounce",
            Paused = false
        };

        var newTorrentInfo = await client.TorrentAddAsync(torrent);

        Assert.IsNotNull(newTorrentInfo);
        Assert.IsTrue(newTorrentInfo.Id != 0);
    }

    [TestMethod]
    public async Task GetTorrentInfo_TestAsync()
    {
        var torrentsInfo = await client.TorrentGetAsync();

        Assert.IsNotNull(torrentsInfo);
        Assert.IsNotNull(torrentsInfo.Torrents);
        Assert.IsTrue(torrentsInfo.Torrents.Any());
    }

    [TestMethod]
    public async Task SetTorrentSettings_TestAsync()
    {
        var torrentsInfo = await client.TorrentGetAsync();
        var torrentInfo = torrentsInfo.Torrents.FirstOrDefault(t => t.Trackers.Any());
        Assert.IsNotNull(torrentInfo, "Torrent not found");

        var trackerInfo = torrentInfo.Trackers.FirstOrDefault();
        Assert.IsNotNull(trackerInfo, "Tracker not found");
        var trackerCount = torrentInfo.Trackers.Length;
        TorrentSettings settings = new()
        {
            IDs = new object[] { torrentInfo.HashString },
            TrackerRemove = new int[] { trackerInfo.Id }
        };

        await client.TorrentSetAsync(settings);

        torrentsInfo = await client.TorrentGetAsync((int)torrentInfo.Id);
        torrentInfo = torrentsInfo.Torrents.FirstOrDefault();

        Assert.IsFalse(trackerCount == torrentInfo.Trackers.Length);
    }

    [TestMethod]
    public async Task RenamePathTorrent_Test()
    {
        var newTorrentInfo = await TorrentAddAsync();
        var torrentsInfo = await client.TorrentGetAsync();
        var torrentInfo = torrentsInfo.Torrents.FirstOrDefault(t => t.Id == newTorrentInfo.Id);
        Assert.IsNotNull(torrentInfo, "Torrent not found");

        var result = await client.TorrentRenamePathAsync((int)torrentInfo.Id, torrentInfo.Files[0].Name, "test_" + torrentInfo.Files[0].Name);

        Assert.IsNotNull(result, "Torrent not found");
        Assert.IsTrue(result.Id != 0);
    }

    [TestMethod]
    public async Task RemoveTorrent_Test()
    {
        var torrentsInfo = await client.TorrentGetAsync();
        var torrentInfo = await TorrentAddAsync();
        Assert.IsNotNull(torrentInfo, "Torrent not found");

        await client.TorrentRemoveAsync(torrentInfo.Id);

        torrentsInfo = await client.TorrentGetAsync();

        Assert.IsFalse(torrentsInfo.Torrents.Any(t => t.Id == torrentInfo.Id));
    }

    #endregion

    #region Session Test

    [TestMethod]
    public async Task SessionGetTestAsync()
    {
        var info = await client.GetSessionInformationAsync();
        Assert.IsNotNull(info);
        Assert.IsNotNull(info.Version);
    }

    [TestMethod]
    public async Task ChangeSessionTestAsync()
    {
        //Get current session information
        var sessionInformation = await client.GetSessionInformationAsync();

        //Save old speed limit up
        var oldSpeedLimit = sessionInformation.SpeedLimitUp;

        //Set new session settings
        await client.SetSessionSettingsAsync(new SessionSettings() { SpeedLimitUp = 100 });

        //Get new session information
        var newSessionInformation = await client.GetSessionInformationAsync();

        //Check new speed limit
        Assert.AreEqual(newSessionInformation.SpeedLimitUp, 100);

        //Restore speed limit
        newSessionInformation.SpeedLimitUp = oldSpeedLimit;

        //Set new session settinhs
        await client.SetSessionSettingsAsync(new SessionSettings() { SpeedLimitUp = oldSpeedLimit });
    }

    #endregion
}
