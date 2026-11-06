namespace JobHunt.Api.Features.ListingImports;

public class ListingImportOptions
{
    public const string Section = "ListingImports";

    // Relative paths resolve against the content root. Holds one subfolder per account email.
    public string Folder { get; set; } = "listing-imports";
    public int PollSeconds { get; set; } = 60;
}
