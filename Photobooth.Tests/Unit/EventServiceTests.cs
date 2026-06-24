using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Photobooth.Data;
using Photobooth.Data.Repositories;
using Photobooth.Services;
using Xunit;

namespace Photobooth.Tests.Unit;

/// <summary>
/// Integration-style unit tests for EventService.
/// Uses a real SQLite in-memory database so EF Core constraints and
/// query-filter behaviour are exercised without touching the filesystem.
/// IFileStorageService is mocked — file I/O is not under test here.
/// </summary>
public sealed class EventServiceTests : IDisposable
{
    private readonly SqliteConnection      _connection;
    private readonly PhotoboothDbContext   _db;
    private readonly EventRepository       _repo;
    private readonly Mock<IFileStorageService> _storageMock;
    private readonly EventService          _sut;

    public EventServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PhotoboothDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db   = new PhotoboothDbContext(options);
        _db.Database.EnsureCreated();

        _repo = new EventRepository(_db);

        _storageMock = new Mock<IFileStorageService>();
        // Return a non-empty StorageRoot so Create() validation passes.
        _storageMock.Setup(s => s.StorageRoot).Returns(@"C:\FakeStorage");
        // CreateEventFolders is a void — default mock behaviour (no-op) is fine.

        _sut = new EventService(_repo, _storageMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Create
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_PersistsEventToDatabase()
    {
        _sut.Create("Summer Bash", paywallEnabled: false, saveImagesEnabled: true,
                    printLimitPerEvent: null, printLimitPerSession: null);

        var events = _db.Events.ToList();
        Assert.Single(events);
        Assert.Equal("Summer Bash", events[0].Name);
    }

    [Fact]
    public void Create_GeneratesSlug()
    {
        var ev = _sut.Create("Winter Gala 2026", false, true, null, null);

        Assert.False(string.IsNullOrWhiteSpace(ev.Slug));
        // Slug should be lower-case and contain the words from the name
        Assert.Contains("winter", ev.Slug);
        Assert.Contains("gala", ev.Slug);
    }

    [Fact]
    public void Create_CallsCreateEventFolders()
    {
        var ev = _sut.Create("Folder Test", false, true, null, null);

        _storageMock.Verify(s => s.CreateEventFolders(ev.Slug), Times.Once);
    }

    [Fact]
    public void Create_ThrowsWhenNameIsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => _sut.Create("  ", false, true, null, null));
    }

    [Fact]
    public void Create_ThrowsWhenStorageRootIsEmpty()
    {
        var emptyStorageMock = new Mock<IFileStorageService>();
        emptyStorageMock.Setup(s => s.StorageRoot).Returns(string.Empty);
        var svc = new EventService(_repo, emptyStorageMock.Object);

        Assert.Throws<InvalidOperationException>(
            () => svc.Create("No Storage", false, true, null, null));
    }

    [Fact]
    public void Create_StoresPaywallAndLimitSettings()
    {
        var ev = _sut.Create("Paid Event", paywallEnabled: true, saveImagesEnabled: false,
                             printLimitPerEvent: 100, printLimitPerSession: 3);

        Assert.True(ev.PaywallEnabled);
        Assert.False(ev.SaveImagesEnabled);
        Assert.Equal(100, ev.PrintLimitPerEvent);
        Assert.Equal(3, ev.PrintLimitPerSession);
    }

    // -------------------------------------------------------------------------
    // GetActive / GetById
    // -------------------------------------------------------------------------

    [Fact]
    public void GetActive_ReturnsAllNonArchivedEvents()
    {
        _sut.Create("Event A", false, true, null, null);
        _sut.Create("Event B", false, true, null, null);

        var active = _sut.GetActive();
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public void GetActive_ExcludesArchivedEvents()
    {
        var ev = _sut.Create("Archived Event", false, true, null, null);
        _sut.Archive(ev.Id);

        var active = _sut.GetActive();
        Assert.Empty(active);
    }

    [Fact]
    public void GetById_ReturnsCorrectEvent()
    {
        var created = _sut.Create("Lookup Event", false, true, null, null);

        var found = _sut.GetById(created.Id);
        Assert.NotNull(found);
        Assert.Equal("Lookup Event", found!.Name);
    }

    [Fact]
    public void GetById_ReturnsNullForMissingId()
    {
        var result = _sut.GetById(9999);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // UpdateDetails
    // -------------------------------------------------------------------------

    [Fact]
    public void UpdateDetails_ChangesName()
    {
        var ev = _sut.Create("Old Name", false, true, null, null);

        _sut.UpdateDetails(ev.Id, "New Name", true, false, 50, 2);

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal("New Name", updated!.Name);
        Assert.True(updated.PaywallEnabled);
        Assert.False(updated.SaveImagesEnabled);
        Assert.Equal(50, updated.PrintLimitPerEvent);
        Assert.Equal(2, updated.PrintLimitPerSession);
    }

    [Fact]
    public void UpdateDetails_ThrowsWhenNameIsEmpty()
    {
        var ev = _sut.Create("Valid Name", false, true, null, null);

        Assert.Throws<ArgumentException>(
            () => _sut.UpdateDetails(ev.Id, "", false, true, null, null));
    }

    // -------------------------------------------------------------------------
    // Appearance setters
    // -------------------------------------------------------------------------

    [Fact]
    public void SetAccentColor_PersistsValue()
    {
        var ev = _sut.Create("Color Test", false, true, null, null);
        _sut.SetAccentColor(ev.Id, "#FF0000");

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal("#FF0000", updated!.AccentColor);
    }

    [Fact]
    public void SetBackgroundColor_PersistsValue()
    {
        var ev = _sut.Create("BG Color", false, true, null, null);
        _sut.SetBackgroundColor(ev.Id, "#00FF00");

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal("#00FF00", updated!.BackgroundColor);
    }

    [Fact]
    public void SetSurfaceColor_PersistsValue()
    {
        var ev = _sut.Create("Surface Color", false, true, null, null);
        _sut.SetSurfaceColor(ev.Id, "#0000FF");

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal("#0000FF", updated!.SurfaceColor);
    }

    [Fact]
    public void SetBackgroundImagePath_PersistsValue()
    {
        var ev = _sut.Create("BG Image", false, true, null, null);
        _sut.SetBackgroundImagePath(ev.Id, @"C:\bg.jpg");

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal(@"C:\bg.jpg", updated!.BackgroundImagePath);
    }

    [Fact]
    public void SetPhotostripTemplatePath_PersistsValue()
    {
        var ev = _sut.Create("Strip Template", false, true, null, null);
        _sut.SetPhotostripTemplatePath(ev.Id, @"C:\template.png");

        var updated = _db.Events.Find(ev.Id);
        Assert.Equal(@"C:\template.png", updated!.PhotostripTemplatePath);
    }

    // -------------------------------------------------------------------------
    // Archive
    // -------------------------------------------------------------------------

    [Fact]
    public void Archive_SetsArchivedAt()
    {
        var ev = _sut.Create("To Archive", false, true, null, null);
        _sut.Archive(ev.Id);

        // IgnoreQueryFilters so the soft-delete filter doesn't hide the row.
        var archived = _db.Events.IgnoreQueryFilters().First(e => e.Id == ev.Id);
        Assert.NotNull(archived.ArchivedAt);
    }

    [Fact]
    public void Archive_HidesEventFromGetActive()
    {
        var ev = _sut.Create("Hide Me", false, true, null, null);
        _sut.Archive(ev.Id);

        Assert.Empty(_sut.GetActive());
    }

    // -------------------------------------------------------------------------
    // Session lifecycle
    // -------------------------------------------------------------------------

    [Fact]
    public void StartSession_CreatesSessionForEvent()
    {
        var ev      = _sut.Create("Session Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);

        Assert.True(session.Id > 0);
        Assert.Equal(ev.Id, session.EventId);
    }

    [Fact]
    public void AbandonSession_RemovesSession()
    {
        var ev      = _sut.Create("Abandon Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);

        _sut.AbandonSession(session.Id);

        Assert.Null(_db.Sessions.Find(session.Id));
    }

    [Fact]
    public void RecordPhoto_AddsPhotoToSession()
    {
        var ev      = _sut.Create("Photo Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);

        _sut.RecordPhoto(session.Id, 1, @"C:\photo1.jpg");

        var photos = _db.Photos.Where(p => p.SessionId == session.Id).ToList();
        Assert.Single(photos);
        Assert.Equal(1, photos[0].Sequence);
        Assert.Equal(@"C:\photo1.jpg", photos[0].FilePath);
    }

    // -------------------------------------------------------------------------
    // ClearSessions
    // -------------------------------------------------------------------------

    [Fact]
    public void ClearSessions_RemovesAllSessionsForEvent()
    {
        var ev = _sut.Create("Clear Event", false, true, null, null);
        _sut.StartSession(ev.Id);
        _sut.StartSession(ev.Id);

        _sut.ClearSessions(ev.Id);

        var remaining = _db.Sessions.Where(s => s.EventId == ev.Id).ToList();
        Assert.Empty(remaining);
    }

    // -------------------------------------------------------------------------
    // GetStats
    // -------------------------------------------------------------------------

    [Fact]
    public void GetStats_ReturnsZerosForEmptyEvent()
    {
        var ev    = _sut.Create("Stats Event", false, true, null, null);
        var stats = _sut.GetStats(ev.Id);

        Assert.Equal(0, stats.Sessions);
        Assert.Equal(0, stats.Photos);
        Assert.Equal(0, stats.Prints);
        Assert.Equal(0, stats.AIGenerations);
    }

    [Fact]
    public void GetStats_CountsSessionsAndPhotos()
    {
        var ev      = _sut.Create("Stats Full Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);
        _sut.RecordPhoto(session.Id, 1, @"C:\p1.jpg");
        _sut.RecordPhoto(session.Id, 2, @"C:\p2.jpg");

        var stats = _sut.GetStats(ev.Id);

        Assert.Equal(1, stats.Sessions);
        Assert.Equal(2, stats.Photos);
    }

    // -------------------------------------------------------------------------
    // Print limits
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordPrint_IncrementsPrintCount()
    {
        var ev      = _sut.Create("Print Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);

        _sut.RecordPrint(session.Id, copies: 2);

        Assert.Equal(2, _sut.GetSessionPrintCount(session.Id));
    }

    [Fact]
    public void RecordPrint_ThrowsWhenEventLimitExceeded()
    {
        var ev      = _sut.Create("Limited Event", false, true,
                                   printLimitPerEvent: 1, printLimitPerSession: null);
        var session = _sut.StartSession(ev.Id);

        // First print — within the limit
        _sut.RecordPrint(session.Id, copies: 1);

        // Second print — exceeds the event limit
        Assert.Throws<InvalidOperationException>(
            () => _sut.RecordPrint(session.Id, copies: 1));
    }

    [Fact]
    public void RecordPrint_ThrowsWhenSessionLimitExceeded()
    {
        var ev      = _sut.Create("Session Limited", false, true,
                                   printLimitPerEvent: null, printLimitPerSession: 2);
        var session = _sut.StartSession(ev.Id);

        _sut.RecordPrint(session.Id, copies: 2);

        Assert.Throws<InvalidOperationException>(
            () => _sut.RecordPrint(session.Id, copies: 1));
    }

    [Fact]
    public void GetEventPrintCount_ReturnsTotalAcrossSessions()
    {
        var ev = _sut.Create("Multi Session Print", false, true, null, null);
        var s1 = _sut.StartSession(ev.Id);
        var s2 = _sut.StartSession(ev.Id);

        _sut.RecordPrint(s1.Id, copies: 3);
        _sut.RecordPrint(s2.Id, copies: 2);

        Assert.Equal(5, _sut.GetEventPrintCount(ev.Id));
    }

    // -------------------------------------------------------------------------
    // GenerateSlug (static helper)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Hello World",   "hello-world")]
    [InlineData("Test 123",      "test-123")]
    [InlineData("Special! Chars","special-chars")]
    public void GenerateSlug_ProducesLowercaseDashSeparated(string input, string expectedPrefix)
    {
        var slug = EventService.GenerateSlug(input);
        Assert.StartsWith(expectedPrefix, slug);
    }

    [Fact]
    public void GenerateSlug_AppendsSuffix()
    {
        // The implementation appends "-{year}" to the slug.
        var slug = EventService.GenerateSlug("My Event");
        Assert.EndsWith($"-{DateTime.UtcNow.Year}", slug);
    }

    // -------------------------------------------------------------------------
    // GetRecentPhotos / GetSessionsWithPhotos
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRecentPhotos_ReturnsUpToRequestedCount()
    {
        var ev      = _sut.Create("Gallery Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);
        _sut.RecordPhoto(session.Id, 1, @"C:\p1.jpg");
        _sut.RecordPhoto(session.Id, 2, @"C:\p2.jpg");
        _sut.RecordPhoto(session.Id, 3, @"C:\p3.jpg");

        var photos = _sut.GetRecentPhotos(ev.Id, count: 2);
        Assert.Equal(2, photos.Count);
    }

    [Fact]
    public void GetSessionsWithPhotos_ReturnsSessionsWithEagerLoadedPhotos()
    {
        var ev      = _sut.Create("Eager Load Event", false, true, null, null);
        var session = _sut.StartSession(ev.Id);
        _sut.RecordPhoto(session.Id, 1, @"C:\p1.jpg");

        var sessions = _sut.GetSessionsWithPhotos(ev.Id);

        Assert.Single(sessions);
        Assert.Single(sessions[0].Photos);
    }

    // -------------------------------------------------------------------------
    // GetRecent
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRecent_ReturnsNewestFirstUpToCount()
    {
        _sut.Create("Alpha", false, true, null, null);
        _sut.Create("Beta",  false, true, null, null);
        _sut.Create("Gamma", false, true, null, null);

        var recent = _sut.GetRecent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Gamma", recent[0].Name); // newest first
    }

    [Fact]
    public void GetRecent_ExcludesArchivedEvents()
    {
        var ev = _sut.Create("Old", false, true, null, null);
        _sut.Archive(ev.Id);
        _sut.Create("New", false, true, null, null);

        var recent = _sut.GetRecent(10);

        Assert.Single(recent);
        Assert.Equal("New", recent[0].Name);
    }

    // -------------------------------------------------------------------------
    // QueryEvents
    // -------------------------------------------------------------------------

    [Fact]
    public void QueryEvents_ReturnsAllActiveWhenNoFilter()
    {
        _sut.Create("A", false, true, null, null);
        _sut.Create("B", false, true, null, null);

        var (events, total) = _sut.QueryEvents(new EventQuery());

        Assert.Equal(2, total);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void QueryEvents_FiltersBySearchCaseInsensitive()
    {
        _sut.Create("Summer Bash", false, true, null, null);
        _sut.Create("Winter Gala", false, true, null, null);

        var (events, total) = _sut.QueryEvents(new EventQuery { Search = "summer" });

        Assert.Equal(1, total);
        Assert.Equal("Summer Bash", events[0].Name);
    }

    [Fact]
    public void QueryEvents_ExcludesArchivedByDefault()
    {
        var archived = _sut.Create("Old", false, true, null, null);
        _sut.Archive(archived.Id);
        _sut.Create("Active", false, true, null, null);

        var (events, total) = _sut.QueryEvents(new EventQuery { IncludeArchived = false });

        Assert.Equal(1, total);
        Assert.Equal("Active", events[0].Name);
    }

    [Fact]
    public void QueryEvents_IncludesArchivedWhenFlagTrue()
    {
        var archived = _sut.Create("Old", false, true, null, null);
        _sut.Archive(archived.Id);
        _sut.Create("Active", false, true, null, null);

        var (events, total) = _sut.QueryEvents(new EventQuery { IncludeArchived = true });

        Assert.Equal(2, total);
    }

    [Fact]
    public void QueryEvents_SortsByNameAZ()
    {
        _sut.Create("Zebra", false, true, null, null);
        _sut.Create("Apple", false, true, null, null);

        var (events, _) = _sut.QueryEvents(new EventQuery { Sort = EventSortOrder.NameAZ });

        Assert.Equal("Apple", events[0].Name);
        Assert.Equal("Zebra", events[1].Name);
    }

    [Fact]
    public void QueryEvents_PaginatesCorrectly()
    {
        for (int i = 1; i <= 5; i++)
            _sut.Create($"Event {i}", false, true, null, null);

        var (page1, total) = _sut.QueryEvents(new EventQuery { Page = 1, PageSize = 3 });
        var (page2, _)     = _sut.QueryEvents(new EventQuery { Page = 2, PageSize = 3 });

        Assert.Equal(5, total);
        Assert.Equal(3, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [Fact]
    public void QueryEvents_TotalCountIsBeforePagination()
    {
        for (int i = 1; i <= 10; i++)
            _sut.Create($"Event {i}", false, true, null, null);

        var (events, total) = _sut.QueryEvents(new EventQuery { Page = 1, PageSize = 4 });

        Assert.Equal(10, total);
        Assert.Equal(4, events.Count);
    }
}
