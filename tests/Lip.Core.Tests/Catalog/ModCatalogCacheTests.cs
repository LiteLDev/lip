using Lip.Core.Catalog;

namespace Lip.Core.Tests.Catalog;

/// What ModCatalogClient keeps in memory of the catalogue (protocol/lip-tui.md, "Catalogue"),
/// and how the calls for a file share its request.
public class ModCatalogCacheTests {
  private const string Index = """
      { "mods": [ { "id": "github.com/acme/Alpha", "name": "Alpha", "side": ["server"] } ] }
      """;

  private const string Detail = """
      { "id": "github.com/acme/Alpha", "versions": [ { "version": "1.0.0" } ] }
      """;

  private static readonly string IndexUrl = ModCatalogClient.BaseUrl + "index.json";
  private static readonly string DetailUrl = ModCatalogClient.DetailUrl("github.com/acme/Alpha");

  /// A catalogue whose requests are counted, and a clock that the test sets.
  private sealed class Catalogue {
    private readonly List<string> _urls = [];

    public long Now { get; set; } = 1_000;

    /// What a request does in place of answering, when given.
    public Func<string, CancellationToken, Task<string>>? Answer { get; set; }

    public ModCatalogClient Client => field ??= new ModCatalogClient(
        (url, token) => {
          lock (_urls) {
            _urls.Add(url);
          }

          return Answer?.Invoke(url, token)
              ?? Task.FromResult(url == IndexUrl ? Index : Detail);
        },
        () => Now);

    public int Requests(string url) {
      lock (_urls) {
        return _urls.Count(requested => requested == url);
      }
    }
  }

  private static CancellationToken Token => TestContext.Current.CancellationToken;

  [Fact]
  public async Task GetIndex_WithinTenMinutes_GivesWhatWasRead_AndReadsAgainAfterThat() {
    Catalogue catalogue = new();

    CatalogIndex first = await catalogue.Client.GetIndex(Token);
    catalogue.Now += (long)ModCatalogClient.StaleAfter.TotalMilliseconds;
    CatalogIndex second = await catalogue.Client.GetIndex(Token);

    // Ten minutes old is not stale yet: the same object, and one request.
    Assert.Same(first, second);
    Assert.Equal(1, catalogue.Requests(IndexUrl));

    catalogue.Now += 1;
    CatalogIndex third = await catalogue.Client.GetIndex(Token);

    Assert.NotSame(first, third);
    Assert.Equal(2, catalogue.Requests(IndexUrl));
  }

  [Fact]
  public async Task GetDetail_IsKeptById_WhateverItsCase_AndForTenMinutes() {
    Catalogue catalogue = new();

    CatalogDetail first = await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);
    CatalogDetail second = await catalogue.Client.GetDetail("github.com/ACME/alpha/", Token);

    Assert.Same(first, second);
    Assert.Equal(1, catalogue.Requests(DetailUrl));

