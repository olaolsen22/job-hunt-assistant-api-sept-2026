using System.Linq.Expressions;
using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;

namespace JobHunt.Api.Features.Listings;

[QueryType]
public static class ListingQueries
{
    // Undated ads still close; treat them as due this long after posting.
    private const int AsapDays = 14;
    private const int UnspecifiedDays = 30;

    // The effective due date, in one place so the default order, dueWithinDays and dueOn can't disagree.
    // Member init (not a bare DateOnly lambda) so EF can translate x.Due in the Where/OrderBy that follow.
    private static readonly Expression<Func<Listing, ListingWithDue>> WithDue = l => new ListingWithDue
    {
        Listing = l,
        Due = l.DeadlineType == DeadlineType.Date ? l.DeadlineOn!.Value
            : l.DeadlineType == DeadlineType.Asap ? l.PostedOn.AddDays(AsapDays)
            : l.PostedOn.AddDays(UnspecifiedDays),
    };

    private static readonly Func<Listing, ListingWithDue> CompiledWithDue = WithDue.Compile();

    internal static DateOnly DueOn(Listing listing) => CompiledWithDue(listing).Due;

    // Offset paging (skip/take), not cursors: the UI shows numbered pages and jumps straight to page N.
    [UseOffsetPaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Listing> GetListings(
        AppDbContext db, ISortingContext sorting, TimeProvider time, bool archived = false, int? dueWithinDays = null,
        string? search = null)
    {
        // Hot Chocolate adds a client's `order` as ThenBy, so a fixed OrderBy here would always win.
        // Default order only without one; with one, Id breaks ties so equal values (e.g. no deadline)
        // don't shuffle between pages. Handled(false): taking ISortingContext turns off automatic sorting otherwise.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        sorting.Handled(false);
        sorting.OnAfterSortingApplied<IQueryable<Listing>>((sortingApplied, query) =>
            sortingApplied && query is IOrderedQueryable<Listing> ordered
                ? ordered.ThenByDescending(l => l.Id)
                : ByClosestDeadline(query, today));

        var query = db.Listings.Where(l => (l.ArchivedAt != null) == archived);
        if (dueWithinDays is { } days)
        {
            if (days < 0)
                throw new GraphQLException("dueWithinDays must be 0 or more.");
            var last = today.AddDays(days);
            query = query.Select(WithDue).Where(x => x.Due >= today && x.Due <= last).Select(x => x.Listing);
        }
        // Normalized columns, not the built-in `contains` filter: that one is case-sensitive on SQLite.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Names.Normalize(search);
            query = query.Where(l => l.NormalizedTitle.Contains(term)
                || l.Company.NormalizedName.Contains(term)
                || l.JobRole.NormalizedName.Contains(term));
        }
        return query;
    }

    // Upcoming soonest first, then expired (also soonest first).
    private static IQueryable<Listing> ByClosestDeadline(IQueryable<Listing> query, DateOnly today) =>
        query
            .Select(WithDue)
            .OrderBy(x => x.Due < today)
            .ThenBy(x => x.Due)
            .ThenByDescending(x => x.Listing.CreatedAt)
            .ThenByDescending(x => x.Listing.Id)
            .Select(x => x.Listing);

    [UseFirstOrDefault]
    [UseProjection]
    public static IQueryable<Listing> GetListingById(int id, AppDbContext db) =>
        db.Listings.Where(l => l.Id == id);
}

[ObjectType<Listing>]
public static partial class ListingNode
{
    static partial void Configure(IObjectTypeDescriptor<Listing> descriptor)
    {
        descriptor.Ignore(l => l.UserId);
        descriptor.Ignore(l => l.NormalizedTitle);
        // Ordered by the client: the timeline by occurredAt, interviews by scheduledAt.
        descriptor.Field(l => l.StatusHistory).UseSorting();
        descriptor.Field(l => l.Interviews).UseSorting();
        // dueOn needs these even when the client didn't select them.
        descriptor.Field(l => l.DeadlineType).IsProjected();
        descriptor.Field(l => l.PostedOn).IsProjected();
        descriptor.Field(l => l.DeadlineOn).IsProjected();
    }

    // Same rule the server sorts and filters on, so the UI's badges can't disagree with the list.
    public static DateOnly GetDueOn([Parent] Listing listing) => ListingQueries.DueOn(listing);
}

internal sealed class ListingWithDue
{
    public Listing Listing { get; init; } = null!;
    public DateOnly Due { get; init; }
}
