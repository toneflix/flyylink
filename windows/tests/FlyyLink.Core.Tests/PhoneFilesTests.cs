using System.Text.Json.Nodes;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors PhoneFilesTests.swift and PhoneFilesTest.kt.
public class PhoneFilesTests
{
    private static NetworkPacket Wire(NetworkPacket packet) => NetworkPacket.Parse(packet.Serialize());

    [Fact]
    public void CleansPaths()
    {
        Assert.Equal("/", PhoneFiles.Clean(""));
        Assert.Equal("/", PhoneFiles.Clean("/"));
        Assert.Equal("/DCIM/Camera", PhoneFiles.Clean("DCIM//./Camera/"));
        Assert.Null(PhoneFiles.Clean("/DCIM/../Android"));
        Assert.Null(PhoneFiles.Clean("/a\0b"));
    }

    [Fact]
    public void SplitsPaths()
    {
        Assert.Equal("/Music", PhoneFiles.Join("/", "Music"));
        Assert.Equal("/Music/a.mp3", PhoneFiles.Join("/Music", "a.mp3"));
        Assert.Equal("a.mp3", PhoneFiles.NameOf("/Music/a.mp3"));
        Assert.Equal("/Music", PhoneFiles.ParentOf("/Music/a.mp3"));
        Assert.Equal("/", PhoneFiles.ParentOf("/Music"));
        Assert.True(PhoneFiles.IsValidName("Notes.txt"));
        Assert.False(PhoneFiles.IsValidName(".."));
        Assert.False(PhoneFiles.IsValidName("a/b"));
    }

    [Fact]
    public void RequestsRoundTrip()
    {
        var list = new PhoneFiles.Request(7, PhoneFiles.Action.List, "/DCIM");
        var move = new PhoneFiles.Request(8, PhoneFiles.Action.Move, "/a.txt", "/Documents/a.txt");

        Assert.Equal(list, PhoneFiles.RequestOf(Wire(PhoneFiles.RequestPacket(list))));
        Assert.Equal(move, PhoneFiles.RequestOf(Wire(PhoneFiles.RequestPacket(move))));
    }

    [Fact]
    public void RefusesBadRequests()
    {
        Assert.Null(PhoneFiles.RequestOf(Wire(PhoneFiles.RequestPacket(new(1, PhoneFiles.Action.List, "/../etc")))));
        Assert.Null(PhoneFiles.RequestOf(Wire(PhoneFiles.RequestPacket(new(1, PhoneFiles.Action.Move, "/a")))));
        Assert.Null(PhoneFiles.RequestOf(Wire(PhoneFiles.RequestPacket(new(1, PhoneFiles.Action.Move, "/a", "/")))));
        Assert.Null(PhoneFiles.RequestOf(new NetworkPacket(PacketType.FilesRequest, new JsonObject { ["id"] = 1, ["action"] = "format", ["path"] = "/" })));
    }

    [Fact]
    public void AnswersRoundTrip()
    {
        PhoneFile[] files =
        [
            new("/DCIM", true, Modified: 1_700_000_000_000),
            new("/notes.txt", false, 12, 1_700_000_100_000),
        ];

        var listing = PhoneFiles.AnswerOf(Wire(PhoneFiles.AnswerPacket(new(7, Files: files))))!;
        var sent = PhoneFiles.AnswerOf(Wire(PhoneFiles.AnswerPacket(new(8, File: files[1]))))!;
        var failed = PhoneFiles.AnswerOf(Wire(PhoneFiles.AnswerPacket(new(9, PhoneFiles.Failure.Denied))))!;

        Assert.True(listing.Ok);
        Assert.Equal(files, listing.Items);
        Assert.Equal(files[1], sent.File);
        Assert.Empty(sent.Items);
        Assert.False(failed.Ok);
        Assert.Equal(PhoneFiles.Failure.Denied, failed.Failure);
        Assert.Equal("notes.txt", files[1].Name);
    }

    [Fact]
    public void ReadsUnknownFailureAsFailed()
    {
        var packet = NetworkPacket.Parse("""{"id":1,"type":"flyylink.files","body":{"id":3,"ok":false,"error":"disk on fire"}}""");

        Assert.Equal(PhoneFiles.Failure.Failed, PhoneFiles.AnswerOf(packet)?.Failure);
    }

    [Fact]
    public void PhonesAdvertiseFilesButTvsDont()
    {
        Assert.Contains(PacketType.FilesRequest, Capabilities.Mvp);
        Assert.DoesNotContain(PacketType.FilesRequest, Capabilities.Tv);
    }
}
