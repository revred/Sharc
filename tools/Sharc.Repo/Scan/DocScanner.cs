// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Sharc.Repo.Scan;

/// <summary>
/// Scans markdown documents for source file references and feature keyword matches.
/// </summary>
public static partial class DocScanner
{
    /// <summary>Result of scanning a single document.</summary>
    public readonly record struct DocScanResult(
        List<string> FileReferences,
        List<string> MatchedFeatures);

    /// <summary>
    /// Scans a document for file references and feature keyword matches.
    /// </summary>
    public static DocScanResult ScanDocument(string content)
    {
        var fileRefs = ExtractFileReferences(content);
        var features = MatchFeatureKeywords(content);
        return new DocScanResult(fileRefs, features);
    }

    /// <summary>
    /// Extracts file path references from markdown content.
    /// Matches patterns like <c>src/Sharc.Core/BTree/BTreeReader.cs</c>.
    /// </summary>
    public static List<string> ExtractFileReferences(string content)
    {
        var matches = FilePathRegex().Matches(content);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (Match m in matches)
        {
            var path = m.Value;
            if (seen.Add(path))
                result.Add(path);
        }

        return result;
    }

    /// <summary>
    /// Matches document text against feature keywords from <see cref="FeatureCatalog"/>.
    /// Returns feature names that have at least one keyword match.
    /// </summary>
    public static List<string> MatchFeatureKeywords(string content)
    {
        var matches = new List<string>();

        foreach (var feature in FeatureCatalog.All)
        {
            if (feature.Keywords.Length == 0) continue;

            foreach (var keyword in feature.Keywords)
            {
                if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(feature.Name);
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Regex matching file paths starting with src/, tests/, bench/, tools/, PRC/, or docs/
    /// followed by non-whitespace characters ending in a file extension.
    /// </summary>
    [GeneratedRegex(@"(?:src|tests|bench|tools|PRC|docs)/[^\s)`""']+\.\w+", RegexOptions.Compiled)]
    private static partial Regex FilePathRegex();
}
