using HotChocolate.Data.Sorting;

namespace JobHunt.Api.Data;

public static class SortingDefaults
{
    // Hot Chocolate adds a client's `order` as ThenBy, so a default OrderBy must only apply without one.
    // Handled(false): taking ISortingContext turns off automatic sorting otherwise.
    public static IQueryable<T> WithDefaultOrder<T>(this ISortingContext sorting, IQueryable<T> query,
        Func<IQueryable<T>, IQueryable<T>> order)
    {
        sorting.Handled(false);
        return sorting.IsDefined ? query : order(query);
    }
}
