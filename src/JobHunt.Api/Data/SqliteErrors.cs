using Microsoft.Data.Sqlite;

namespace JobHunt.Api.Data;

public static class SqliteErrors
{
    // SQLITE_BUSY (5), SQLITE_LOCKED (6) or SQLITE_CONSTRAINT (19): another request got the write lock or the row
    // first. Raw from BeginTransaction, or wrapped in DbUpdateException by SaveChanges.
    public static bool IsContention(Exception ex) =>
        (ex as SqliteException ?? ex.InnerException as SqliteException) is { SqliteErrorCode: 5 or 6 or 19 };
}