    catalogue.Now += (long)ModCatalogClient.StaleAfter.TotalMilliseconds + 1;
    CatalogDetail third = await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);

    Assert.NotSame(first, third);
    Assert.Equal(2, catalogue.Requests(DetailUrl));
  }

  [Fact]
  public async Task GetDetail_IsReadAgain_OnceTheIndexWasReadAgain() {
    Catalogue catalogue = new();
    await catalogue.Client.GetIndex(Token);
    await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);

    // An index that is still kept leaves the files of the mods as they are.
    await catalogue.Client.GetIndex(Token);
    await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);
    Assert.Equal(1, catalogue.Requests(DetailUrl));

    // A new index names the mods anew: their files are read again. The file is not stale by
    // itself here, since it is read later than the index was.
    catalogue.Now += (long)ModCatalogClient.StaleAfter.TotalMilliseconds - 10;
    await catalogue.Client.GetDetail("github.com/acme/Beta", Token);
    catalogue.Now += 20;
    await catalogue.Client.GetIndex(Token);
    await catalogue.Client.GetDetail("github.com/acme/Beta", Token);

    Assert.Equal(2, catalogue.Requests(IndexUrl));
    Assert.Equal(2, catalogue.Requests(ModCatalogClient.DetailUrl("github.com/acme/Beta")));
  }

  [Fact]
  public async Task AnIndexThatCannotBeReadAgain_DropsNoFileOfAMod() {
    bool offline = false;
    Catalogue catalogue = new();
    catalogue.Answer = (url, _) => offline && url == IndexUrl
        ? Task.FromException<string>(new IOException("offline"))
        : Task.FromResult(url == IndexUrl ? Index : Detail);
    await catalogue.Client.GetIndex(Token);
    catalogue.Now += (long)ModCatalogClient.StaleAfter.TotalMilliseconds - 10;
    await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);
    catalogue.Now += 20;
    offline = true;

    await Assert.ThrowsAsync<CatalogUnavailableException>(
        () => catalogue.Client.GetIndex(Token));
    await catalogue.Client.GetDetail("github.com/acme/Alpha", Token);

    Assert.Equal(1, catalogue.Requests(DetailUrl));
  }

  [Fact]
  public async Task GetLoaderSupport_SharesTheFileOfLeviLamina() {
    Catalogue catalogue = new();
    string url = ModCatalogClient.DetailUrl("github.com/LiteLDev/LeviLamina");

    await catalogue.Client.GetLoaderSupport(Token);
    await catalogue.Client.GetDetail("github.com/LiteLDev/LeviLamina", Token);
    await catalogue.Client.GetLoaderSupport(Token);

    Assert.Equal(1, catalogue.Requests(url));
  }

  [Fact]
  public async Task AFileThatCannotBeRead_IsNotKept_TheNextCallReadsAgain() {
    bool offline = true;
    Catalogue catalogue = new() {
      Answer = (url, _) => offline
          ? Task.FromException<string>(new IOException("offline"))
          : Task.FromResult(url == IndexUrl ? Index : Detail),
    };

    await Assert.ThrowsAsync<CatalogUnavailableException>(
        () => catalogue.Client.GetIndex(Token));
    await Assert.ThrowsAsync<CatalogUnavailableException>(
        () => catalogue.Client.GetDetail("github.com/acme/Alpha", Token));
    offline = false;

    Assert.Single((await catalogue.Client.GetIndex(Token)).Mods);
    Assert.Equal("github.com/acme/Alpha", (await catalogue.Client.GetDetail(
        "github.com/acme/Alpha", Token)).Id);
    Assert.Equal(2, catalogue.Requests(IndexUrl));
    Assert.Equal(2, catalogue.Requests(DetailUrl));
  }

  [Fact]
  public async Task CallsWhileAFileIsRead_ShareTheOneRequest() {
    TaskCompletionSource<string> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Catalogue catalogue = new() { Answer = (_, _) => answer.Task };

    Task<CatalogIndex> first = catalogue.Client.GetIndex(Token);
    Task<CatalogIndex> second = catalogue.Client.GetIndex(Token);
    answer.SetResult(Index);

    Assert.Same(await first, await second);
    Assert.Equal(1, catalogue.Requests(IndexUrl));
  }

  [Fact]
  public async Task ACallThatIsCancelled_AbandonsTheRequest_OnceNoCallWaitsForIt() {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource abandoned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Catalogue catalogue = new() {
      Answer = async (_, token) => {
        token.Register(() => abandoned.TrySetResult());
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        return Detail;
      },
    };
    using CancellationTokenSource first = CancellationTokenSource.CreateLinkedTokenSource(Token);
    using CancellationTokenSource second = CancellationTokenSource.CreateLinkedTokenSource(Token);
    Task<CatalogDetail> one = catalogue.Client.GetDetail("github.com/acme/Alpha", first.Token);
    Task<CatalogDetail> two = catalogue.Client.GetDetail("github.com/acme/Alpha", second.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);

    // One of the two stops waiting: the other still waits, and the request goes on.
    await first.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => one);
    Assert.False(abandoned.Task.IsCompleted);
    Assert.False(two.IsCompleted);

    // The last one stops waiting: the request is abandoned.
    await second.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => two);
    await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);

    // What was abandoned answers no later call: the file is asked for again.
    catalogue.Answer = null;
    Assert.Equal("github.com/acme/Alpha", (await catalogue.Client.GetDetail(
        "github.com/acme/Alpha", Token)).Id);
    Assert.Equal(2, catalogue.Requests(DetailUrl));
  }
}
