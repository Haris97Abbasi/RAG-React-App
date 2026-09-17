using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using PolicyLens.Api.Models;

namespace PolicyLens.Api.Services;

/// <summary>
/// A minimal, hand-rolled <see cref="VectorStoreCollection{TKey, TRecord}"/> for <see cref="PolicyChunk"/>,
/// backed directly by Microsoft.Data.Sqlite.
///
/// Written in place of the official Microsoft.SemanticKernel.Connectors.SqliteVec connector because that
/// package (latest: 1.74.0-preview) is compiled against Microsoft.Extensions.VectorData.Abstractions 10.1.0
/// and throws MissingMethodException on VectorSearchOptions&lt;TRecord&gt; under anything newer, while
/// Microsoft.Agents.AI 1.21.0 requires Abstractions >= 10.10.0. No version of either package satisfies both
/// at once (confirmed by testing 10.1.0, 10.7.0 and 10.10.0 against SqliteVec 1.74.0-preview).
///
/// Vectors are stored as raw float32 blobs; similarity search is brute-force cosine distance in memory,
/// which is trivial at this scale (12 policy chunks).
/// </summary>
public sealed class SqlitePolicyChunkCollection : VectorStoreCollection<string, PolicyChunk>
{
    private readonly string _connectionString;

    public SqlitePolicyChunkCollection(string connectionString, string name)
    {
        _connectionString = connectionString;
        Name = name;
    }

    public override string Name { get; }

    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name";
        command.Parameters.AddWithValue("@name", Name);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{Name}" (
                id TEXT PRIMARY KEY,
                section_number INTEGER NOT NULL,
                section_title TEXT NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP TABLE IF EXISTS "{Name}" """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task<PolicyChunk?> GetAsync(
        string key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT id, section_number, section_title, text, embedding FROM "{Name}" WHERE id = @id""";
        command.Parameters.AddWithValue("@id", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    public override async IAsyncEnumerable<PolicyChunk> GetAsync(
        Expression<Func<PolicyChunk, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<PolicyChunk>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var predicate = filter.Compile();
        var matched = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT id, section_number, section_title, text, embedding FROM "{Name}" """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (matched < top && await reader.ReadAsync(cancellationToken))
        {
            var record = ReadRecord(reader);
            if (predicate(record))
            {
                matched++;
                yield return record;
            }
        }
    }

    public override async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DELETE FROM "{Name}" WHERE id = @id""";
        command.Parameters.AddWithValue("@id", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task UpsertAsync(PolicyChunk record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO "{Name}" (id, section_number, section_title, text, embedding)
            VALUES (@id, @sectionNumber, @sectionTitle, @text, @embedding)
            ON CONFLICT(id) DO UPDATE SET
                section_number = excluded.section_number,
                section_title = excluded.section_title,
                text = excluded.text,
                embedding = excluded.embedding
            """;
        command.Parameters.AddWithValue("@id", record.Id);
        command.Parameters.AddWithValue("@sectionNumber", record.SectionNumber);
        command.Parameters.AddWithValue("@sectionTitle", record.SectionTitle);
        command.Parameters.AddWithValue("@text", record.Text);
        command.Parameters.AddWithValue("@embedding", VectorToBytes(record.Embedding));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task UpsertAsync(IEnumerable<PolicyChunk> records, CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            await UpsertAsync(record, cancellationToken);
        }
    }

    public override async IAsyncEnumerable<VectorSearchResult<PolicyChunk>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<PolicyChunk>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (searchValue is not ReadOnlyMemory<float> queryVector)
        {
            throw new NotSupportedException(
                $"{nameof(SqlitePolicyChunkCollection)} only supports searching by a {nameof(ReadOnlyMemory<float>)} vector.");
        }

        var allRecords = new List<PolicyChunk>();
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT id, section_number, section_title, text, embedding FROM "{Name}" """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                allRecords.Add(ReadRecord(reader));
            }
        }

        var ranked = allRecords
            .Select(record => (Record: record, Distance: CosineDistance(queryVector.Span, record.Embedding.Span)))
            .OrderBy(x => x.Distance)
            .Take(top);

        foreach (var (record, distance) in ranked)
        {
            yield return new VectorSearchResult<PolicyChunk>(record, 1 - distance);
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    private static PolicyChunk ReadRecord(SqliteDataReader reader)
    {
        var embeddingBytes = (byte[])reader["embedding"];
        var floats = new float[embeddingBytes.Length / sizeof(float)];
        Buffer.BlockCopy(embeddingBytes, 0, floats, 0, embeddingBytes.Length);

        return new PolicyChunk
        {
            Id = reader.GetString(0),
            SectionNumber = reader.GetInt32(1),
            SectionTitle = reader.GetString(2),
            Text = reader.GetString(3),
            Embedding = floats,
        };
    }

    private static byte[] VectorToBytes(ReadOnlyMemory<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, magnitudeA = 0, magnitudeB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var cosineSimilarity = dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        return (float)(1 - cosineSimilarity);
    }
}
