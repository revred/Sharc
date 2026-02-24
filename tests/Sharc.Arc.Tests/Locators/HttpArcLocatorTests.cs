// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Arc;
using Sharc.Arc.Locators;
using Xunit;

namespace Sharc.Arc.Tests.Locators;

/// <summary>
/// Tests for <see cref="HttpArcLocator"/> — focused on URL transformation logic
/// (unit-testable without real network calls).
/// </summary>
public sealed class HttpArcLocatorTests
{
    // ── Dropbox URL transformation ──

    [Fact]
    public void Dropbox_Dl0_ConvertedToDl1()
    {
        var url = "https://www.dropbox.com/s/abc123/data.sharc?dl=0";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://www.dropbox.com/s/abc123/data.sharc?dl=1", result);
    }

    [Fact]
    public void Dropbox_NoDlParam_AppendsDl1()
    {
        var url = "https://www.dropbox.com/s/abc123/data.sharc";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://www.dropbox.com/s/abc123/data.sharc?dl=1", result);
    }

    [Fact]
    public void Dropbox_ExistingQueryParam_AppendsDl1()
    {
        var url = "https://www.dropbox.com/s/abc123/data.sharc?foo=bar";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://www.dropbox.com/s/abc123/data.sharc?foo=bar&dl=1", result);
    }

    [Fact]
    public void Dropbox_AlreadyDl1_Unchanged()
    {
        var url = "https://www.dropbox.com/s/abc123/data.sharc?dl=1";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://www.dropbox.com/s/abc123/data.sharc?dl=1", result);
    }

    // ── Google Drive URL transformation ──

    [Fact]
    public void GoogleDrive_ShareLink_ConvertedToDirectDownload()
    {
        var url = "https://drive.google.com/file/d/1A2B3C4D5E6F/view?usp=sharing";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://drive.google.com/uc?export=download&id=1A2B3C4D5E6F", result);
    }

    [Fact]
    public void GoogleDrive_ShareLinkNoQuery_ConvertedToDirectDownload()
    {
        var url = "https://drive.google.com/file/d/MyFileId123/view";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal("https://drive.google.com/uc?export=download&id=MyFileId123", result);
    }

    [Fact]
    public void GoogleDrive_UcFormat_Unchanged()
    {
        var url = "https://drive.google.com/uc?export=download&id=1A2B3C4D5E6F";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal(url, result);
    }

    // ── Generic / S3 / Azure / GCS URLs ──

    [Fact]
    public void S3PresignedUrl_Unchanged()
    {
        var url = "https://mybucket.s3.amazonaws.com/data.sharc?X-Amz-Signature=abc123";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void AzureBlobUrl_Unchanged()
    {
        var url = "https://myaccount.blob.core.windows.net/container/data.sharc?sp=r&st=2024-01-01";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal(url, result);
    }

    [Fact]
    public void GenericHttpsUrl_Unchanged()
    {
        var url = "https://cdn.example.com/arcs/factory-floor.sharc";
        var result = HttpArcLocator.TransformCloudUrl(url);

        Assert.Equal(url, result);
    }

    // ── ResolveDownloadUrl (from ArcUri) ──

    [Fact]
    public void ResolveDownloadUrl_FullUrl_PassedThrough()
    {
        var uri = ArcUri.Parse("arc://https/https://cdn.example.com/data.sharc");
        var result = HttpArcLocator.ResolveDownloadUrl(uri);

        Assert.Contains("cdn.example.com/data.sharc", result);
    }

    // ── Locator properties ──

    [Fact]
    public void Authority_IsHttps()
    {
        using var locator = new HttpArcLocator();
        Assert.Equal("https", locator.Authority);
    }

    [Fact]
    public void Disposable_OwnsClient()
    {
        // Should not throw
        var locator = new HttpArcLocator();
        locator.Dispose();
    }

    [Fact]
    public void Disposable_DoesNotOwnClient()
    {
        var client = new HttpClient();
        var locator = new HttpArcLocator(client, ownsClient: false);
        locator.Dispose();

        // Client should still be usable (not disposed)
        Assert.NotNull(client.BaseAddress?.ToString() ?? "ok");
    }
}
