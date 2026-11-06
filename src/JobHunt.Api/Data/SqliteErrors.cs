using Microsoft.Data.Sqlite;

namespace JobHunt.Api.Data;

public static class SqliteErrors
{
    // SQLITE_BUSY (5), LOCKED (6) or CONSTRAINT (19): another request got the lock or the row first.
    public static bool IsContention(Exception ex) =>
        (ex as SqliteException ?? ex.InnerException as SqliteException) is { SqliteErrorCode: 5 or 6 or 19 };
}
