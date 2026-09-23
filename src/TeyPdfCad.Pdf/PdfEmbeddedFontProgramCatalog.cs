using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace TeyPdfCad.Pdf;

internal sealed class PdfEmbeddedFontProgramCatalog
{
    private const int MaximumReferenceDepth = 16;
    private const int MaximumParentDepth = 32;
    private readonly IReadOnlyDictionary<string, PdfFontProgramIdentity?> _identityByExactFontName;

    private PdfEmbeddedFontProgramCatalog(
        IReadOnlyDictionary<string, PdfFontProgramIdentity?> identityByExactFontName)
    {
        _identityByExactFontName = identityByExactFontName;
    }

    public PdfFontProgramIdentity? ResolveUnique(string? exactFontName)
        => !string.IsNullOrWhiteSpace(exactFontName)
            && _identityByExactFontName.TryGetValue(exactFontName, out var identity)
                ? identity
                : null;

    public string? ResolveUniqueSha256(string? exactFontName)
        => ResolveUnique(exactFontName)?.Sha256;

    public static PdfEmbeddedFontProgramCatalog Create(
        PdfDocument document,
        Page page)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);

        try
        {
            var resources = FindInheritedResources(
                page.Dictionary,
                token => ResolveDocumentToken(document, token));
            return resources is null
                ? new PdfEmbeddedFontProgramCatalog(
                    new Dictionary<string, PdfFontProgramIdentity?>(StringComparer.Ordinal))
                : CreateFromResources(
                    resources,
                    token => ResolveDocumentToken(document, token));
        }
        catch
        {
            // Font-program identity is optional evidence. A malformed,
            // unsupported or encrypted font resource must never turn into a
            // guessed identity or abort the whole PDF conversion.
            return new PdfEmbeddedFontProgramCatalog(
                new Dictionary<string, PdfFontProgramIdentity?>(StringComparer.Ordinal));
        }
    }

    internal static PdfEmbeddedFontProgramCatalog CreateFromResources(
        DictionaryToken resources,
        Func<IToken, IToken?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(resolver);

        var candidates = new List<FontProgramCandidate>();
        var fontToken = Get(resources, "Font");
        var fontDictionary = ResolveDictionary(fontToken, resolver);
        if (fontDictionary is null)
        {
            return new PdfEmbeddedFontProgramCatalog(
                new Dictionary<string, PdfFontProgramIdentity?>(StringComparer.Ordinal));
        }

        foreach (var resource in fontDictionary.Data
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var dictionary = ResolveDictionary(resource.Value, resolver);
            if (dictionary is null)
                continue;

            candidates.Add(ReadCandidate(dictionary, resolver));
        }

        var names = candidates
            .SelectMany(candidate => candidate.ExactNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var resolved = new Dictionary<string, PdfFontProgramIdentity?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var matches = candidates
                .Where(candidate => candidate.ExactNames.Contains(
                    name,
                    StringComparer.Ordinal))
                .ToArray();

            // Exact identity is accepted only when every resource with that
            // exact name is embedded/resolvable AND the font program plus
            // rendering-relevant font dictionary identity is the same.
            // Same binary with a different Encoding is still ambiguous.
            if (matches.Length == 0
                || matches.Any(candidate => candidate.Identity is null))
            {
                resolved[name] = null;
                continue;
            }

            var identities = matches
                .Select(candidate => candidate.Identity!)
                .Distinct()
                .ToArray();
            resolved[name] = identities.Length == 1
                ? identities[0]
                : null;
        }

        return new PdfEmbeddedFontProgramCatalog(resolved);
    }

    private static FontProgramCandidate ReadCandidate(
        DictionaryToken fontDictionary,
        Func<IToken, IToken?> resolver)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        AddName(fontDictionary, "BaseFont", names);

        var descriptorOwner = fontDictionary;
        var subtype = ResolveName(Get(fontDictionary, "Subtype"), resolver)
            ?? "<unknown>";
        var descendantSubtype = string.Empty;
        var encodingName = DescribeEncoding(
            Get(fontDictionary, "Encoding"),
            resolver);
        var hasToUnicode = Get(fontDictionary, "ToUnicode") is not null;
        if (string.Equals(subtype, "Type0", StringComparison.Ordinal))
        {
            var descendants = ResolveArray(
                Get(fontDictionary, "DescendantFonts"),
                resolver);
            if (descendants is not { Data.Count: 1 })
                return new FontProgramCandidate(names.ToArray(), null);

            var descendant = ResolveDictionary(descendants.Data[0], resolver);
            if (descendant is null)
                return new FontProgramCandidate(names.ToArray(), null);

            descriptorOwner = descendant;
            AddName(descendant, "BaseFont", names);
            descendantSubtype = ResolveName(
                Get(descendant, "Subtype"),
                resolver) ?? "<unknown>";
        }

        var descriptor = ResolveDictionary(
            Get(descriptorOwner, "FontDescriptor"),
            resolver);
        if (descriptor is null
            && !ReferenceEquals(descriptorOwner, fontDictionary))
        {
            descriptor = ResolveDictionary(
                Get(fontDictionary, "FontDescriptor"),
                resolver);
        }

        if (descriptor is null)
            return new FontProgramCandidate(names.ToArray(), null);

        AddName(descriptor, "FontName", names);

        var stream = ResolveFontProgramStream(descriptor, resolver);
        if (stream is null)
            return new FontProgramCandidate(names.ToArray(), null);

        try
        {
            var decoded = stream.Decode(DefaultFilterProvider.Instance);
            if (decoded.Length == 0)
                return new FontProgramCandidate(names.ToArray(), null);

            var hash = SHA256.HashData(decoded.Span);
            var programSubtype = string.Equals(
                subtype,
                "Type0",
                StringComparison.Ordinal)
                    ? $"Type0/{descendantSubtype}"
                    : subtype;
            var identity = new PdfFontProgramIdentity(
                Convert.ToHexString(hash),
                programSubtype,
                encodingName,
                hasToUnicode,
                names.Any(IsSubsetFontName));
            return new FontProgramCandidate(
                names.ToArray(),
                identity);
        }
        catch
        {
            return new FontProgramCandidate(names.ToArray(), null);
        }
    }

    private static string DescribeEncoding(
        IToken? token,
        Func<IToken, IToken?> resolver)
    {
        var resolved = Resolve(token, resolver);
        return resolved switch
        {
            null => "<absent>",
            NameToken name => name.Data,
            DictionaryToken => "<custom-dictionary>",
            StreamToken => "<custom-stream>",
            _ => "<unsupported>"
        };
    }

    private static bool IsSubsetFontName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length < 8
            || name[6] != '+')
        {
            return false;
        }

        for (var index = 0; index < 6; index++)
        {
            if (name[index] < 'A' || name[index] > 'Z')
                return false;
        }

        return true;
    }

    private static StreamToken? ResolveFontProgramStream(
        DictionaryToken descriptor,
        Func<IToken, IToken?> resolver)
    {
        var streams = new List<StreamToken>();
        foreach (var key in new[] { "FontFile", "FontFile2", "FontFile3" })
        {
            var token = Get(descriptor, key);
            if (token is null)
                continue;

            var resolved = Resolve(token, resolver);
            if (resolved is not StreamToken stream)
                return null;
            streams.Add(stream);
        }

        return streams.Count == 1 ? streams[0] : null;
    }

    private static DictionaryToken? FindInheritedResources(
        DictionaryToken pageDictionary,
        Func<IToken, IToken?> resolver)
    {
        var current = pageDictionary;
        var visitedParents = new HashSet<string>(StringComparer.Ordinal);

        for (var depth = 0; depth < MaximumParentDepth; depth++)
        {
            var resources = ResolveDictionary(Get(current, "Resources"), resolver);
            if (resources is not null)
                return resources;

            var parentToken = Get(current, "Parent");
            if (parentToken is null)
                return null;

            if (parentToken is IndirectReferenceToken reference
                && !visitedParents.Add(reference.Data.ToString()))
            {
                return null;
            }

            var parent = ResolveDictionary(parentToken, resolver);
            if (parent is null)
                return null;
            current = parent;
        }

        return null;
    }

    private static IToken? ResolveDocumentToken(
        PdfDocument document,
        IToken token)
    {
        var current = token;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; depth < MaximumReferenceDepth; depth++)
        {
            if (current is not IndirectReferenceToken reference)
                return current;

            if (!visited.Add(reference.Data.ToString()))
                return null;

            current = document.Structure.GetObject(reference.Data).Data;
        }

        return null;
    }

    private static IToken? Resolve(
        IToken? token,
        Func<IToken, IToken?> resolver)
    {
        if (token is null)
            return null;

        var current = token;
        for (var depth = 0; depth < MaximumReferenceDepth; depth++)
        {
            if (current is not IndirectReferenceToken)
                return current;

            var next = resolver(current);
            if (next is null || ReferenceEquals(next, current))
                return null;
            current = next;
        }

        return null;
    }

    private static DictionaryToken? ResolveDictionary(
        IToken? token,
        Func<IToken, IToken?> resolver)
        => Resolve(token, resolver) as DictionaryToken;

    private static ArrayToken? ResolveArray(
        IToken? token,
        Func<IToken, IToken?> resolver)
        => Resolve(token, resolver) as ArrayToken;

    private static string? ResolveName(
        IToken? token,
        Func<IToken, IToken?> resolver)
        => (Resolve(token, resolver) as NameToken)?.Data;

    private static IToken? Get(
        DictionaryToken dictionary,
        string name)
        => dictionary.Data.TryGetValue(name, out var token)
            ? token
            : null;

    private static void AddName(
        DictionaryToken dictionary,
        string key,
        ISet<string> output)
    {
        if (Get(dictionary, key) is NameToken name
            && !string.IsNullOrWhiteSpace(name.Data))
        {
            output.Add(name.Data);
        }
    }

    internal sealed record PdfFontProgramIdentity(
        string Sha256,
        string FontSubtype,
        string EncodingName,
        bool HasToUnicode,
        bool IsSubset);

    private sealed record FontProgramCandidate(
        IReadOnlyList<string> ExactNames,
        PdfFontProgramIdentity? Identity);
}
