using System.Text;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

public class GitPackFileTests {
  [Fact]
  public void ReadObjects_BaseObjects_ReturnsVerifiedObjects() {
    // Arrange
    byte[] blob = Encoding.UTF8.GetBytes("hello\n");
    string blobId = PackBuilder.ObjectId(GitObjectType.Blob, blob);
    byte[] tree = PackBuilder.Tree(("100644", "hello.txt", blobId));
    string treeId = PackBuilder.ObjectId(GitObjectType.Tree, tree);
    byte[] commit = PackBuilder.Commit(treeId);

    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Commit, commit);
    builder.AddObject(GitObjectType.Tree, tree);
    builder.AddObject(GitObjectType.Blob, blob);

    // Act
    using GitPackFile pack = GitPackFile.Open(new MemoryStream(builder.Build()));
    List<GitObject> objects = [.. pack.ReadObjects()];

    // Assert
    Assert.Equal(3, pack.Count);
    Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", blobId);
    Assert.Equal(
        [GitObjectType.Commit, GitObjectType.Tree, GitObjectType.Blob],
        objects.Select(o => o.Type));
    Assert.Equal(blob, objects[2].Content);
    Assert.Equal(blobId, objects[2].ObjectId);
    Assert.Equal(treeId, GitObject.ParseCommitTree(objects[0].Content));
  }

  [Fact]
  public void ReadObjects_OfsAndRefDeltaChain_ResolvesContent() {
    // Arrange
    byte[] baseBlob = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog.\n");
    byte[] second = Encoding.UTF8.GetBytes("The quick brown cat jumps over the lazy dog.\n");
    byte[] third = Encoding.UTF8.GetBytes("A quick brown cat jumps over the lazy dog!!\n");
    string baseId = PackBuilder.ObjectId(GitObjectType.Blob, baseBlob);
    string secondId = PackBuilder.ObjectId(GitObjectType.Blob, second);
    string thirdId = PackBuilder.ObjectId(GitObjectType.Blob, third);

    byte[] delta1 = PackBuilder.Delta(baseBlob.Length, second.Length,
        (0, 16), "cat"u8.ToArray(), (19, baseBlob.Length - 19));
    byte[] delta2 = PackBuilder.Delta(second.Length, third.Length,
        "A"u8.ToArray(), (3, second.Length - 5), "!!\n"u8.ToArray());

    PackBuilder builder = new();
    // REF_DELTA before its base is legal and must be deferred.
    builder.AddRefDelta(secondId, delta2);
    long baseOffset = builder.AddObject(GitObjectType.Blob, baseBlob);
    builder.AddOfsDelta(baseOffset, delta1);

    // Act
    using GitPackFile pack = GitPackFile.Open(new MemoryStream(builder.Build()));
    Dictionary<string, GitObject> objects = pack.ReadObjects().ToDictionary(o => o.ObjectId);

    // Assert
    Assert.Equal(3, objects.Count);
    Assert.Equal(baseBlob, objects[baseId].Content);
    Assert.Equal(second, objects[secondId].Content);
    Assert.Equal(third, objects[thirdId].Content);
    Assert.Equal(third, pack.FindObject(thirdId)!.Content);
    Assert.Null(pack.FindObject(new string('0', 40)));
  }

  [Fact]
  public void ReadObjects_OfsDeltaOnDeferredRefDelta_Resolves() {
    // Arrange
    byte[] baseBlob = Encoding.UTF8.GetBytes("base content;");
    byte[] middle = Encoding.UTF8.GetBytes("base content;middle;");
    byte[] top = Encoding.UTF8.GetBytes("base content;middle;top;");
    string baseId = PackBuilder.ObjectId(GitObjectType.Blob, baseBlob);

    PackBuilder builder = new();
    long middleOffset = builder.AddRefDelta(baseId, PackBuilder.Delta(
        baseBlob.Length, middle.Length, (0, baseBlob.Length), "middle;"u8.ToArray()));
    builder.AddOfsDelta(middleOffset, PackBuilder.Delta(
        middle.Length, top.Length, (0, middle.Length), "top;"u8.ToArray()));
    builder.AddObject(GitObjectType.Blob, baseBlob);

    // Act
    using GitPackFile pack = GitPackFile.Open(new MemoryStream(builder.Build()));
    List<byte[]> contents = [.. pack.ReadObjects().Select(o => o.Content)];

    // Assert
    Assert.Equal(3, contents.Count);
    Assert.Contains(top, contents);
    Assert.Contains(middle, contents);
  }

  [Fact]
  public void ReadObjects_MissingRefDeltaBase_Throws() {
    PackBuilder builder = new();
    builder.AddRefDelta(new string('1', 40), PackBuilder.Delta(1, 1, (0, 1)));

    using GitPackFile pack = GitPackFile.Open(new MemoryStream(builder.Build()));

    Assert.Throws<GitProtocolException>(() => pack.ReadObjects().ToList());
  }

  [Fact]
  public void Open_LargeBlob_Works() {
    byte[] blob = new byte[20 * 1024 * 1024];
    new Random(42).NextBytes(blob.AsSpan(0, blob.Length / 2));
    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Blob, blob);

    using GitPackFile pack = GitPackFile.Open(new MemoryStream(builder.Build()));
    GitObject obj = Assert.Single(pack.ReadObjects());

    Assert.Equal(blob, obj.Content);
  }

  [Fact]
  public void Open_ChecksumMismatch_Throws() {
    PackBuilder builder = new();
    builder.AddObject(GitObjectType.Blob, [1, 2, 3]);
    byte[] bytes = builder.Build();
    bytes[^1] ^= 0xFF;

    Assert.Throws<GitProtocolException>(() => GitPackFile.Open(new MemoryStream(bytes)));
  }

  [Fact]
  public void Open_BadSignature_Throws() {
    byte[] bytes = new PackBuilder().Build();
    bytes[0] = (byte)'X';

    Assert.Throws<GitProtocolException>(() => GitPackFile.Open(new MemoryStream(bytes)));
  }

  [Fact]
  public void ApplyDelta_CopyAndInsert_ProducesResult() {
    byte[] baseContent = Encoding.ASCII.GetBytes("0123456789");
    byte[] delta = PackBuilder.Delta(10, 7, (5, 3), "ab"u8.ToArray(), (0, 2));

    Assert.Equal("567ab01", Encoding.ASCII.GetString(GitPackFile.ApplyDelta(baseContent, delta)));
  }

  [Fact]
  public void ApplyDelta_OutOfRangeCopy_Throws() {
    byte[] delta = PackBuilder.Delta(10, 5, (8, 5));

    Assert.Throws<GitProtocolException>(() => GitPackFile.ApplyDelta(new byte[10], delta));
  }

  [Fact]
  public void ParseTree_ReturnsModesAsOctalDigits() {
    string id = new('a', 40);
    byte[] tree = PackBuilder.Tree(("100644", "a", id), ("40000", "dir", id), ("120000", "link", id));

    List<(int Mode, string Name, string ObjectId)> entries = [.. GitObject.ParseTree(tree)];

    Assert.Equal([(100644, "a", id), (40000, "dir", id), (120000, "link", id)], entries);
  }
}
