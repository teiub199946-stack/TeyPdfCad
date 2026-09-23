using System.Security.Cryptography;
using System.Text;
using TeyPdfCad.Pdf;
using UglyToad.PdfPig.Tokens;
using Xunit;

namespace TeyPdfCad.Pdf.Tests;

public sealed class PdfEmbeddedFontProgramCatalogTests
{
    [Fact]
    public void Exact_embedded_font_program_is_fingerprinted_from_decoded_stream_bytes()
    {
        var fontBytes = Encoding.ASCII.GetBytes("embedded-font-program-v1");
        var resources = Resources(
            ("F1", Font(
                "ABCDEF+ArialMT",
                Descriptor(
                    "ABCDEF+ArialMT",
                    new StreamToken(EmptyDictionary(), fontBytes)))));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(fontBytes)),
            catalog.ResolveUniqueSha256("ABCDEF+ArialMT"));
        Assert.Null(catalog.ResolveUniqueSha256("ArialMT"));
    }

    [Fact]
    public void Same_exact_name_with_embedded_and_unembedded_resources_is_ambiguous()
    {
        var fontBytes = Encoding.ASCII.GetBytes("embedded-font-program-v1");
        var resources = Resources(
            ("F1", Font(
                "ArialMT",
                Descriptor(
                    "ArialMT",
                    new StreamToken(EmptyDictionary(), fontBytes)))),
            ("F2", Font("ArialMT", descriptor: null)));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Null(catalog.ResolveUniqueSha256("ArialMT"));
    }

    [Fact]
    public void Same_exact_name_with_different_embedded_programs_is_ambiguous()
    {
        var resources = Resources(
            ("F1", Font(
                "ArialMT",
                Descriptor(
                    "ArialMT",
                    new StreamToken(
                        EmptyDictionary(),
                        Encoding.ASCII.GetBytes("program-a"))))),
            ("F2", Font(
                "ArialMT",
                Descriptor(
                    "ArialMT",
                    new StreamToken(
                        EmptyDictionary(),
                        Encoding.ASCII.GetBytes("program-b"))))));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Null(catalog.ResolveUniqueSha256("ArialMT"));
    }

    [Fact]
    public void Descriptor_with_multiple_font_program_entries_is_fail_closed()
    {
        var descriptor = Dictionary(
            ("Type", Name("FontDescriptor")),
            ("FontName", Name("ArialMT")),
            ("FontFile", new StreamToken(
                EmptyDictionary(),
                Encoding.ASCII.GetBytes("type1-program"))),
            ("FontFile2", new StreamToken(
                EmptyDictionary(),
                Encoding.ASCII.GetBytes("truetype-program"))));
        var resources = Resources(("F1", Font("ArialMT", descriptor)));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Null(catalog.ResolveUniqueSha256("ArialMT"));
    }

    [Fact]
    public void Type0_with_multiple_descendants_is_fail_closed()
    {
        var bytes = Encoding.ASCII.GetBytes("cid-font-program");
        var descendant1 = Font(
            "ArialUnicodeMS",
            Descriptor(
                "ArialUnicodeMS",
                new StreamToken(EmptyDictionary(), bytes)));
        var descendant2 = Font(
            "ArialUnicodeMS",
            Descriptor(
                "ArialUnicodeMS",
                new StreamToken(EmptyDictionary(), bytes)));
        var type0 = Dictionary(
            ("Type", Name("Font")),
            ("Subtype", Name("Type0")),
            ("BaseFont", Name("ArialUnicodeMS")),
            ("DescendantFonts", new ArrayToken([descendant1, descendant2])));
        var resources = Resources(("F1", type0));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Null(catalog.ResolveUniqueSha256("ArialUnicodeMS"));
    }

    [Fact]
    public void Type0_font_uses_descendant_font_descriptor_program()
    {
        var fontBytes = Encoding.ASCII.GetBytes("cid-font-program");
        var descendant = Font(
            "ABCDEF+ArialUnicodeMS",
            Descriptor(
                "ABCDEF+ArialUnicodeMS",
                new StreamToken(EmptyDictionary(), fontBytes)));
        var type0 = Dictionary(
            ("Type", Name("Font")),
            ("Subtype", Name("Type0")),
            ("BaseFont", Name("ABCDEF+ArialUnicodeMS")),
            ("DescendantFonts", new ArrayToken([descendant])));
        var resources = Resources(("F1", type0));

        var catalog = PdfEmbeddedFontProgramCatalog.CreateFromResources(
            resources,
            token => token);

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(fontBytes)),
            catalog.ResolveUniqueSha256("ABCDEF+ArialUnicodeMS"));
    }

    private static DictionaryToken Resources(
        params (string Key, IToken Font)[] fonts)
        => Dictionary(
            ("Font", new DictionaryToken(
                fonts.ToDictionary(
                    pair => Name(pair.Key),
                    pair => pair.Font))));

    private static DictionaryToken Font(
        string baseFont,
        DictionaryToken? descriptor)
    {
        var entries = new List<(string Key, IToken Value)>
        {
            ("Type", Name("Font")),
            ("Subtype", Name("TrueType")),
            ("BaseFont", Name(baseFont))
        };
        if (descriptor is not null)
            entries.Add(("FontDescriptor", descriptor));
        return Dictionary(entries.ToArray());
    }

    private static DictionaryToken Descriptor(
        string fontName,
        StreamToken fontFile)
        => Dictionary(
            ("Type", Name("FontDescriptor")),
            ("FontName", Name(fontName)),
            ("FontFile2", fontFile));

    private static DictionaryToken EmptyDictionary()
        => new(new Dictionary<NameToken, IToken>());

    private static DictionaryToken Dictionary(
        params (string Key, IToken Value)[] values)
        => new(values.ToDictionary(
            pair => Name(pair.Key),
            pair => pair.Value));

    private static NameToken Name(string value) => NameToken.Create(value);
}
