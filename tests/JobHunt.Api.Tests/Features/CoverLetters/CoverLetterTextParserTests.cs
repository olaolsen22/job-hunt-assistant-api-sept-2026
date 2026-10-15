using JobHunt.Api.Features.CoverLetters;
using JobHunt.Api.Features.Listings;

namespace JobHunt.Api.Tests.Features.CoverLetters;

public class CoverLetterTextParserTests
{
    private static readonly CoverLetterContent Header = new(Language.Norwegian, FullName: "Alex Berg",
        Phone: "+47 00000000", Email: "alex.berg@example.com", Organization: "Eksempel Produktutvikling AS",
        Subject: "Søknad på stilling som utvikler", Closing: "Vennlig hilsen");

    [Fact]
    public void Parse_FullLetterWithBlankLines_DropsHeaderAndSignature()
    {
        const string text = """
            Alex Berg                         
            [Gateadresse]
            Telefon: +47 00000000
            E-post: alex.berg@example.com

            Oslo, 15. oktober 2026

            Eksempel Produktutvikling AS
            [Postnummer og sted]

            Søknad på stilling som programvareutvikler

            Jeg søker stillingen fordi jeg ønsker å utvikle bestillingsløsninger.
            I utlysningen beskriver dere behov for stabile integrasjoner.

            Hos Nordvik har jeg utviklet ordre-API-er.

            Jeg utdyper gjerne dette i et intervju.

            Med vennlig hilsen
            Alex Berg
            """;

        var letter = CoverLetterTextParser.Parse(text, Header);

        Assert.Equal("Søknad på stilling som programvareutvikler", letter.Subject);
        Assert.Null(letter.Greeting);
        Assert.Equal([
            "Jeg søker stillingen fordi jeg ønsker å utvikle bestillingsløsninger. I utlysningen beskriver dere behov for stabile integrasjoner.",
            "Hos Nordvik har jeg utviklet ordre-API-er.",
            "Jeg utdyper gjerne dette i et intervju.",
        ], letter.Paragraphs);
        Assert.Equal("Med vennlig hilsen", letter.Closing);
        Assert.Equal("Alex Berg", letter.FullName);
    }

    [Fact]
    public void Parse_OneParagraphPerLine_KeepsGreetingAndHeaderSubject()
    {
        var letter = CoverLetterTextParser.Parse(
            "Dear Kari,\nI build reliable APIs.\nI would like to discuss it.\nKind regards,\nAlex", Header);

        Assert.Equal("Dear Kari,", letter.Greeting);
        Assert.Equal("Søknad på stilling som utvikler", letter.Subject);
        Assert.Equal(["I build reliable APIs.", "I would like to discuss it."], letter.Paragraphs);
        Assert.Equal("Kind regards", letter.Closing);
    }

    [Fact]
    public void Parse_ListItemsKeepTheirLines()
    {
        var letter = CoverLetterTextParser.Parse("Jeg tilbyr:\n- API-er\n- Testing.\n\nVennlig hilsen", Header);

        Assert.Equal(["Jeg tilbyr:\n- API-er\n- Testing."], letter.Paragraphs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Vennlig hilsen\nAlex")]
    public void Parse_NoBody_IsRejected(string text) =>
        Assert.Throws<InvalidCoverLetterException>(() => CoverLetterTextParser.Parse(text, Header));
}
