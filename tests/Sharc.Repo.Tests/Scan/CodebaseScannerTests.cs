// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Repo.Scan;
using Xunit;

namespace Sharc.Repo.Tests.Scan;

public sealed class CodebaseScannerTests : IDisposable
{
    private readonly string _tempDir;

    public CodebaseScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharc_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        SeedFiles();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private void SeedFiles()
    {
        // Create a minimal source tree
        CreateFile("src/Sharc.Crypto/AesGcmCipher.cs", """
            using Sharc.Core;
            namespace Sharc.Crypto;
            /// <summary>AES-256-GCM page cipher</summary>
            public sealed class AesGcmCipher { }
            """);

        CreateFile("src/Sharc.Core/BTree/BTreeReader.cs", """
            namespace Sharc.Core.BTree;
            public sealed class BTreeReader { }
            """);

        CreateFile("src/Sharc.Core/IO/FilePageSource.cs", """
            namespace Sharc.Core.IO;
            public sealed class FilePageSource { }
            """);

        CreateFile("tests/Sharc.Tests/Crypto/AesGcmCipherTests.cs", """
            using Sharc.Crypto;
            namespace Sharc.Tests.Crypto;
            public sealed class AesGcmCipherTests { }
            """);

        CreateFile("src/Sharc/SharcDatabase.cs", """
            using Sharc.Core;
            using Sharc.Crypto;
            namespace Sharc;
            public sealed class SharcDatabase { }
            """);

        // Doc files
        CreateFile("PRC/EncryptionSpec.md", """
            # Encryption Specification
            AES-256-GCM cipher at src/Sharc.Crypto/AesGcmCipher.cs
            """);

        CreateFile("docs/README.md", """
            # Sharc Documentation
            No specific feature keywords here.
            """);
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Scan_ProducesFeatures_FromCatalog()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        Assert.True(result.Features.Count >= 20);
        Assert.Contains(result.Features, f => f.Name == "encryption");
        Assert.Contains(result.Features, f => f.Name == "btree-read");
    }

    [Fact]
    public void Scan_ProducesFilePurposes_ForAllCsFiles()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        Assert.True(result.FilePurposes.Count >= 5);
        Assert.Contains(result.FilePurposes, fp => fp.Path.Contains("AesGcmCipher.cs"));
        Assert.Contains(result.FilePurposes, fp => fp.Path.Contains("BTreeReader.cs"));
    }

    [Fact]
    public void Scan_ProducesFeatureEdges_SourceFiles()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        var encryptionEdges = result.FeatureEdges.Where(e => e.FeatureName == "encryption").ToList();
        Assert.True(encryptionEdges.Count > 0);
        Assert.Contains(encryptionEdges, e => e.TargetKind == "source");
    }

    [Fact]
    public void Scan_ProducesFeatureEdges_TestFiles()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        var testEdges = result.FeatureEdges.Where(e => e.TargetKind == "test").ToList();
        Assert.True(testEdges.Count > 0);
    }

    [Fact]
    public void Scan_ProducesFeatureEdges_DocFiles()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        var docEdges = result.FeatureEdges.Where(e => e.TargetKind == "doc").ToList();
        Assert.True(docEdges.Count > 0);
        Assert.Contains(docEdges, e => e.TargetPath.Contains("EncryptionSpec.md"));
    }

    [Fact]
    public void Scan_ProducesFileDeps_UsingDirectives()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        // SharcDatabase.cs uses Sharc.Core and Sharc.Crypto
        var sharcDbDeps = result.FileDeps.Where(d => d.SourcePath.Contains("SharcDatabase.cs")).ToList();
        Assert.True(sharcDbDeps.Count >= 1);
    }

    [Fact]
    public void ExtractUsingDirectives_SharcNamespaces_Returned()
    {
        var content = """
            using System;
            using Sharc.Core;
            using Sharc.Crypto;
            using Xunit;
            """;

        var usings = CodebaseScanner.ExtractUsingDirectives(content);
        Assert.Contains("Sharc.Core", usings);
        Assert.Contains("Sharc.Crypto", usings);
        Assert.DoesNotContain("System", usings);
        Assert.DoesNotContain("Xunit", usings);
    }

    [Fact]
    public void ExtractUsingDirectives_GlobalUsing_Handled()
    {
        var content = "using global::Sharc.Core.Query;";
        var usings = CodebaseScanner.ExtractUsingDirectives(content);
        Assert.Contains("Sharc.Core.Query", usings);
    }

    [Fact]
    public void Scan_FilePurpose_HasProject()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        var cipher = result.FilePurposes.First(fp => fp.Path.Contains("AesGcmCipher.cs"));
        Assert.Equal("Sharc.Crypto", cipher.Project);
    }

    [Fact]
    public void Scan_FilePurpose_HasLayer()
    {
        var scanner = new CodebaseScanner(_tempDir);
        var result = scanner.Scan();

        var cipher = result.FilePurposes.First(fp => fp.Path.Contains("AesGcmCipher.cs"));
        Assert.Equal("crypto", cipher.Layer);
    }
}
