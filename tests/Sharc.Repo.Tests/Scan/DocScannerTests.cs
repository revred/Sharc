// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Scan;
using Xunit;

namespace Sharc.Repo.Tests.Scan;

public sealed class DocScannerTests
{
    [Fact]
    public void ExtractFileReferences_MarkdownWithPaths_ReturnsAll()
    {
        var markdown = """
            The cipher lives at `src/Sharc.Crypto/AesGcmCipher.cs` and the
            reader at src/Sharc.Core/BTree/BTreeReader.cs.
            Also see tests/Sharc.Tests/Crypto/CipherTests.cs for tests.
            """;

        var refs = DocScanner.ExtractFileReferences(markdown);
        Assert.Contains("src/Sharc.Crypto/AesGcmCipher.cs", refs);
        Assert.Contains("src/Sharc.Core/BTree/BTreeReader.cs", refs);
        Assert.Contains("tests/Sharc.Tests/Crypto/CipherTests.cs", refs);
    }

    [Fact]
    public void ExtractFileReferences_NoReferences_ReturnsEmpty()
    {
        var refs = DocScanner.ExtractFileReferences("No file references here.");
        Assert.Empty(refs);
    }

    [Fact]
    public void ExtractFileReferences_DuplicatePaths_Deduplicated()
    {
        var markdown = """
            See src/Sharc.Core/BTree/BTreeReader.cs and also
            src/Sharc.Core/BTree/BTreeReader.cs again.
            """;

        var refs = DocScanner.ExtractFileReferences(markdown);
        Assert.Single(refs.Where(r => r == "src/Sharc.Core/BTree/BTreeReader.cs"));
    }

    [Fact]
    public void MatchFeatureKeywords_TextWithKeywords_ReturnsMatchingFeatures()
    {
        var text = "This document covers AES-256-GCM encryption and Argon2 KDF.";
        var features = DocScanner.MatchFeatureKeywords(text);
        Assert.Contains("encryption", features);
    }

    [Fact]
    public void MatchFeatureKeywords_GraphKeywords_MatchesGraphEngine()
    {
        var text = "The BFS traversal algorithm in the graph store.";
        var features = DocScanner.MatchFeatureKeywords(text);
        Assert.Contains("graph-engine", features);
    }

    [Fact]
    public void MatchFeatureKeywords_NoKeywords_ReturnsEmpty()
    {
        var features = DocScanner.MatchFeatureKeywords("Just some plain text with no keywords.");
        Assert.Empty(features);
    }

    [Fact]
    public void ScanDocument_MixedContent_ReturnsFileRefsAndFeatures()
    {
        var content = """
            # Encryption Specification
            The AES-256-GCM cipher at src/Sharc.Crypto/AesGcmCipher.cs
            provides page-level encryption with Argon2id KDF.
            """;

        var result = DocScanner.ScanDocument(content);
        Assert.Contains("src/Sharc.Crypto/AesGcmCipher.cs", result.FileReferences);
        Assert.Contains("encryption", result.MatchedFeatures);
    }
}
